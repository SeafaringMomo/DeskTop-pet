using System.IO;
using DesktopPetCSharp.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DesktopPetCSharp.Services;

/// <summary>
/// 配置服务：从 config/ 目录加载三份 YAML 文件，合并为运行时 <see cref="PetConfig"/>。
///
/// 配置文件分工（全部位于 config/ 目录，集中管理）：
/// - app.yaml         应用常量（非敏感）：音频开关、精灵图配置、动画参数、窗口边距
/// - characters.yaml  角色定义（非敏感）：角色映射、配色主题、各角色动画参数
/// - secrets.yaml     敏感信息：AI 提供方（含 API_KEY）、MCP 服务器地址（不入库）
///
/// 向后兼容：
/// - 若 app.yaml / characters.yaml 不存在，回退到旧 config_pet.yaml
/// - 若 secrets.yaml 不存在，回退到环境变量 OPENAI_API_KEY 等
/// - 旧 user_data/config_pet.yaml 首次启动会被读取用于迁移角色偏好，之后忽略
/// </summary>
public static class ConfigService
{
    // ===== 配置文件路径（全部在 config/ 目录）=====

    /// <summary>应用常量配置（config/app.yaml）</summary>
    public static string AppConfigPath => Path.Combine(App.BaseDir, "config", "app.yaml");

    /// <summary>角色定义配置（config/characters.yaml）</summary>
    public static string CharactersConfigPath => Path.Combine(App.BaseDir, "config", "characters.yaml");

    /// <summary>敏感信息配置（config/secrets.yaml，不入库）</summary>
    public static string SecretsConfigPath => Path.Combine(App.BaseDir, "config", "secrets.yaml");

    /// <summary>敏感信息配置示例（config/secrets.yaml.example，可入库）</summary>
    public static string SecretsExamplePath => Path.Combine(App.BaseDir, "config", "secrets.yaml.example");

    /// <summary>旧版统一配置（config/config_pet.yaml，向后兼容）</summary>
    public static string LegacyConfigPath => Path.Combine(App.BaseDir, "config", "config_pet.yaml");

    /// <summary>旧版用户配置（user_data/config_pet.yaml，仅用于迁移）</summary>
    public static string LegacyUserConfigPath => Path.Combine(App.UserDataDir, "config_pet.yaml");

    // ===== P2 新增配置文件路径（宠物管理系统）=====

    /// <summary>宠物管理常量配置（config/pet_management.yaml）</summary>
    public static string PetManagementConfigPath => Path.Combine(App.BaseDir, "config", "pet_management.yaml");

    /// <summary>皮肤配置（config/skins.yaml）</summary>
    public static string SkinsConfigPath => Path.Combine(App.BaseDir, "config", "skins.yaml");

    /// <summary>食物配置（config/foods.yaml）</summary>
    public static string FoodsConfigPath => Path.Combine(App.BaseDir, "config", "foods.yaml");

    /// <summary>动作配置（config/animations.yaml）</summary>
    public static string AnimationsConfigPath => Path.Combine(App.BaseDir, "config", "animations.yaml");

    // ===== 缓存（避免每次 Load 都读文件）=====

    private static PetConfig? _cachedPetConfig;
    private static SecretsConfig? _cachedSecrets;
    private static PetManagementConfig? _cachedPetMgmt;
    private static SkinConfig? _cachedSkins;
    private static FoodConfig? _cachedFoods;
    private static AnimationConfig? _cachedAnimations;
    private static readonly object _lock = new();

    // ===== PetConfig 加载（合并 app.yaml + characters.yaml）=====

    /// <summary>
    /// 加载并合并 app.yaml + characters.yaml 为 <see cref="PetConfig"/>。
    /// 结果缓存，直到 <see cref="InvalidateCache"/> 被调用。
    /// </summary>
    public static PetConfig Load()
    {
        lock (_lock)
        {
            if (_cachedPetConfig != null) return _cachedPetConfig;
            _cachedPetConfig = LoadInternal();
            return _cachedPetConfig;
        }
    }

