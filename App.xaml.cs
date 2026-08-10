using System.IO;
using System.Windows;
using DesktopPetCSharp.Generators;
using DesktopPetCSharp.Services;
using DesktopPetCSharp.Services.AI;
using DesktopPetCSharp.Services.AI.Providers;
using DesktopPetCSharp.Services.MCP;

namespace DesktopPetCSharp;

/// <summary>
/// 应用程序入口。
/// 目录结构：
/// - BaseDir：项目根（含 .csproj），存放 static/（静态资源）和 config/（默认配置模板）
/// - UserDataDir：项目根 user_data/，存放运行时生成的用户数据（日志、待办数据库、用户配置）
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 应用程序入口。
    /// 目录结构：
    /// - BaseDir：项目根（含 .csproj），存放 static/（静态资源）和 config/（默认配置模板）
    /// - UserDataDir：项目根 user_data/，存放运行时生成的用户数据（日志、待办数据库、用户配置）
    /// </summary>
    public static string BaseDir { get; } = ResolveBaseDir();

    /// <summary>
    /// 用户数据目录（项目根 user_data/）：
    /// - user_data/logs/      运行日志（按天滚动）
    /// - user_data/data/      待办数据库 todo.db
    /// - user_data/config_pet.yaml  用户配置（退出时保存）
    /// </summary>
    public static string UserDataDir { get; } = ResolveUserDataDir();

    /// <summary>共享的待办数据仓储（UI 与 MCP 服务器共用同一实例）</summary>
    public static TodoRepository Todos { get; private set; } = null!;

    /// <summary>对话记录仓储（按当前用户隔离，登录后初始化）</summary>
    public static ChatHistoryRepository? ChatHistory { get; private set; }

    /// <summary>全局静音开关。为 true 时：背景音乐不播放、音效不播放、TTS 不朗读。</summary>
    public static bool IsMuted { get; set; }

    /// <summary>
    /// 单实例 Mutex 引用。保持存活以防止被 GC 提前释放（对应 AGENTS.md §4.2）。
    /// </summary>
    private static Mutex? _singleInstanceMutex;

    /// <summary>
    /// App 构造函数：注册全局异常兜底处理器。
    /// 必须在 OnStartup 之前注册，保证启动期间的异常也能被捕获。
    /// </summary>
    public App()
    {
        // 1) WPF UI 线程未处理异常
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 2) 非 UI 线程（包括原生线程）未处理异常
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // 3) 未观察的 Task 异常（async void / 未 await 的任务抛错）
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// UI 线程异常兜底：记录日志并提示用户。
    /// 设置 e.Handled = true 防止应用崩溃。
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Logger.Error("App", "UI 线程未处理异常", e.Exception);
            MessageBox.Show($"发生未处理异常：{e.Exception.Message}\n\n详细信息已记录到日志。",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* 异常处理失败也不让应用崩溃 */ }
        e.Handled = true;
    }

    /// <summary>非 UI 线程异常兜底：仅记录日志，无法阻止应用终止。</summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null)
                Logger.Error("App", $"非 UI 线程未处理异常（IsTerminating={e.IsTerminating}）", ex);
            else
                Logger.Error("App", $"非 UI 线程未处理异常（IsTerminating={e.IsTerminating}）：{e.ExceptionObject}");
        }
        catch { }
    }

    /// <summary>未观察 Task 异常兜底：标记为已观察防止应用崩溃。</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Logger.Error("App", "未观察的 Task 异常", e.Exception);
        }
        catch { }
        e.SetObserved();
    }

    private static string ResolveBaseDir()
    {
        var env = Environment.GetEnvironmentVariable("DESKTOP_PET_BASE");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(Path.Combine(env, "static")))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++)
        {
            if (dir.GetFiles("*.csproj").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    private static string ResolveUserDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++)
        {
            if (dir.GetFiles("*.csproj").Length > 0)
                return Path.Combine(dir.FullName, "user_data");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "user_data");
    }

    /// <summary>
    /// 获取角色资源目录的完整路径：&lt;BaseDir&gt;/&lt;static_path&gt;/&lt;英文目录&gt;
    /// 如 D:\...\DesktopPetCSharp\static\luffy
    /// </summary>
    public static string GetRoleDir(string roleName)
    {
        var cfg = ConfigService.Load();
        return Path.Combine(BaseDir, cfg.GetRoleSubdir(roleName));
    }

    /// <summary>获取角色资源目录下某文件的完整路径。</summary>
    public static string GetRoleFile(string roleName, string fileName)
        => Path.Combine(GetRoleDir(roleName), fileName);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 0) 单实例检测：若已有实例运行则提示后退出（对应 AGENTS.md §4.2 并发限制）
        if (!SingleInstance.TryAcquire(out _singleInstanceMutex))
        {
            MessageBox.Show("桌面宠物已经在运行中，请勿重复启动。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 1) 初始化日志系统（最先做，后续步骤都可被记录）
        Logger.Init(UserDataDir);
        Logger.Info("App", $"应用启动，BaseDir={BaseDir}，UserDataDir={UserDataDir}");

        var cfg = ConfigService.Load();

        // 2) 为所有配置的角色生成像素精灵图（若已存在则跳过，便于用户手动替换）
        //    P4: 皮肤系统后，默认皮肤素材生成到 static/<角色>/classic/ 子目录
        foreach (var kv in cfg.Roles)
        {
            var roleRootDir = GetRoleDir(kv.Key);
            var classicDir = Path.Combine(roleRootDir, "classic");
            try
            {
                SpriteGeneratorRegistry.EnsureGenerated(kv.Key, kv.Value, classicDir, cfg.SpriteFile);
                Logger.Info("App", $"角色 {kv.Key}({kv.Value}) 经典皮肤精灵图就绪：{classicDir}");
            }
            catch (System.Exception ex)
            {
                Logger.Error("App", $"生成角色 {kv.Key} 精灵图失败", ex);
            }
        }

        // 3) 注册 AI 提供方（从 secrets.yaml 加载，回退到环境变量）
        RegisterAiProviders();

        // 4) 显示启动页：用户在此选择/新建用户、配置模型
        var launcher = new Windows.LauncherWindow();
        MainWindow = launcher;
        launcher.ShowDialog();

        // 5) 若用户未选择进入主界面（直接关闭启动页），则退出应用
        if (!launcher.Entered)
        {
            Logger.Info("App", "用户在启动页取消，应用退出");
            Shutdown();
            return;
        }

        // 6) 用户登录后：失效配置缓存（让后续 Load() 用 CurrentUser.Role 覆盖默认角色）
        ConfigService.InvalidateCache();

        // 7) 初始化待办数据库（按用户隔离）、注册 MCP、打开宠物窗口
        var userDir = Services.UserService.Instance.CurrentUserDir;
        var todoDbPath = System.IO.Path.Combine(userDir, "todo.db");
        Todos = new TodoRepository(todoDbPath);
        Logger.Info("App", $"待办数据库初始化：{todoDbPath}");

        // 初始化对话记录数据库（按用户隔离）
        var chatDbPath = System.IO.Path.Combine(userDir, "chat_history.db");
        ChatHistory = new ChatHistoryRepository(chatDbPath);
        Logger.Info("App", $"对话记录数据库初始化：{chatDbPath}");

        // 注册 MCP 服务器（从 secrets.yaml 读取配置，详见 P9）
        await RegisterMcpServers();

        // 8) 启动宠物主窗口
        var pet = new Windows.PetWindow();
        MainWindow = pet;
        pet.Show();

        // 9) 应用待处理的角色偏好（启动前未登录阶段可能记录的角色切换）
        Services.UserService.Instance.ApplyPendingRole();

        // 10) 启动好感度衰减后台计时器（P6）
        Services.AffinityService.Instance.Start();

        // 11) 迁移清理：登录后用户的角色偏好已写入 profile.yaml，删除旧版 user_data/config_pet.yaml
        CleanupLegacyUserConfig();
    }

    /// <summary>
    /// 删除旧版 user_data/config_pet.yaml（已被 user_data/users/&lt;name&gt;/profile.yaml 取代）。
    /// 仅在用户已登录（迁移成功）后执行，避免误删未迁移的数据。
    /// </summary>
    private static void CleanupLegacyUserConfig()
    {
        try
        {
            var legacy = ConfigService.LegacyUserConfigPath;
            if (File.Exists(legacy) && Services.UserService.Instance.CurrentUser != null)
            {
                File.Delete(legacy);
                Logger.Info("App", $"已清理旧版用户配置：{legacy}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("App", $"清理旧版用户配置失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 从 secrets.yaml 加载 AI 提供方配置并注册。
    /// 回退顺序：secrets.yaml → 环境变量 OPENAI_API_KEY。
    /// </summary>
    private static void RegisterAiProviders()
    {
        var registry = AiProviderRegistry.Instance;
        var secrets = ConfigService.LoadSecrets();
        bool anyRegistered = false;

        foreach (var p in secrets.AiProviders)
        {
            if (!p.Enabled) continue;
            if (string.IsNullOrWhiteSpace(p.Name)) continue;

            if (p.Type == "local")
            {
                registry.Register(new LocalKleeProvider());
                anyRegistered = true;
                Logger.Info("App", $"注册 AI 提供方（local）：{p.Name}");
            }
            else if (p.Type == "openai-compatible")
            {
                if (string.IsNullOrWhiteSpace(p.ApiKey))
                {
                    Logger.Warn("App", $"跳过 AI 提供方 {p.Name}：api_key 为空");
                    continue;
                }
                registry.Register(new OpenAiCompatibleProvider
                {
                    Name = p.Name,
                    BaseUrl = p.BaseUrl,
                    ApiKey = p.ApiKey,
                    Model = p.Model
                });
                anyRegistered = true;
                Logger.Info("App", $"注册 AI 提供方（openai-compatible）：{p.Name}，model={p.Model}");
            }
        }

        // 回退：若 secrets.yaml 没配置任何 provider，尝试环境变量
        if (!anyRegistered)
        {
            TryRegisterOpenAiCompatible(registry);
        }

        // 兜底：始终保证有一个 LocalKleeProvider
        if (!registry.Names.Contains("local-klee"))
        {
            registry.Register(new LocalKleeProvider());
        }

        Logger.Info("App", $"已注册 AI 提供方: {string.Join(", ", registry.Names)}");
    }

    private static void TryRegisterOpenAiCompatible(AiProviderRegistry registry)
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return;

        registry.Register(new OpenAiCompatibleProvider
        {
            Name = Environment.GetEnvironmentVariable("OPENAI_PROVIDER_NAME") ?? "openai",
            BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1",
            ApiKey = key,
            Model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini"
        });
        Logger.Info("App", "已注册 OpenAI 兼容提供方");
    }

    /// <summary>
    /// 从 secrets.yaml 加载 MCP 服务器配置并注册。
    /// type=local：本进程内服务器（当前仅 local-todo）；type=stdio/sse：预留接口，记录警告但不实现。
    /// </summary>
    private static async Task RegisterMcpServers()
    {
        var secrets = ConfigService.LoadSecrets();
        bool anyRegistered = false;

        foreach (var s in secrets.McpServers)
        {
            if (!s.Enabled) continue;
            if (string.IsNullOrWhiteSpace(s.Name)) continue;

            if (s.Type == "local")
            {
                // 目前仅 local-todo 一个本地服务器实现
                if (s.Name == "local-todo")
                {
                    await McpManager.Instance.RegisterAsync(new LocalTodoMcpServer(Todos));
                    anyRegistered = true;
                    Logger.Info("App", $"注册 MCP 服务器（local）：{s.Name}");
                }
                else
                {
                    Logger.Warn("App", $"未实现的 local MCP 服务器：{s.Name}（仅支持 local-todo）");
                }
            }
            else if (s.Type == "stdio")
            {
                if (string.IsNullOrWhiteSpace(s.Command))
                {
                    Logger.Warn("App", $"跳过 MCP 服务器 {s.Name}：command 为空");
                    continue;
                }
                try
                {
                    var stdio = new StdioMcpServer(s.Name, s.Command, s.Args);
                    await stdio.InitializeAsync();
                    await McpManager.Instance.RegisterAsync(stdio);
                    anyRegistered = true;
                    Logger.Info("App", $"注册 MCP 服务器（stdio）：{s.Name}，command={s.Command}");
                }
                catch (Exception ex)
                {
                    Logger.Error("App", $"注册 stdio MCP 服务器 {s.Name} 失败", ex);
                }
            }
            else if (s.Type == "sse")
            {
                if (string.IsNullOrWhiteSpace(s.Url))
                {
                    Logger.Warn("App", $"跳过 MCP 服务器 {s.Name}：url 为空");
                    continue;
                }
                try
                {
                    var sse = new SseMcpServer(s.Name, s.Url!);
                    await sse.InitializeAsync();
                    await McpManager.Instance.RegisterAsync(sse);
                    anyRegistered = true;
                    Logger.Info("App", $"注册 MCP 服务器（sse）：{s.Name}，url={s.Url}");
                }
                catch (Exception ex)
                {
                    Logger.Error("App", $"注册 sse MCP 服务器 {s.Name} 失败", ex);
                }
            }
            else
            {
                Logger.Warn("App", $"未知 MCP 服务器类型：{s.Type}（{s.Name}）");
            }
        }

        // 兜底：若 secrets.yaml 没配置任何 MCP 服务器，注册默认的 local-todo
        if (!anyRegistered)
        {
            await McpManager.Instance.RegisterAsync(new LocalTodoMcpServer(Todos));
            Logger.Info("App", "兜底注册 MCP 服务器：local-todo");
        }

        Logger.Info("App", "MCP 服务器注册完成");
    }
}

/// <summary>
/// 精灵图生成器注册表：按角色名分发到对应的生成器。
/// 同时负责生成角色的默认 BGM（若角色目录下无 bgm 文件）。
/// 新增角色时在此注册即可。
/// </summary>
public static class SpriteGeneratorRegistry
{
    public static void EnsureGenerated(string roleName, string dirName, string roleDir, string spriteFile)
    {
        if (!Directory.Exists(roleDir)) Directory.CreateDirectory(roleDir);

        // 1) 精灵图
        var sheetPath = Path.Combine(roleDir, spriteFile);
        switch (dirName.ToLowerInvariant())
        {
            case "luffy":
                LuffySpriteGenerator.EnsureGenerated(sheetPath);
                break;
            case "zoro":
                ZoroSpriteGenerator.EnsureGenerated(sheetPath);
                break;
            default:
                // 未知角色不生成精灵图，等待用户放入文件
                break;
        }

        // 2) 验证精灵图规格（若用户用 Aseprite 替换了文件，确保网格匹配）
        var cfg = ConfigService.Load();
        if (File.Exists(sheetPath))
        {
            SpriteSheetValidator.ValidateAndLog(sheetPath, cfg.SpriteCols, cfg.SpriteRows);
        }

        // 3) 导出调色板到 palette.gpl / palette.hex（供 Aseprite 等工具导入）
        var palette = GetPalette(dirName);
        if (palette != null)
        {
            PaletteExporter.ExportForRole(roleDir, roleName, palette);
        }

        // 4) 默认 BGM：若无任何 bgm 文件，按角色生成
        EnsureDefaultBgm(dirName, roleDir);
    }

    /// <summary>获取角色调色板（用于导出 .gpl 供 Aseprite 导入）。</summary>
    private static Dictionary<char, System.Windows.Media.Color>? GetPalette(string dirName)
        => dirName.ToLowerInvariant() switch
        {
            "luffy" => LuffySpriteGenerator.Palette,
            "zoro" => ZoroSpriteGenerator.Palette,
            _ => null
        };

    /// <summary>若无 bgm.mp3/bgm.wav 则生成默认占位 BGM。</summary>
    private static void EnsureDefaultBgm(string dirName, string roleDir)
    {
        var mp3 = Path.Combine(roleDir, "bgm.mp3");
        var wav = Path.Combine(roleDir, "bgm.wav");
        if (File.Exists(mp3) || File.Exists(wav)) return;

        switch (dirName.ToLowerInvariant())
        {
            case "zoro":
                BgmGenerator.EnsureGenerated(wav); // 索隆用程序生成的旋律
                break;
            // luffy 用用户提供的 mengde.mp3（已移到 static/luffy/bgm.mp3），不生成
            // 其它角色：不生成，静默跳过
        }
    }
}
