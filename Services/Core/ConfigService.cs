using System.IO;
using DesktopGent.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DesktopGent.Services.Core;

/// <summary>
/// 配置服务：加载 config/ 目录下的 YAML，并通过扫描 static/ 目录动态发现角色。
///
/// 配置分工：
/// - app.yaml            应用常量（动画间隔、窗口边距等）
/// - secrets.yaml        敏感信息（AI/API_KEY、MCP 地址，不入库）
/// - gent_management.yaml 助手养成常量（奖励/冷却/等级/食物默认价）
/// - foods.yaml          食物定义
///
/// 角色定义不在 config/ 中：每个角色目录 static/&lt;英文目录&gt;/theme.yaml
/// 提供显示名与主题配色，帧动画由目录下的子文件夹（如 01_idle_breath/）提供。
/// </summary>
public static class ConfigService
{
    /// <summary>角色主题配置文件固定名（放在角色目录下）</summary>
    public const string ThemeFileName = "theme.yaml";

    /// <summary>应用常量配置（config/app.yaml）</summary>
    public static string AppConfigPath => Path.Combine(App.BaseDir, "config", "app.yaml");

    /// <summary>敏感信息配置（config/secrets.yaml，不入库）</summary>
    public static string SecretsConfigPath => Path.Combine(App.BaseDir, "config", "secrets.yaml");

    /// <summary>敏感信息配置示例（config/secrets.yaml.example，可入库）</summary>
    public static string SecretsExamplePath => Path.Combine(App.BaseDir, "config", "secrets.yaml.example");

    /// <summary>助手养成常量配置（config/gent_management.yaml）</summary>
    public static string GentManagementConfigPath => Path.Combine(App.BaseDir, "config", "gent_management.yaml");

    /// <summary>食物配置（config/foods.yaml）</summary>
    public static string FoodsConfigPath => Path.Combine(App.BaseDir, "config", "foods.yaml");

    /// <summary>静态资源根目录（相对 BaseDir）</summary>
    public static string StaticRoot => Path.Combine(App.BaseDir, "static");

    // ===== 缓存 =====

    private static GentConfig? _cachedGentConfig;
    private static SecretsConfig? _cachedSecrets;
    private static GentManagementConfig? _cachedGentMgmt;
    private static FoodConfig? _cachedFoods;
    private static readonly object _lock = new();

    /// <summary>
    /// 加载应用配置并扫描 static/ 下的角色。结果缓存，直到 <see cref="InvalidateCache"/>。
    /// </summary>
    public static GentConfig Load()
    {
        lock (_lock)
        {
            if (_cachedGentConfig != null) return _cachedGentConfig;
            _cachedGentConfig = LoadInternal();
            return _cachedGentConfig;
        }
    }

    private static GentConfig LoadInternal()
    {
        var cfg = new GentConfig();

        if (File.Exists(AppConfigPath))
        {
            try
            {
                var app = Deserialize<AppConfigYaml>(File.ReadAllText(AppConfigPath));
                app.ApplyTo(cfg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] app.yaml 解析失败: {ex.Message}");
            }
        }

        ScanRoles(cfg);

        // 用户角色偏好优先：登录后由 CurrentUser.Role 覆盖默认角色
        try
        {
            var userRole = UserService.TryGetLastRole();
            if (!string.IsNullOrEmpty(userRole)) cfg.Role = userRole;
        }
        catch
        {
            // UserService 未初始化时忽略
        }

        // 保证当前角色存在
        if (cfg.Roles.Count > 0 && !cfg.Roles.ContainsKey(cfg.Role))
            cfg.Role = cfg.Roles.Keys.First();

        return cfg;
    }

    /// <summary>
    /// 扫描 static/ 下每个含 theme.yaml 的目录，将其注册为角色。
    /// theme.yaml 的 name 为角色显示名，目录名为英文目录名。
    /// </summary>
    private static void ScanRoles(GentConfig cfg)
    {
        if (!Directory.Exists(StaticRoot))
        {
            // 兜底：无静态目录时仍提供一个默认角色
            AddFallbackRole(cfg);
            return;
        }

        var found = 0;
        foreach (var dir in Directory.GetDirectories(StaticRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var themePath = Path.Combine(dir, ThemeFileName);
            if (!File.Exists(themePath)) continue;

            RoleTheme theme;
            try
            {
                theme = Deserialize<RoleTheme>(File.ReadAllText(themePath));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] {Path.GetFileName(dir)} theme.yaml 解析失败: {ex.Message}");
                continue;
            }

            var name = string.IsNullOrWhiteSpace(theme.Name) ? Path.GetFileName(dir) : theme.Name;
            var dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            cfg.Roles[name] = dirName;
            cfg.Themes[name] = theme;
            if (!cfg.FrameScale.ContainsKey(name))
                cfg.FrameScale[name] = new List<double> { cfg.FrameIntervalMs, 0.4 };
            found++;
        }

        if (found == 0)
            AddFallbackRole(cfg);
    }

