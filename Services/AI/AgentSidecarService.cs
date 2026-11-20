using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using DesktopGent.Models;
using DesktopGent.Services.Core;

namespace DesktopGent.Services.AI;

/// <summary>
/// Python Agent sidecar 进程守护。
///
/// 职责：
/// - 探测 Python 解释器（项目根 .venv 优先）
/// - 分配空闲端口、生成随机令牌
/// - 启动子进程并等待 /health 就绪
/// - 把 stdout/stderr 转发到 Logger("Agent")
/// - 退出时确保杀死进程树（AGENTS.md §6.6）
///
/// 单例。未配置或启动失败时 <see cref="IsReady"/> 为 false，
/// 上层据此回退到其他 AI 提供方（保留兜底逻辑，AGENTS.md §7.1）。
/// </summary>
public sealed class AgentSidecarService : IDisposable
{
    public static AgentSidecarService Instance { get; } = new();

    /// <summary>健康检查最长等待时间。</summary>
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(30);

    private Process? _process;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>是否处于主动停止流程（用于区分「意外退出」与「正常收尾」）。</summary>
    private bool _stopping;

    private AgentSidecarService() { }

    /// <summary>
    /// sidecar 意外退出（非本进程主动 Stop）时触发，参数为原因描述。
    /// 上层据此把默认提供方从 python-agent 摘下并回退，避免后续每条消息都失败。
    /// </summary>
    public event Action<string>? UnexpectedExit;

    /// <summary>sidecar 是否已就绪（健康检查通过）。</summary>
    public bool IsReady { get; private set; }

    /// <summary>sidecar 基础地址，如 http://127.0.0.1:12345。</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>鉴权令牌。</summary>
    public string Token { get; private set; } = "";

    /// <summary>工具桥地址（由 App 在启动 sidecar 前设置）。</summary>
    public string ToolBridgeUrl { get; set; } = "";

    /// <summary>工具桥令牌。</summary>
    public string ToolBridgeToken { get; set; } = "";

    /// <summary>sidecar 启动时的角色名（用于重建提示与日志）。</summary>
    public string Role { get; private set; } = "";

    /// <summary>
    /// 启动 sidecar。重复调用幂等；已在运行则直接返回。
    /// </summary>
    /// <param name="userName">当前用户名（决定工作空间目录）</param>
    /// <param name="role">当前角色显示名</param>
    /// <param name="llm">LLM 配置（来自 secrets.yaml 的第一个启用的 openai-compatible）</param>
    /// <param name="agent">Agent 配置块</param>
    public async Task<bool> StartAsync(
        string userName,
        string role,
        AiProviderConfig? llm,
        AgentConfig agent)
    {
        lock (_lock)
        {
            if (_process != null && !_process.HasExited && IsReady) return true;
            if (!agent.Enabled) { Logger.Info("Agent", "agent.enabled=false，跳过 sidecar 启动"); return false; }
        }

        if (llm == null || string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            Logger.Warn("Agent", "没有可用的在线模型配置（需至少一个已启用的 openai-compatible 提供方），sidecar 不启动");
            return false;
        }

        var python = ResolvePython(agent.PythonPath);
        if (python == null)
        {
            Logger.Warn("Agent", "未找到 Python 解释器（<BaseDir>/.venv 需在当前系统执行 uv sync），sidecar 不启动");
            return false;
        }

        var workDir = ResolveAgentDir();
        if (workDir == null)
        {
            Logger.Error("Agent", "未找到 agent/ 目录（含 pyproject.toml），sidecar 不启动");
            return false;
        }

        var workspace = ResolveWorkspace(agent.WorkspaceRoot, userName);
        var port = GetFreePort();
        var token = RandomNumberGenerator.GetHexString(32);

        var psi = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("agent_service.main");
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString());
        psi.ArgumentList.Add("--token");
        psi.ArgumentList.Add(token);
        psi.ArgumentList.Add("--user");
        psi.ArgumentList.Add(userName);
        psi.ArgumentList.Add("--role");
        psi.ArgumentList.Add(role);
        psi.ArgumentList.Add("--workspace");
        psi.ArgumentList.Add(workspace);
        if (!string.IsNullOrWhiteSpace(ToolBridgeUrl))
        {
            psi.ArgumentList.Add("--tool-bridge-url");
            psi.ArgumentList.Add(ToolBridgeUrl);
            psi.ArgumentList.Add("--tool-bridge-token");
            psi.ArgumentList.Add(ToolBridgeToken);
        }
        if (agent.EnableExec) psi.ArgumentList.Add("--enable-exec");

