using System.IO;
using System.Runtime.CompilerServices;

namespace DesktopPetCSharp.Tests;

/// <summary>
/// 测试模块初始化器：在测试程序集加载时设置 DESKTOP_PET_BASE 环境变量，
/// 指向主项目根目录，让 ConfigService 能找到 config/ 目录。
/// 必须在任何测试代码访问 App.BaseDir 之前执行。
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // 测试项目位于 DesktopPetCSharp.Tests/，主项目位于其上级目录
        var testsDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(testsDir);
        // 从 bin/<config>/<tfm> 向上查找直到找到 DesktopPetCSharp.Tests 目录
        for (int i = 0; i < 8 && dir != null; i++)
        {
            if (dir.Name == "DesktopPetCSharp.Tests")
            {
                var baseDir = dir.Parent!.FullName;
                // App.ResolveBaseDir 要求 env 路径下存在 static/ 子目录，测试环境首次启动前创建空目录
                var staticDir = Path.Combine(baseDir, "static");
                Directory.CreateDirectory(staticDir);
                Environment.SetEnvironmentVariable("DESKTOP_PET_BASE", baseDir);
                return;
            }
            dir = dir.Parent;
        }
    }
}
