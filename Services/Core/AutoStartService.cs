using Microsoft.Win32;

namespace DesktopGent.Services.Core;

/// <summary>
/// 开机自启动服务：通过注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// 实现开机自动启动桌面助手。仅影响当前 Windows 用户。
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DesktopGent";

    /// <summary>检查当前是否已设置开机自启。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            Logger.Warn("App", $"读取开机自启状态失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>启用开机自启。</summary>
    public static bool Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            // 当前可执行文件路径（dotnet run 时是 dotnet.exe，发布后是 .exe）
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;
            key?.SetValue(AppName, $"\"{exePath}\"");
            Logger.Info("App", $"已启用开机自启：{exePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("App", "启用开机自启失败", ex);
            return false;
        }
    }

    /// <summary>禁用开机自启。</summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
            Logger.Info("App", "已禁用开机自启");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("App", "禁用开机自启失败", ex);
            return false;
        }
    }
}
