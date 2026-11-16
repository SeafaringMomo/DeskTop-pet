using DesktopGent.Services.Core;

namespace DesktopGent.Services.AI.Providers;

/// <summary>
/// 本地占位 AI 提供方，重写自原 ChatService 中的 KleeLocalChatService。
/// 不联网、不支持工具调用，仅做关键词匹配并模拟活泼语气。
/// 主角名取自配置文件（默认默认角色），回复自称跟随主角名，
/// 这样用户替换像素图、改 role 后离线模式称呼也保持一致。
/// 注册名："local-klee"（保留旧名兼容环境变量与已有配置）。
/// </summary>
public sealed class LocalKleeProvider : IAiProvider
{
    public string Name => "local-klee";
    public bool SupportsToolCalling => false;

    public Task<AiResponse> ChatAsync(AiRequest request, CancellationToken ct = default)
    {
        var msg = request.UserMessage?.Trim() ?? string.Empty;
        var role = CurrentRole();
        var reply = string.IsNullOrEmpty(msg)
            ? $"嗯？{role}在听呢！"
            : Reply(msg, role);
        return Task.FromResult(new AiResponse { Content = reply });
    }

    private static string CurrentRole()
    {
        try { return ConfigService.Load().Role; } catch { return "默认角色"; }
    }

    private static string Reply(string msg, string role)
    {
        if (msg.Contains("你好") || msg.Contains("Hello") || msg.Contains("Hi", StringComparison.OrdinalIgnoreCase))
            return $"嘿嘿，你好呀！我是{role}！";
        if (msg.Contains("名字") || msg.Contains("你是谁"))
            return $"我的名字是{role}！";
        if (msg.Contains("冒险") || msg.Contains("旅行") || msg.Contains("出海"))
            return $"走！我们一起去冒险吧！我知道好多好玩的地方～";
        if (msg.Contains("吃") || msg.Contains("饿") || msg.Contains("肉"))
            return "肚子饿了吗？吃饱了才有力气冒险呀！";
        if (msg.Contains("待办") || msg.Contains("todo") || msg.Contains("任务") || msg.Contains("记一下"))
            return "嗯…要记下待办事项吗？右键点击桌面助手，选『待办』就能打开了哦！";
        if (msg.Contains("再见") || msg.Contains("拜拜") || msg.Contains("bye", StringComparison.OrdinalIgnoreCase))
            return $"下次再来找{role}玩呀！再见啦～";
        if (msg.Contains("晚安") || msg.Contains("睡觉"))
            return "晚安！记得早点休息哦，明天我们继续冒险！";
        if (msg.Contains("早上好") || msg.Contains("早安"))
            return "早上好！今天也要元气满满地出发冒险！";
        if (msg.Contains("中午") || msg.Contains("午安"))
            return "中午好！要不要先歇一会儿再继续？";

        return $"嗯…「{msg}」…我不太懂，不过我们一起想想办法吧！";
    }
}
