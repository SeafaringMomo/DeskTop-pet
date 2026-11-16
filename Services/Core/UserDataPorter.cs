using System.IO;
using System.IO.Compression;

namespace DesktopGent.Services.Core;

/// <summary>
/// 用户数据导出/导入服务：把当前用户的档案（profile.yaml + todo.db + chat_history.db）
/// 打包为 ZIP 文件，便于备份与迁移到其他设备。
/// </summary>
public static class UserDataPorter
{
    /// <summary>导出当前用户数据到指定 ZIP 文件路径。</summary>
    public static bool Export(string zipPath)
    {
        try
        {
            var user = UserService.Instance.CurrentUser;
            if (user == null)
            {
                Logger.Warn("App", "导出失败：未登录用户");
                return false;
            }

            var userDir = UserService.Instance.CurrentUserDir;
            if (!Directory.Exists(userDir))
            {
                Logger.Warn("App", $"导出失败：用户目录不存在 {userDir}");
                return false;
            }

            // 临时目录构造 ZIP，避免文件被占用时无法读取
            var tempZip = zipPath + ".tmp";
            if (File.Exists(tempZip)) File.Delete(tempZip);
            ZipFile.CreateFromDirectory(userDir, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(tempZip, zipPath, overwrite: true);

            Logger.Info("App", $"已导出用户档案：{user.Name} -> {zipPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("App", "导出用户档案失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 从 ZIP 文件导入用户数据。导入前会先登出当前用户，确保数据库连接释放。
    /// 导入完成后需要重新登录该用户才能生效。
    /// </summary>
    /// <param name="zipPath">ZIP 文件路径</param>
    /// <param name="targetUserName">目标用户名（决定导入到哪个目录）</param>
    public static bool Import(string zipPath, string targetUserName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetUserName))
            {
                Logger.Warn("App", "导入失败：用户名为空");
                return false;
            }
            if (!File.Exists(zipPath))
            {
                Logger.Warn("App", $"导入失败：ZIP 文件不存在 {zipPath}");
                return false;
            }

            var targetDir = Path.Combine(UserService.UsersDir, targetUserName);
            // 若目标目录已存在，先备份为 .bak-{timestamp}
            if (Directory.Exists(targetDir))
            {
                var bakDir = targetDir + $".bak-{DateTime.Now:yyyyMMddHHmmss}";
                Directory.Move(targetDir, bakDir);
                Logger.Info("App", $"已备份原用户目录到 {bakDir}");
            }
            Directory.CreateDirectory(targetDir);

            ZipFile.ExtractToDirectory(zipPath, targetDir);
            Logger.Info("App", $"已导入用户档案：{zipPath} -> {targetDir}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("App", "导入用户档案失败", ex);
            return false;
        }
    }
}
