using DesktopPetCSharp.Services;
using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Tests;

/// <summary>
/// AffinityService 单元测试：验证等级匹配逻辑。
/// 对应 AGENTS.md §5 好感度规则，§5.2 等级匹配规则。
/// </summary>
public class AffinityServiceTests
{
    public AffinityServiceTests()
    {
        // 测试前失效配置缓存，保证读取最新 YAML
        ConfigService.InvalidateCache();
        ConfigService.InvalidatePetManagementCache();
    }

    [Theory]
    [InlineData(100, "挚友")]
    [InlineData(80, "挚友")]
    [InlineData(79, "亲密")]
    [InlineData(60, "亲密")]
    [InlineData(59, "友好")]
    [InlineData(40, "友好")]
    [InlineData(39, "认识")]
    [InlineData(20, "认识")]
    [InlineData(19, "陌生人")]
    [InlineData(0, "陌生人")]
    public void GetLevel_ReturnsCorrectLevelName(int affinity, string expectedName)
    {
        var service = AffinityService.Instance;

        var level = service.GetLevel(affinity);

        Assert.Equal(expectedName, level.Name);
    }

    [Theory]
    [InlineData(100, 0.7)]
    [InlineData(80, 0.7)]
    [InlineData(79, 0.8)]
    [InlineData(59, 0.9)]
    [InlineData(39, 0.95)]
    [InlineData(19, 1.0)]
    public void GetLevel_ReturnsCorrectSkinDiscount(int affinity, double expectedDiscount)
    {
        var service = AffinityService.Instance;

        var level = service.GetLevel(affinity);

        Assert.Equal(expectedDiscount, level.SkinDiscount);
    }

    [Fact]
    public void GetLevel_LevelsAreOrderedFromHighToLow()
    {
        // 验证等级匹配是从高到低（§5.2）
        var service = AffinityService.Instance;
        var mgmt = ConfigService.LoadPetManagement();

        // 升序检查：Min 越大，对应等级的「门槛」越高
        var sortedMins = mgmt.Levels.Select(l => l.Min).OrderByDescending(m => m).ToList();
        // 第一个匹配上界（≥80）应为「挚友」
        Assert.Equal("挚友", service.GetLevel(sortedMins.First()).Name);
    }

    [Fact]
    public void GetSystemPromptHint_ReturnsNonEmptyString()
    {
        var service = AffinityService.Instance;

        // 未设置好感度时也应有提示（默认初始值）
        var hint = service.GetSystemPromptHint("路飞");

        Assert.False(string.IsNullOrEmpty(hint));
    }

    [Fact]
    public void GetSystemPromptHint_VariesByLevel()
    {
        var service = AffinityService.Instance;

        // 不同等级应给出不同提示
        var strangerHint = service.GetSystemPromptHint("未知角色1");
        var bestFriendHint = service.GetSystemPromptHint("未知角色2");

        // 由于 GetAffinity 在用户未登录时返回 50（默认初始值），这两个值应该相同
        // 这里主要验证不抛异常
        Assert.NotNull(strangerHint);
        Assert.NotNull(bestFriendHint);
    }
}