        // 环境变量注入 LLM 配置与可选密钥（禁止写日志）
        psi.Environment["AGENT_LLM_MODEL"] = llm.Model;
        psi.Environment["AGENT_LLM_BASE_URL"] = llm.BaseUrl;
        psi.Environment["AGENT_LLM_API_KEY"] = llm.ApiKey;
        psi.Environment["AGENT_MAX_TOOL_ITERATIONS"] = agent.MaxToolIterations.ToString();
        psi.Environment["AGENT_LOG_LEVEL"] = agent.LogLevel;
        if (!string.IsNullOrWhiteSpace(agent.BraveApiKey))
            psi.Environment["BRAVE_API_KEY"] = agent.BraveApiKey;
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUNBUFFERED"] = "1";

        try
        {
            var proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start 返回 null");
            lock (_lock) { _process = proc; BaseUrl = $"http://127.0.0.1:{port}"; Token = token; Role = role; _stopping = false; }
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) => OnProcessExited(proc);

            _ = Task.Run(() => PumpLogsAsync(proc, "stdout"));
            _ = Task.Run(() => PumpLogsAsync(proc, "stderr"));

            var ready = await WaitForHealthAsync(proc, agent.RequestTimeoutSeconds).ConfigureAwait(false);
            lock (_lock) { IsReady = ready; }
            if (ready)
            {
                Logger.Info("Agent", $"Python Agent sidecar 就绪：{BaseUrl}，user={userName}，role={role}，model={llm.Model}");
            }
            else
            {
                Logger.Error("Agent", "sidecar 健康检查超时，将回退到其他 AI 提供方");
                Stop();
            }
            return ready;
        }
        catch (Exception ex)
        {
            Logger.Error("Agent", "启动 sidecar 失败", ex);
            lock (_lock) { IsReady = false; }
            return false;
        }
    }

    /// <summary>停止 sidecar（幂等）。退出应用时必须调用。</summary>
    public void Stop()
    {
        Process? proc;
        lock (_lock)
        {
            proc = _process;
            _process = null;
            IsReady = false;
            _stopping = true;
        }
        if (proc == null) return;

        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Agent", $"结束 sidecar 进程时出错：{ex.Message}");
        }
        finally
        {
            try { proc.Dispose(); } catch { }
            Logger.Info("Agent", "Python Agent sidecar 已停止");
        }
    }

    /// <summary>
    /// 子进程退出回调：区分主动停止与意外崩溃。
    ///
    /// 不自动重启（避免崩溃循环），但必须把 <see cref="IsReady"/> 置 false 并通知上层回退，
    /// 否则 <see cref="PythonAgentProvider"/> 会继续对着已死端口发请求，每条消息都失败。
    /// 回调运行在进程退出线程，这里只做状态变更与事件触发，不等待锁外的阻塞操作。
    /// </summary>
    private void OnProcessExited(Process proc)
    {
        bool unexpected;
        int exitCode = -1;
        lock (_lock)
        {
            // 主动 Stop 时 _process 已被清空，或 _stopping 为 true
            unexpected = !_stopping && ReferenceEquals(_process, proc) && IsReady;
            if (ReferenceEquals(_process, proc)) _process = null;
            IsReady = false;
            try { exitCode = proc.HasExited ? proc.ExitCode : -1; } catch { /* 进程对象可能已释放 */ }
        }
        if (!unexpected) return;

        var reason = $"sidecar 进程意外退出（退出码={exitCode}）";
        Logger.Error("Agent", $"{reason}，默认提供方将回退");
        try { UnexpectedExit?.Invoke(reason); }
        catch (Exception ex) { Logger.Error("Agent", "处理 sidecar 退出回调失败", ex); }
    }

    /// <summary>
    /// 主动标记 sidecar 不可用（检测到崩溃 / 连接失败时调用）。
    ///
    /// 不杀进程（可能只是暂时不可达），只把状态置为未就绪并通知上层回退。
    /// 幂等：已经是未就绪状态时不再重复通知。
    /// </summary>
    public void MarkUnavailable(string reason)
    {
        lock (_lock)
        {
            if (!IsReady) return;
            IsReady = false;
        }
        Logger.Error("Agent", $"{reason}；默认提供方将回退");
        try { UnexpectedExit?.Invoke(reason); }
        catch (Exception ex) { Logger.Error("Agent", "处理 sidecar 不可用回调失败", ex); }
    }

    /// <summary>等待 /health 返回 200，且进程未退出。</summary>
    private async Task<bool> WaitForHealthAsync(Process proc, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        while (DateTime.UtcNow < deadline)
        {
            if (proc.HasExited)
            {
                Logger.Error("Agent", $"sidecar 进程提前退出，退出码={proc.ExitCode}");
                return false;
            }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/health");
                req.Headers.Add("X-Agent-Token", Token);
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch
            {
                // 尚未监听，继续等待
            }
            await Task.Delay(300).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>把子进程输出按行转发到应用日志。</summary>
    private static async Task PumpLogsAsync(Process proc, string stream)
    {
        var reader = stream == "stdout" ? proc.StandardOutput : proc.StandardError;
        try
        {
            while (!proc.HasExited)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                // Python 侧的 WARNING 及以上按 Warn 记录，其余按 Info
                if (line.Contains(" ERROR ", StringComparison.Ordinal) ||
                    line.Contains(" WARNING [", StringComparison.Ordinal))
                    Logger.Warn("Agent", $"[py/{stream}] {line}");
                else
                    Logger.Info("Agent", $"[py/{stream}] {line}");
            }
        }
        catch { /* 进程退出时流会关闭 */ }
    }

    /// <summary>探测当前平台的 Python 解释器（&lt;BaseDir&gt;/.venv）。</summary>
    private static string? ResolvePython(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var venv = Path.Combine(App.BaseDir, ".venv");
        // 只探测当前平台的解释器布局：在 Windows 上执行 .venv/bin/python（Linux 符号链接）
        // 会抛 Win32Exception「不是有效的应用程序」，Linux 执行 python.exe 同理
        var candidate = OperatingSystem.IsWindows()
            ? Path.Combine(venv, "Scripts", "python.exe")
            : Path.Combine(venv, "bin", "python");
        if (File.Exists(candidate)) return candidate;

        // .venv 存在但没有本平台解释器：通常是跨平台拷贝/同步导致的布局不匹配
        if (Directory.Exists(venv))
        {
            Logger.Warn("Agent",
                $".venv 不是当前平台的虚拟环境（缺少 {candidate}），请在当前系统重新执行 uv sync 后再启动");
        }
        return null;
    }

    /// <summary>定位 agent/ 目录（含 pyproject.toml）。</summary>
    private static string? ResolveAgentDir()
    {
        var dir = Path.Combine(App.BaseDir, "agent");
        return File.Exists(Path.Combine(dir, "pyproject.toml")) ? dir : null;
    }

    /// <summary>确定工作空间目录（默认按用户隔离）。</summary>
    private static string ResolveWorkspace(string configuredRoot, string userName)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
            return configuredRoot.Replace("{user}", userName);

        var userDir = UserService.Instance.CurrentUserDir;
        if (string.IsNullOrEmpty(userDir))
            userDir = Path.Combine(App.UserDataDir, "users", userName);
        return Path.Combine(userDir, "agent_workspace");
    }

    /// <summary>申请一个空闲的环回端口。</summary>
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
