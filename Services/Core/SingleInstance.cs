using System.Threading;

namespace DesktopGent.Services.Core;

/// <summary>
/// 单实例检测器：使用命名 Mutex 保证整个系统只运行一个桌面助手实例。
/// 对应 AGENTS.md §4.2 并发限制（多实例同时写同一用户档案会冲突）。
///
/// 用法：
/// <code>
/// if (!SingleInstance.TryAcquire(out var mutex))
/// {
///     MessageBox.Show("桌面助手已经在运行中");
///     Shutdown();
/// }
/// // 应用退出时调用 mutex.ReleaseMutex() 或让其随进程结束自动释放
/// </code>
/// </summary>
public static class SingleInstance
{
    private const string MutexName = "DesktopGent_SingleInstance_Mutex";

    /// <summary>
    /// 尝试获取单实例 Mutex。若已存在则返回 false。
    /// 注意：返回的 mutex 引用必须保持存活（如存到静态字段），否则会被 GC 提前释放。
    /// </summary>
    public static bool TryAcquire(out Mutex? mutex)
    {
        mutex = new Mutex(initiallyOwned: true, name: MutexName, out bool createdNew);
        if (createdNew) return true;

        // 已有实例在运行，释放我们持有的引用
        mutex.Dispose();
        mutex = null;
        return false;
    }
}
