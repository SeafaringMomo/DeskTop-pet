using System.IO;
using System.Windows;
using DesktopGent.Services.AI;
using DesktopGent.Services.AI.Providers;
using DesktopGent.Services.Core;
using DesktopGent.Services.Data;
using DesktopGent.Services.MCP;

namespace DesktopGent;

/// <summary>
/// 应用程序入口。
/// 目录结构：
/// - BaseDir：项目根（含 .csproj），存放 static/（静态资源）与 config/（默认配置模板）
/// - UserDataDir：项目根 user_data/，存放运行时生成的用户数据（日志、待办数据库、用户档案）
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 应用程序根目录。
    /// - BaseDir：项目根，存放 static/（静态资源）与 config/（默认配置模板）
    /// - UserDataDir：项目根 user_data/，存放运行时生成的用户数据
    /// </summary>
    public static string BaseDir { get; } = ResolveBaseDir();

    /// <summary>
    /// 用户数据目录（项目根 user_data/）：
    /// - user_data/logs/      运行日志（按天滚动）
    /// - user_data/users/     用户档案与数据库
    /// </summary>
    public static string UserDataDir { get; } = ResolveUserDataDir();

    /// <summary>共享的待办数据仓库（UI 与 MCP 服务器共用同一实例）</summary>
    public static TodoRepository Todos { get; private set; } = null!;

    /// <summary>对话记录仓库（按当前用户隔离，登录后初始化）</summary>
    public static ChatHistoryRepository? ChatHistory { get; private set; }

    /// <summary>全局静音开关。为 true 时不播放提示音，TTS 不朗读。</summary>
    public static bool IsMuted { get; set; }

    /// <summary>
    /// 单实例 Mutex 引用。保持存活以防被 GC 提前释放（对应 AGENTS.md 并发限制）。
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

        // 3) 未观察的 Task 异常
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
        catch
        {
            // 异常处理失败也不让应用崩溃
        }
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
        var env = Environment.GetEnvironmentVariable("DESKTOP_GENT_BASE");
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

    /// <summary>获取角色资源目录的完整路径：&lt;BaseDir&gt;/&lt;static_path&gt;/&lt;英文目录&gt;</summary>
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

        // 0) 单实例检测：若已有实例运行则提示后退出（对应 AGENTS.md 并发限制）
        if (!SingleInstance.TryAcquire(out _singleInstanceMutex))
        {
            MessageBox.Show("桌面助手已经在运行中，请勿重复启动。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 1) 初始化日志系统（最先做，后续步骤都可被记录）
        Logger.Init(UserDataDir);
        Logger.Info("App", $"应用启动，BaseDir={BaseDir}，UserDataDir={UserDataDir}");

        // 2) 注册 AI 提供商（从 secrets.yaml 加载，回退到环境变量）
        RegisterAiProviders();

        // 3) 显示启动页：用户在此选择/新建用户、配置模型
        var launcher = new Windows.LauncherWindow();
        MainWindow = launcher;
        launcher.ShowDialog();

        // 4) 若用户未选择进入主界面（直接关闭启动页），则退出应用
        if (!launcher.Entered)
        {
            Logger.Info("App", "用户在启动页取消，应用退出");
            Shutdown();
            return;
        }

        // 5) 用户登录后：失效配置缓存（让后续 Load() 用 CurrentUser.Role 覆盖默认角色）
        ConfigService.InvalidateCache();

        // 5.1) 恢复该用户上次选择的默认 AI 提供方
        ApplyUserProviderPreference();

        // 6) 初始化待办数据库（按用户隔离）、注册 MCP、打开助手窗口
        var userDir = UserService.Instance.CurrentUserDir;
        var todoDbPath = Path.Combine(userDir, "todo.db");
        Todos = new TodoRepository(todoDbPath);
        Logger.Info("App", $"待办数据库初始化：{todoDbPath}");

        // 初始化对话记录数据库（按用户隔离）
        var chatDbPath = Path.Combine(userDir, "chat_history.db");
        ChatHistory = new ChatHistoryRepository(chatDbPath);
        Logger.Info("App", $"对话记录数据库初始化：{chatDbPath}");

        // 注册 MCP 服务器（从 secrets.yaml 读取配置）
        await RegisterMcpServers();

        // 7) 启动助手主窗口
        var gent = new Windows.GentWindow();
        MainWindow = gent;
        gent.Show();

        // 8) 应用待处理的角色偏好（启动前未登录阶段可能记录的切换）
        UserService.Instance.ApplyPendingRole();

        // 9) 启动好感度衰减后台计时器
        AffinityService.Instance.Start();

        // 10) 启动 Python Agent sidecar（异步，不阻塞 UI；失败则回退到其他提供方）
        await StartAgentSidecarAsync();
    }

    /// <summary>
    /// 启动工具桥与 Python Agent sidecar。
    ///
    /// 顺序：工具桥（C# 侧）→ sidecar（Python 侧，需要桥地址才能加载待办工具）。
    /// 失败仅记录日志并保留原有 Provider，不影响应用可用性。
    /// </summary>
    private static async Task StartAgentSidecarAsync()
    {
        var secrets = ConfigService.LoadSecrets();
        var agent = secrets.Agent;
        if (!agent.Enabled || !agent.AutoStart)
        {
            Logger.Info("Agent", $"Python Agent 未启用（enabled={agent.Enabled}，auto_start={agent.AutoStart}）");
            return;
        }

        // 选择 sidecar 使用的在线模型：第一个启用的 openai-compatible
        var llm = secrets.AiProviders.FirstOrDefault(p =>
            p.Enabled &&
            p.Type == "openai-compatible" &&
            !string.IsNullOrWhiteSpace(p.ApiKey));
        if (llm == null)
        {
            Logger.Warn("Agent", "没有已启用的 openai-compatible 提供方，跳过 sidecar（请先在模型设置中配置 API Key）");
            return;
        }

        // 1) 起工具桥，再把地址与令牌交给 sidecar
        if (AgentToolBridge.Instance.Start())
        {
            AgentSidecarService.Instance.ToolBridgeUrl = AgentToolBridge.Instance.BaseUrl;
            AgentSidecarService.Instance.ToolBridgeToken = AgentToolBridge.Instance.Token;
        }

        // 2) 起 sidecar
        //    意外退出时不自动重启（避免崩溃循环），但必须把默认提供方摘下来，
        //    否则 PythonAgentProvider 会持续对着已死端口发请求
        var sidecar = AgentSidecarService.Instance;
        sidecar.UnexpectedExit -= OnSidecarUnexpectedExit;
        sidecar.UnexpectedExit += OnSidecarUnexpectedExit;

        var ready = await AgentSidecarService.Instance.StartAsync(
            UserService.Instance.CurrentUserName,
            ConfigService.Load().Role,
            llm,
            agent);

        if (!ready) return;

        // 3) sidecar 就绪后才切换默认提供方：
        //    否则 python-agent 会在启动前抢占默认，导致首条消息必然失败
        //    例外：用户明确选过别的在线提供方（既非 local-klee 占位也非未选择）时尊重其选择
        var preferred = UserService.Instance.CurrentUser?.DefaultProvider ?? "";
        var userPickedOther = preferred is not ("" or "local-klee" or "python-agent");
        if (userPickedOther)
        {
            Logger.Info("Agent", $"sidecar 就绪，但用户已选定默认提供方 {preferred}，不自动切换");
            return;
        }

        if (AiProviderRegistry.Instance.TrySetDefault("python-agent"))
            Logger.Info("Agent", "默认 AI 提供方已切换为 python-agent");
        else
            Logger.Warn("Agent", "python-agent 未注册，保持当前默认提供方");
    }

    /// <summary>
    /// 恢复用户档案里保存的默认 AI 提供方。
    ///
    /// 约束：登录时尚不确定 sidecar 能否就绪，所以跳过 python-agent
    /// （就绪后由 <see cref="StartAgentSidecarAsync"/> 再切换，AGENTS.md §7.4）。
    /// 提供方不存在时保持当前默认并记日志。
    /// </summary>
    private static void ApplyUserProviderPreference()
    {
        var profile = UserService.Instance.CurrentUser;
        if (profile == null) return;

        var name = profile.DefaultProvider;
        if (string.IsNullOrWhiteSpace(name) || name == "python-agent") return;

        var registry = AiProviderRegistry.Instance;
        if (!registry.Names.Contains(name))
        {
            Logger.Info("AI", $"用户偏好提供方 {name} 未注册，保持默认 {registry.DefaultName}");
            return;
        }
        if (registry.TrySetDefault(name))
            Logger.Info("AI", $"按用户档案恢复默认 AI 提供方：{name}");
    }

    /// <summary>
    /// sidecar 意外退出：把默认提供方回退到非 python-agent 的可用项。
    ///
    /// 与启动阶段的回退逻辑一致（AGENTS.md §7.1 兜底不可移除）：
    /// 优先其他在线提供方，其次 local-klee。
    /// </summary>
    private static void OnSidecarUnexpectedExit(string reason)
    {
        var registry = AiProviderRegistry.Instance;
        if (registry.DefaultName != "python-agent")
        {
            Logger.Warn("Agent", $"{reason}；当前默认提供方不是 python-agent，无需回退");
            return;
        }

        var fallback = registry.Names.FirstOrDefault(n => n != "python-agent")
            ?? (registry.Names.Contains("local-klee") ? "local-klee" : null);
        if (fallback != null && registry.TrySetDefault(fallback))
            Logger.Warn("Agent", $"{reason}；默认提供方已回退为 {fallback}");
        else
            Logger.Error("Agent", $"{reason}；没有可回退的 AI 提供方");
    }

    /// <summary>
    /// 应用退出：释放 sidecar 与工具桥（AGENTS.md §6.6）。
    /// 必须在 Shutdown 前完成，否则子进程会成为孤儿。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try { AgentSidecarService.Instance.Stop(); }
        catch (Exception ex) { Logger.Error("Agent", "停止 sidecar 失败", ex); }

        try { AgentToolBridge.Instance.Dispose(); }
        catch (Exception ex) { Logger.Error("Agent", "停止工具桥失败", ex); }

        Logger.Info("App", "应用退出清理完成");
        base.OnExit(e);
    }

    /// <summary>
    /// 从 secrets.yaml 加载 AI 提供商配置并注册。
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
                Logger.Info("App", $"注册 AI 提供商（local）：{p.Name}");
            }
            else if (p.Type == "openai-compatible")
            {
                if (string.IsNullOrWhiteSpace(p.ApiKey))
                {
                    Logger.Warn("App", $"跳过 AI 提供商 {p.Name}：api_key 为空");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(p.BaseUrl))
                {
                    // 空 base_url 会让请求打到 "/chat/completions"，报错难排查
                    Logger.Warn("App", $"跳过 AI 提供商 {p.Name}：base_url 为空");
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
                Logger.Info("App", $"注册 AI 提供商（openai-compatible）：{p.Name}，model={p.Model}");
            }
            else if (p.Type == "python-agent")
            {
                if (!secrets.Agent.Enabled)
                {
                    Logger.Warn("App", $"跳过 AI 提供商 {p.Name}：agent.enabled=false");
                    continue;
                }
                registry.Register(new PythonAgentProvider
                {
                    Name = p.Name,
                    TimeoutSeconds = Math.Max(10, secrets.Agent.RequestTimeoutSeconds)
                });
                anyRegistered = true;
                Logger.Info("App", $"注册 AI 提供商（python-agent）：{p.Name}");
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

        Logger.Info("App", $"已注册 AI 提供商：{string.Join(", ", registry.Names)}");

        // 启动阶段不要选中 python-agent：此时 sidecar 尚未拉起，选中会导致首条消息失败。
        // 真正的切换在 StartAgentSidecarAsync 健康检查通过后进行。
        if (registry.DefaultName == "python-agent")
        {
            var fallback = registry.Names.FirstOrDefault(n => n != "python-agent");
            if (fallback != null)
            {
                registry.TrySetDefault(fallback);
                Logger.Info("App", $"启动阶段默认提供方暂设为 {fallback}（等待 sidecar 就绪）");
            }
        }
    }

    private static void TryRegisterOpenAiCompatible(AiProviderRegistry registry)
    {        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return;

        registry.Register(new OpenAiCompatibleProvider
        {
            Name = Environment.GetEnvironmentVariable("OPENAI_PROVIDER_NAME") ?? "openai",
            BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1",
            ApiKey = key,
            Model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini"
        });
        Logger.Info("App", "已注册 OpenAI 兼容提供商");
    }

    /// <summary>
    /// 从 secrets.yaml 加载 MCP 服务器配置并注册。
    /// type=local：进程内服务器（当前仅 local-todo）；type=stdio/sse：预留接口，记录警告但不实现。
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
                // 目前仅 local-todo 一个本地服务实现
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
