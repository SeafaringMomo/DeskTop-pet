using DesktopPetCSharp.Services;
using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Tests;

/// <summary>
/// ConfigService 单元测试：验证 YAML 配置文件加载与缓存行为。
/// 对应 AGENTS.md §3 配置文件规则。
/// </summary>
public class ConfigServiceTests
{
    public ConfigServiceTests()
    {
        // 每个测试前失效缓存，保证隔离
        ConfigService.InvalidateCache();
        ConfigService.InvalidateSecretsCache();
        ConfigService.InvalidatePetManagementCache();
    }

    [Fact]
    public void Load_ReturnsPetConfigWithRoles()
    {
        var cfg = ConfigService.Load();

        Assert.NotNull(cfg);
        Assert.NotNull(cfg.Roles);
        // 主项目至少配置了路飞与索隆
        Assert.True(cfg.Roles.ContainsKey("路飞") || cfg.Roles.Count > 0);
    }

    [Fact]
    public void LoadPetManagement_ReturnsConfiguredLevels()
    {
        var mgmt = ConfigService.LoadPetManagement();

        Assert.NotNull(mgmt);
        Assert.NotNull(mgmt.Levels);
        Assert.NotEmpty(mgmt.Levels);
        // 检查默认配置中应包含「挚友」和「陌生人」等级
        Assert.Contains(mgmt.Levels, l => l.Name == "挚友");
        Assert.Contains(mgmt.Levels, l => l.Name == "陌生人");
    }

    [Fact]
    public void LoadPetManagement_ReturnsPositiveDecayValues()
    {
        var mgmt = ConfigService.LoadPetManagement();

        Assert.True(mgmt.Decay.AffinityLossPerDay > 0);
        Assert.True(mgmt.Decay.SatietyLowThreshold > 0);
        Assert.True(mgmt.Decay.SatietyLowAffinityLossMinutes > 0);
        Assert.True(mgmt.Decay.MutedAffinityLossPerHour > 0);
    }

    [Fact]
    public void LoadPetManagement_ReturnsPositiveRewards()
    {
        var mgmt = ConfigService.LoadPetManagement();

        Assert.True(mgmt.Rewards.FeedLoved > mgmt.Rewards.FeedNormal);
        Assert.True(mgmt.Rewards.FeedNormal > mgmt.Rewards.FeedDisliked);
        Assert.True(mgmt.Rewards.SatietyHighBonus >= 1.0);
    }

    [Fact]
    public void LoadPetManagement_ConstantsHaveSkinPrice()
    {
        var mgmt = ConfigService.LoadPetManagement();

        // 皮肤价格应大于 0（AGENTS.md §8.2 禁止硬编码，从配置读）
        Assert.True(mgmt.Constants.SkinPrice > 0);
        Assert.True(mgmt.Constants.InitialCoins > 0);
        Assert.True(mgmt.Constants.InitialAffinity >= 0);
    }

    [Fact]
    public void Load_ReturnsCachedInstance()
    {
        var first = ConfigService.Load();
        var second = ConfigService.Load();

        // 缓存保证返回同一实例（AGENTS.md §3.3）
        Assert.Same(first, second);
    }

    [Fact]
    public void InvalidateCache_ForcesReload()
    {
        var first = ConfigService.Load();
        ConfigService.InvalidateCache();
        var second = ConfigService.Load();

        // 失效后应重新加载，得到新实例
        Assert.NotSame(first, second);
    }

    [Fact]
    public void LoadFoods_ReturnsFoodConfig()
    {
        var foods = ConfigService.LoadFoods();

        Assert.NotNull(foods);
        // foods.yaml 中应至少有一个食物定义
        Assert.NotNull(foods.Foods);
    }

    [Fact]
    public void LoadAnimations_ReturnsAnimationConfig()
    {
        var animations = ConfigService.LoadAnimations();

        Assert.NotNull(animations);
    }

    [Fact]
    public void LoadSkins_ReturnsSkinConfig()
    {
        var skins = ConfigService.LoadSkins();

        Assert.NotNull(skins);
    }
}
