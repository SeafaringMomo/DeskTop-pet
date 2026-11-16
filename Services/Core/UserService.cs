using System.IO;
using System.Linq;
using DesktopGent.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DesktopGent.Services.Core;

/// <summary>
/// 用户管理服务（单例）：管理用户注册表（user_data/users.yaml）与各用户档案
/// （user_data/users/&lt;name&gt;/profile.yaml）。
///
/// 职责：
/// - 列出所有已注册用户
/// - 创建新用户（初始化 profile 与数据目录）
/// - 加载/保存当前登录用户的档案
/// - 持久化用户偏好（当前角色、缩放、默认 AI 提供方）
///
/// 用户数据目录结构：
/// user_data/
/// ├── users.yaml                # 用户注册表
/// └── users/
///     └── &lt;username&gt;/
///         ├── profile.yaml      # 用户档案（偏好）
///         ├── chat_history.db   # 对话记录（SQLite）
///         └── todo.db           # 待办（SQLite，按用户隔离）
/// </summary>
public sealed class UserService
{
    /// <summary>单例</summary>
    public static UserService Instance { get; } = new();

    /// <summary>用户注册表路径（user_data/users.yaml）</summary>
    public static string RegistryPath => Path.Combine(App.UserDataDir, "users.yaml");

    /// <summary>用户数据根目录（user_data/users/）</summary>
    public static string UsersDir => Path.Combine(App.UserDataDir, "users");

    /// <summary>当前登录用户（null 表示未登录）</summary>
    public UserProfile? CurrentUser { get; private set; }

    /// <summary>当前登录用户名（便捷访问，未登录返回空串）</summary>
    public string CurrentUserName => CurrentUser?.Name ?? "";

    /// <summary>当前用户的数据目录（user_data/users/&lt;name&gt;/）</summary>
    public string CurrentUserDir => CurrentUser != null
        ? Path.Combine(UsersDir, CurrentUser.Name)
        : UsersDir;

    private UserService() { }

    // ===== 注册表操作 =====

    /// <summary>列出所有已注册用户（按最后登录时间降序）。</summary>
    public List<UserRegistryEntry> ListUsers()
    {
        var registry = LoadRegistry();
        return registry.Users
            .OrderByDescending(u => u.LastLoginAt)
            .ToList();
    }

    /// <summary>加载用户注册表。文件不存在则返回空注册表。</summary>
    private UserRegistry LoadRegistry()
    {
        if (!File.Exists(RegistryPath)) return new UserRegistry();
        try
        {
            var yaml = File.ReadAllText(RegistryPath);
            var d = new DeserializerBuilder()
                .WithNamingConvention(NullNamingConvention.Instance)
                .Build();
            return d.Deserialize<UserRegistry?>(yaml) ?? new UserRegistry();
        }
        catch
        {
            return new UserRegistry();
        }
    }

