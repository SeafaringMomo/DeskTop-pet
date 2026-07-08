using System.IO;

namespace DesktopPetCSharp.Services;

/// <summary>
/// 轻量级文件日志服务。记录用户每一次操作和应用关键事件，便于排查问题与回溯行为。
/// 日志按天滚动，文件位于 &lt;BaseDir&gt;/logs/app_YYYYMMDD.log。
/// 线程安全（写文件加锁），异步落盘不阻塞 UI（每次写仅一次小文件追加）。
/// </summary>
public static class Logger
{
    private static string? _logDir;
    private static readonly object _lock = new();

    /// <summary>初始化日志目录。应在应用启动时调用一次。</summary>
    public static void Init(string baseDir)
    {
        _logDir = Path.Combine(baseDir, "logs");
        try { Directory.CreateDirectory(_logDir); } catch { }
        Info("Logger", $"日志系统初始化，目录: {_logDir}");
    }

    private static string? CurrentFile =>
        _logDir == null ? null : Path.Combine(_logDir, $"app_{DateTime.Now:yyyyMMdd}.log");

    /// <summary>常规信息：用户操作、状态切换、正常事件。</summary>
    public static void Info(string category, string message) => Write("INFO", category, message);

    /// <summary>警告：可恢复的异常、非预期但可继续的情况。</summary>
    public static void Warn(string category, string message) => Write("WARN", category, message);

    /// <summary>错误：影响功能的异常。</summary>
    public static void Error(string category, string message) => Write("ERROR", category, message);

    /// <summary>错误：带异常对象。</summary>
    public static void Error(string category, string message, Exception ex)
        => Write("ERROR", category, $"{message} | {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string category, string message)
    {
        var file = CurrentFile;
        if (file == null) return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-5}] [{category}] {message}{Environment.NewLine}";
        lock (_lock)
        {
            try { File.AppendAllText(file, line); } catch { /* 日志失败不应影响主流程 */ }
        }
    }
}
