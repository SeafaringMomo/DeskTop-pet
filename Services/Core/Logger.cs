using System.IO;
using System.Linq;

namespace DesktopGent.Services.Core;

/// <summary>
/// 轻量级文件日志服务。记录用户每一次操作和应用关键事件，便于排查问题与回溯行为。
/// 日志按天滚动，文件位于 &lt;BaseDir&gt;/logs/app_YYYYMMDD.log。
/// 线程安全（写文件加锁），异步落盘不阻塞 UI（每次写仅一次小文件追加）。
///
/// 容量管理：
/// - 单日志文件大小上限 <see cref="MaxLogSizeBytes"/>（10 MB），超过则滚动归档为 app_YYYYMMDD_HHmmss.1.log
/// - 启动时自动清理 <see cref="RetentionDays"/>（7 天）前的日志文件
/// </summary>
public static class Logger
{
    /// <summary>单个日志文件大小上限（字节）。超过后自动滚动归档。</summary>
    public const long MaxLogSizeBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>日志保留天数。早于此天数的日志文件在初始化时被清理。</summary>
    public const int RetentionDays = 7;

    private static string? _logDir;
    private static readonly object _lock = new();

    /// <summary>初始化日志目录。应在应用启动时调用一次。</summary>
    public static void Init(string baseDir)
    {
        _logDir = Path.Combine(baseDir, "logs");
        try { Directory.CreateDirectory(_logDir); } catch { }
        try { CleanupOldLogs(); } catch { /* 清理失败不应影响启动 */ }
        Info("Logger", $"日志系统初始化，目录: {_logDir}，保留 {RetentionDays} 天，单文件上限 {MaxLogSizeBytes / 1024 / 1024}MB");
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
            try
            {
                // 大小上限检查：超过则滚动归档（原文件追加时间戳后缀，然后从空文件继续写）
                if (File.Exists(file) && new FileInfo(file).Length >= MaxLogSizeBytes)
                {
                    ArchiveCurrentFile(file);
                }
                File.AppendAllText(file, line);
            }
            catch { /* 日志失败不应影响主流程 */ }
        }
    }

    /// <summary>把当前日志文件归档为 app_YYYYMMDD_HHmmss.1.log，让原文件从空开始。</summary>
    private static void ArchiveCurrentFile(string currentFile)
    {
        if (!File.Exists(currentFile)) return;
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var archive = Path.Combine(
            Path.GetDirectoryName(currentFile) ?? _logDir ?? "",
            $"app_{stamp}.1.log");
        // 避免重名（极短时间内多次归档）
        int seq = 1;
        while (File.Exists(archive))
        {
            seq++;
            archive = Path.Combine(
                Path.GetDirectoryName(currentFile) ?? _logDir ?? "",
                $"app_{stamp}.{seq}.log");
        }
        try
        {
            File.Move(currentFile, archive);
        }
        catch
        {
            // 移动失败则清空原文件，避免持续超过上限
            try { File.WriteAllText(currentFile, ""); } catch { }
        }
    }

    /// <summary>清理超过保留天数的日志文件。仅在初始化时调用一次。</summary>
    private static void CleanupOldLogs()
    {
        if (string.IsNullOrEmpty(_logDir) || !Directory.Exists(_logDir)) return;
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var f in Directory.EnumerateFiles(_logDir, "app_*.log*"))
        {
            try
            {
                var info = new FileInfo(f);
                if (info.LastWriteTime < cutoff)
                {
                    File.Delete(f);
                }
            }
            catch { /* 单个文件删除失败不影响其他 */ }
        }
    }
}