    private static void AddFallbackRole(GentConfig cfg)
    {
        const string fallbackName = "默认角色";
        cfg.Roles[fallbackName] = "default_role";
        cfg.Themes[fallbackName] = RoleTheme.DefaultRole;
        if (!cfg.FrameScale.ContainsKey(fallbackName))
            cfg.FrameScale[fallbackName] = new List<double> { cfg.FrameIntervalMs, 0.4 };
    }

    /// <summary>使应用配置缓存失效，下次 <see cref="Load"/> 重新扫描。</summary>
    public static void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedGentConfig = null;
        }
    }

    // ===== Secrets =====

    public static SecretsConfig LoadSecrets()
    {
        lock (_lock)
        {
            if (_cachedSecrets != null) return _cachedSecrets;
        }

        SecretsConfig? secrets = null;
        if (File.Exists(SecretsConfigPath))
        {
            try
            {
                secrets = Deserialize<SecretsConfig>(File.ReadAllText(SecretsConfigPath));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] secrets.yaml 解析失败: {ex.Message}");
            }
        }
        secrets ??= new SecretsConfig();

        lock (_lock)
        {
            _cachedSecrets = secrets;
        }
        return secrets;
    }

    public static void SaveSecrets(SecretsConfig secrets)
    {
        var dir = Path.GetDirectoryName(SecretsConfigPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        File.WriteAllText(SecretsConfigPath, serializer.Serialize(secrets));

        lock (_lock)
        {
            _cachedSecrets = secrets;
        }
    }

    public static void InvalidateSecretsCache()
    {
        lock (_lock)
        {
            _cachedSecrets = null;
        }
    }

    /// <summary>
    /// 更新内存中的应用配置并持久化用户角色偏好（当前角色通过 UserService 保存）。
    /// </summary>
    public static void Save(GentConfig cfg)
    {
        lock (_lock)
        {
            _cachedGentConfig = cfg;
        }
        try
        {
            UserService.SaveCurrentRole(cfg.Role);
        }
        catch
        {
            // UserService 未初始化时忽略
        }
    }

    // ===== 养成管理系统 =====

    public static GentManagementConfig LoadGentManagement()
    {
        lock (_lock)
        {
            if (_cachedGentMgmt != null) return _cachedGentMgmt;
        }
        var cfg = LoadYamlFile<GentManagementConfig>(GentManagementConfigPath) ?? new GentManagementConfig();
        lock (_lock) { _cachedGentMgmt = cfg; }
        return cfg;
    }

    public static FoodConfig LoadFoods()
    {
        lock (_lock)
        {
            if (_cachedFoods != null) return _cachedFoods;
        }
        var cfg = LoadYamlFile<FoodConfig>(FoodsConfigPath) ?? new FoodConfig();
        lock (_lock) { _cachedFoods = cfg; }
        return cfg;
    }

    public static void InvalidateGentManagementCache()
    {
        lock (_lock)
        {
            _cachedGentMgmt = null;
            _cachedFoods = null;
        }
    }

    // ===== 反序列化辅助 =====

    private static T? LoadYamlFile<T>(string path) where T : class, new()
    {
        if (!File.Exists(path)) return null;
        try
        {
            return Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Config] {Path.GetFileName(path)} 解析失败: {ex.Message}");
            return null;
        }
    }

    private static T Deserialize<T>(string yaml) where T : new()
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<T?>(yaml) ?? new T();
    }

    // ===== app.yaml 内部模型 =====

    private class AppConfigYaml
    {
        [YamlMember(Alias = "audio")] public bool Audio { get; set; } = true;
        [YamlMember(Alias = "bg_music")] public bool BgMusic { get; set; } = true;
        [YamlMember(Alias = "static_path")] public string StaticPath { get; set; } = "static";
        [YamlMember(Alias = "animation")] public AnimationSection? Animation { get; set; }
        [YamlMember(Alias = "window")] public WindowSection? Window { get; set; }

        public void ApplyTo(GentConfig cfg)
        {
            cfg.Audio = Audio;
            cfg.BgMusic = BgMusic;
            cfg.StaticPath = StaticPath;
            if (Animation != null)
            {
                if (Animation.FrameIntervalMs > 0) cfg.FrameIntervalMs = Animation.FrameIntervalMs;
                if (Animation.RandomActionIntervalMs > 0) cfg.RandomActionIntervalMs = Animation.RandomActionIntervalMs;
            }
        }
    }

    private class AnimationSection
    {
        [YamlMember(Alias = "frame_interval_ms")] public int FrameIntervalMs { get; set; } = 120;
        [YamlMember(Alias = "random_action_interval_ms")] public int RandomActionIntervalMs { get; set; } = 9000;
    }

    private class WindowSection
    {
        [YamlMember(Alias = "edge_margin")] public int EdgeMargin { get; set; } = 20;
    }
}