    private static PetConfig LoadInternal()
    {
        var cfg = new PetConfig();

        // 1) 优先从 app.yaml + characters.yaml 加载
        bool loadedFromSplit = false;

        if (File.Exists(AppConfigPath))
        {
            try
            {
                var app = Deserialize<AppConfigYaml>(File.ReadAllText(AppConfigPath));
                app.ApplyTo(cfg);
                loadedFromSplit = true;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] app.yaml 解析失败: {ex.Message}");
            }
        }

        if (File.Exists(CharactersConfigPath))
        {
            try
            {
                var chars = Deserialize<CharactersConfigYaml>(File.ReadAllText(CharactersConfigPath));
                chars.ApplyTo(cfg);
                loadedFromSplit = true;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] characters.yaml 解析失败: {ex.Message}");
            }
        }

        // 2) 回退到旧版 config_pet.yaml（向后兼容）
        if (!loadedFromSplit && File.Exists(LegacyConfigPath))
        {
            try
            {
                var legacy = Deserialize<PetConfig>(File.ReadAllText(LegacyConfigPath));
                cfg = legacy;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] 旧版 config_pet.yaml 解析失败: {ex.Message}");
            }
        }

        // 3) 用户角色偏好优先：登录后用 CurrentUser.Role，未登录时尝试迁移旧配置
        try
        {
            var userRole = UserService.TryGetLastRole();
            if (!string.IsNullOrEmpty(userRole)) cfg.Role = userRole;
        }
        catch { /* UserService 未初始化时忽略 */ }

        return cfg;
    }

    /// <summary>使缓存失效，下次 <see cref="Load"/> 会重新读文件。</summary>
    public static void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedPetConfig = null;
        }
    }

    // ===== SecretsConfig 加载/保存 =====

    /// <summary>
    /// 加载 secrets.yaml。文件不存在时返回空对象（后续回退到环境变量）。
    /// </summary>
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
            catch (System.Exception ex)
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

    /// <summary>保存 secrets.yaml（运行时通过 UI 修改后调用）。</summary>
    public static void SaveSecrets(SecretsConfig secrets)
    {
        var dir = Path.GetDirectoryName(SecretsConfigPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        var yaml = serializer.Serialize(secrets);
        File.WriteAllText(SecretsConfigPath, yaml);

        lock (_lock)
        {
            _cachedSecrets = secrets;
        }
    }

    /// <summary>使 secrets 缓存失效。</summary>
    public static void InvalidateSecretsCache()
    {
        lock (_lock)
        {
            _cachedSecrets = null;
        }
    }

    // ===== 旧版 Save（向后兼容，不再写入 user_data，改为更新内存缓存）=====

    /// <summary>
    /// 旧版保存接口（向后兼容）。新架构下 PetConfig 为只读聚合，不再持久化。
    /// 角色切换等用户偏好由 UserService.SaveProfile 写入 user_data/users/&lt;name&gt;/profile.yaml。
    /// 此方法仅更新内存缓存以保持调用方语义。
    /// </summary>
    public static void Save(PetConfig cfg)
    {
        lock (_lock)
        {
            _cachedPetConfig = cfg;
        }
        // 用户偏好（当前角色）通过 UserService 持久化
        try { UserService.SaveCurrentRole(cfg.Role); } catch { /* UserService 未初始化时忽略 */ }
    }

    // ===== P2 新增配置加载（宠物管理系统）=====

    /// <summary>加载宠物管理常量配置（pet_management.yaml）。结果缓存。</summary>
    public static PetManagementConfig LoadPetManagement()
    {
        lock (_lock)
        {
            if (_cachedPetMgmt != null) return _cachedPetMgmt;
        }
        var cfg = LoadYamlFile<PetManagementConfig>(PetManagementConfigPath) ?? new PetManagementConfig();
        lock (_lock) { _cachedPetMgmt = cfg; }
        return cfg;
    }

    /// <summary>加载皮肤配置（skins.yaml）。结果缓存。</summary>
    public static SkinConfig LoadSkins()
    {
        lock (_lock)
        {
            if (_cachedSkins != null) return _cachedSkins;
        }
        var cfg = LoadYamlFile<SkinConfig>(SkinsConfigPath) ?? new SkinConfig();
        lock (_lock) { _cachedSkins = cfg; }
        return cfg;
    }

    /// <summary>加载食物配置（foods.yaml）。结果缓存。</summary>
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

    /// <summary>加载动作配置（animations.yaml）。结果缓存。</summary>
    public static AnimationConfig LoadAnimations()
    {
        lock (_lock)
        {
            if (_cachedAnimations != null) return _cachedAnimations;
        }
        var cfg = LoadYamlFile<AnimationConfig>(AnimationsConfigPath) ?? new AnimationConfig();
        lock (_lock) { _cachedAnimations = cfg; }
        return cfg;
    }

    /// <summary>失效所有宠物管理相关缓存。</summary>
    public static void InvalidatePetManagementCache()
    {
        lock (_lock)
        {
            _cachedPetMgmt = null;
            _cachedSkins = null;
            _cachedFoods = null;
            _cachedAnimations = null;
        }
    }

    // ===== 反序列化辅助 =====

    private static T? LoadYamlFile<T>(string path) where T : class, new()
    {
        if (!File.Exists(path)) return null;
        try { return Deserialize<T>(File.ReadAllText(path)); }
        catch (System.Exception ex)
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

    // ===== 内部 YAML 模型（用于拆分文件的解析）=====

    /// <summary>app.yaml 的解析模型</summary>
    private class AppConfigYaml
    {
        [YamlMember(Alias = "audio")] public bool Audio { get; set; } = true;
        [YamlMember(Alias = "bg_music")] public bool BgMusic { get; set; } = true;
        [YamlMember(Alias = "static_path")] public string StaticPath { get; set; } = "static";
        [YamlMember(Alias = "sprite")] public SpriteSection? Sprite { get; set; }
        [YamlMember(Alias = "animation")] public AnimationSection? Animation { get; set; }
        [YamlMember(Alias = "window")] public WindowSection? Window { get; set; }

        public void ApplyTo(PetConfig cfg)
        {
            cfg.Audio = Audio;
            cfg.BgMusic = BgMusic;
            cfg.StaticPath = StaticPath;
            if (Sprite != null)
            {
                if (!string.IsNullOrEmpty(Sprite.File)) cfg.SpriteFile = Sprite.File;
                if (Sprite.Cols > 0) cfg.SpriteCols = Sprite.Cols;
                if (Sprite.Rows > 0) cfg.SpriteRows = Sprite.Rows;
            }
            if (Animation != null)
            {
                if (Animation.BlinkIntervalMs > 0) cfg.BlinkIntervalMs = Animation.BlinkIntervalMs;
            }
        }
    }

    private class SpriteSection
    {
        [YamlMember(Alias = "file")] public string File { get; set; } = "sheet.png";
        [YamlMember(Alias = "cols")] public int Cols { get; set; } = 2;
        [YamlMember(Alias = "rows")] public int Rows { get; set; } = 1;
    }

    private class AnimationSection
    {
        [YamlMember(Alias = "blink_interval_ms")] public int BlinkIntervalMs { get; set; } = 3000;
        [YamlMember(Alias = "default_scale")] public double DefaultScale { get; set; } = 0.4;
    }

    private class WindowSection
    {
        [YamlMember(Alias = "edge_margin")] public int EdgeMargin { get; set; } = 20;
    }

    /// <summary>characters.yaml 的解析模型</summary>
    private class CharactersConfigYaml
    {
        [YamlMember(Alias = "default_role")] public string DefaultRole { get; set; } = "路飞";
        [YamlMember(Alias = "roles")] public Dictionary<string, string> Roles { get; set; } = new();
        [YamlMember(Alias = "frame_scale")] public Dictionary<string, List<double>> FrameScale { get; set; } = new();
        [YamlMember(Alias = "themes")] public Dictionary<string, RoleTheme> Themes { get; set; } = new();

        public void ApplyTo(PetConfig cfg)
        {
            cfg.Role = DefaultRole;
            if (Roles.Count > 0) cfg.Roles = Roles;
            if (FrameScale.Count > 0) cfg.FrameScale = FrameScale;
            if (Themes.Count > 0) cfg.Themes = Themes;
        }
    }
}