    /// <summary>保存用户注册表。</summary>
    private void SaveRegistry(UserRegistry registry)
    {
        var dir = Path.GetDirectoryName(RegistryPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var s = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        AtomicWrite(RegistryPath, s.Serialize(registry));
    }

    // ===== 用户创建/登录 =====

    /// <summary>
    /// 创建新用户。若用户名已存在则返回 false。
    /// 会初始化用户的 profile.yaml 与数据目录。
    /// </summary>
    public bool CreateUser(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();

        var registry = LoadRegistry();
        if (registry.Users.Any(u => u.Name == name))
            return false;

        // 初始化用户目录与档案
        var userDir = Path.Combine(UsersDir, name);
        Directory.CreateDirectory(userDir);

        var now = DateTime.Now.ToString("o");
        var mgmt = ConfigService.LoadGentManagement();
        var profile = new UserProfile
        {
            Name = name,
            Role = ConfigService.Load().Role,   // 继承当前默认角色
            Scale = 0.4,
            DefaultProvider = "local-klee",
            CreatedAt = now,
            LastLoginAt = now,
            Coins = mgmt.Constants.InitialCoins
        };
        SaveProfile(profile);

        // 加入注册表
        registry.Users.Add(new UserRegistryEntry
        {
            Name = name,
            CreatedAt = now,
            LastLoginAt = now
        });
        SaveRegistry(registry);

        Logger.Info("User", $"创建用户：{name}，目录={userDir}");
        return true;
    }

    /// <summary>
    /// 登录指定用户。加载其档案到 <see cref="CurrentUser"/>，更新最后登录时间。
    /// 返回加载的用户档案，若用户不存在返回 null。
    /// </summary>
    public UserProfile? Login(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();

        var registry = LoadRegistry();
        var entry = registry.Users.FirstOrDefault(u => u.Name == name);
        if (entry == null) return null;

        var profile = LoadProfile(name);
        if (profile == null)
        {
            // 注册表有但 profile 丢失，用默认值重建
            profile = new UserProfile
            {
                Name = name,
                Role = "默认角色",
                Scale = 0.4,
                DefaultProvider = "local-klee",
                CreatedAt = entry.CreatedAt
            };
        }

        var now = DateTime.Now.ToString("o");
        profile.LastLoginAt = now;
        entry.LastLoginAt = now;
        SaveProfile(profile);
        SaveRegistry(registry);

        CurrentUser = profile;
        Logger.Info("User", $"用户登录：{name}，角色={profile.Role}，AI={profile.DefaultProvider}");
        return profile;
    }

    /// <summary>登出当前用户。</summary>
    public void Logout()
    {
        if (CurrentUser != null)
        {
            Logger.Info("User", $"用户登出：{CurrentUser.Name}");
            CurrentUser = null;
        }
    }

    // ===== Profile 读写 =====

    /// <summary>获取用户档案文件路径。</summary>
    public string GetProfilePath(string name) => Path.Combine(UsersDir, name, "profile.yaml");

    /// <summary>加载指定用户的档案。文件不存在返回 null。</summary>
    public UserProfile? LoadProfile(string name)
    {
        var path = GetProfilePath(name);
        if (!File.Exists(path)) return null;
        try
        {
            var d = new DeserializerBuilder()
                .WithNamingConvention(NullNamingConvention.Instance)
                .Build();
            return d.Deserialize<UserProfile?>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Logger.Error("User", $"加载用户档案失败：{name}", ex);
            return null;
        }
    }

    /// <summary>保存用户档案。</summary>
    public void SaveProfile(UserProfile profile)
    {
        var path = GetProfilePath(profile.Name);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var s = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        AtomicWrite(path, s.Serialize(profile));
    }

    /// <summary>
    /// 原子写入文件：先写到 &lt;path&gt;.tmp，再 Move 到目标路径（覆盖）。
    /// 避免写入过程中崩溃导致文件损坏（对应 AGENTS.md §4.2 并发与崩溃安全）。
    /// </summary>
    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        // File.Move with overwrite=true 在同一卷上是原子的（Windows 上保证）
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>保存当前用户的档案（若已登录）。</summary>
    public void SaveCurrentProfile()
    {
        if (CurrentUser == null) return;
        SaveProfile(CurrentUser);
    }

    // ===== 用户偏好便捷方法（供 ConfigService 等调用）=====

    /// <summary>
    /// 持久化当前用户的角色偏好。若未登录，仅记录到内存（向后兼容）。
    /// </summary>
    public static void SaveCurrentRole(string role)
    {
        var svc = Instance;
        if (svc.CurrentUser != null)
        {
            svc.CurrentUser.Role = role;
            svc.SaveCurrentProfile();
        }
        else
        {
            // 未登录时记录到内存，登录后由 Launcher 写入
            lock (svc._pendingRoleLock)
            {
                svc._pendingRole = role;
            }
        }
    }

    /// <summary>
    /// 获取上次使用的角色（用于启动时恢复）。
    /// </summary>
    public static string? TryGetLastRole()
    {
        var svc = Instance;
        if (svc.CurrentUser != null)
            return svc.CurrentUser.Role;

        lock (svc._pendingRoleLock)
        {
            if (!string.IsNullOrEmpty(svc._pendingRole))
                return svc._pendingRole;
        }

        return null;
    }

    private readonly object _pendingRoleLock = new();
    private string? _pendingRole;

    /// <summary>登录后把待处理的角色偏好应用到当前用户。</summary>
    public void ApplyPendingRole()
    {
        lock (_pendingRoleLock)
        {
            if (!string.IsNullOrEmpty(_pendingRole) && CurrentUser != null)
            {
                CurrentUser.Role = _pendingRole;
                SaveCurrentProfile();
                _pendingRole = null;
            }
        }
    }
}
