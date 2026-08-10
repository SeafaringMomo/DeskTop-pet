using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Tests;

/// <summary>
/// UserProfile 单元测试：验证默认值、背包逻辑与好感度读取。
/// 对应 AGENTS.md §4 用户数据隔离规则。
/// </summary>
public class UserProfileTests
{
    [Fact]
    public void Defaults_RoleIsLuffy()
    {
        var profile = new UserProfile();

        Assert.Equal("路飞", profile.Role);
        Assert.Equal(0.4, profile.Scale);
        Assert.Equal("local-klee", profile.DefaultProvider);
    }

    [Fact]
    public void Defaults_EconomicSystemInitialized()
    {
        var profile = new UserProfile();

        Assert.Equal(100, profile.Coins);
        Assert.NotNull(profile.UnlockedSkins);
        Assert.NotNull(profile.CurrentSkins);
    }

    [Fact]
    public void Defaults_AffinityAndSatietyDictionariesEmpty()
    {
        var profile = new UserProfile();

        Assert.NotNull(profile.Affinity);
        Assert.NotNull(profile.Satiety);
        Assert.Empty(profile.Affinity);
        Assert.Empty(profile.Satiety);
    }

    [Fact]
    public void GetAffinity_ReturnsDefault_WhenNotSet()
    {
        var profile = new UserProfile();

        // 未设置时返回默认值 50
        Assert.Equal(50, profile.GetAffinity("路飞"));
        Assert.Equal(50, profile.GetAffinity("索隆"));
    }

    [Fact]
    public void GetAffinity_ReturnsSetValue()
    {
        var profile = new UserProfile
        {
            Affinity = new() { ["路飞"] = 75 }
        };

        Assert.Equal(75, profile.GetAffinity("路飞"));
        Assert.Equal(50, profile.GetAffinity("索隆"));
    }

    [Fact]
    public void GetSatiety_ReturnsDefault_WhenNotSet()
    {
        var profile = new UserProfile();

        Assert.Equal(50, profile.GetSatiety("路飞"));
    }

    [Fact]
    public void AddInventory_IncreasesCount()
    {
        var profile = new UserProfile();

        profile.AddInventory("apple", 3);
        Assert.Equal(3, profile.GetInventory("apple"));

        profile.AddInventory("apple", 2);
        Assert.Equal(5, profile.GetInventory("apple"));
    }

    [Fact]
    public void AddInventory_IgnoresNonPositiveCount()
    {
        var profile = new UserProfile();

        profile.AddInventory("apple", 0);
        Assert.Equal(0, profile.GetInventory("apple"));

        profile.AddInventory("apple", -1);
        Assert.Equal(0, profile.GetInventory("apple"));
    }

    [Fact]
    public void ConsumeInventory_ReducesCount()
    {
        var profile = new UserProfile();
        profile.AddInventory("apple", 5);

        var ok = profile.ConsumeInventory("apple", 2);

        Assert.True(ok);
        Assert.Equal(3, profile.GetInventory("apple"));
    }

    [Fact]
    public void ConsumeInventory_Fails_WhenInsufficient()
    {
        var profile = new UserProfile();
        profile.AddInventory("apple", 1);

        var ok = profile.ConsumeInventory("apple", 2);

        Assert.False(ok);
        Assert.Equal(1, profile.GetInventory("apple"));
    }

    [Fact]
    public void ConsumeInventory_DoesNotReduce_WhenNoStock()
    {
        var profile = new UserProfile();

        var ok = profile.ConsumeInventory("nonexistent", 1);

        Assert.False(ok);
        Assert.Equal(0, profile.GetInventory("nonexistent"));
    }

    [Fact]
    public void SetCurrentSkin_AndGetCurrentSkin_RoundTrip()
    {
        var profile = new UserProfile();

        profile.SetCurrentSkin("路飞", "samurai");
        Assert.Equal("samurai", profile.GetCurrentSkin("路飞"));
        Assert.Equal("", profile.GetCurrentSkin("索隆"));
    }
}
