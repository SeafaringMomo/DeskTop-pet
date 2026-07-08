using System.IO;
using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Services;

/// <summary>
/// 皮肤服务（P4）：管理皮肤配置的加载、查询、装备、解锁。
///
/// 皮肤 = 精灵图 + 主题配色 + 声音，与角色绑定但可独立切换。
/// 用户偏好（已解锁皮肤、当前装备皮肤）存储在 UserProfile。
/// </summary>
public sealed class SkinService
{
    public static SkinService Instance { get; } = new();

    private SkinService() { }

    // ===== 配置查询 =====

    /// <summary>获取所有皮肤列表。</summary>
    public List<Skin> GetAllSkins() => ConfigService.LoadSkins().Skins;

    /// <summary>按皮肤 ID 获取皮肤。不存在返回 null。</summary>
    public Skin? GetSkin(string skinId)
        => GetAllSkins().FirstOrDefault(s => s.Id == skinId);

    /// <summary>获取某角色的所有皮肤。</summary>
    public List<Skin> GetSkinsByRole(string roleName)
        => GetAllSkins().Where(s => s.Role == roleName).ToList();

    /// <summary>获取某角色的默认皮肤。无则返回该角色的第一个皮肤。</summary>
    public Skin? GetDefaultSkin(string roleName)
    {
        var skins = GetSkinsByRole(roleName);
        return skins.FirstOrDefault(s => s.IsDefault) ?? skins.FirstOrDefault();
    }

    /// <summary>获取用户当前装备的皮肤（未设置则返回默认皮肤）。</summary>
    public Skin? GetCurrentSkin(string roleName)
    {
        var user = UserService.Instance.CurrentUser;
        if (user != null)
        {
            var skinId = user.GetCurrentSkin(roleName);
            if (!string.IsNullOrEmpty(skinId))
            {
                var skin = GetSkin(skinId);
                if (skin != null) return skin;
            }
        }
        return GetDefaultSkin(roleName);
    }

    // ===== 路径辅助 =====

    /// <summary>皮肤精灵图目录的完整路径（如 D:\...\static\luffy\classic）。</summary>
    public string GetSkinDir(Skin skin)
        => Path.Combine(App.BaseDir, skin.SpriteDir);

    /// <summary>皮肤目录下某文件的完整路径。</summary>
    public string GetSkinFile(Skin skin, string fileName)
        => Path.Combine(GetSkinDir(skin), fileName);

    /// <summary>获取皮肤的背景音乐路径（无则 null）。</summary>
    public string? GetBgmPath(Skin skin)
    {
        if (string.IsNullOrEmpty(skin.Audio.Bgm)) return null;
        var p = GetSkinFile(skin, skin.Audio.Bgm);
        return File.Exists(p) ? p : null;
    }

    /// <summary>获取皮肤的问候语音路径（按时段）。无则回退到默认皮肤，再无则 null。</summary>
    public string? GetGreetingPath(Skin skin)
    {
        int h = DateTime.Now.Hour;
        string? fileName = h switch
        {
            >= 4 and <= 10 => skin.Audio.GreetingMorning,
            >= 11 and <= 14 => skin.Audio.GreetingNoon,
            > 17 and <= 24 => skin.Audio.GreetingEvening,
            _ => null
        };
        if (string.IsNullOrEmpty(fileName)) return null;
        var p = GetSkinFile(skin, fileName);
        if (File.Exists(p)) return p;

        // 回退到默认皮肤
        var def = GetDefaultSkin(skin.Role);
        if (def != null && def.Id != skin.Id)
        {
            fileName = h switch
            {
                >= 4 and <= 10 => def.Audio.GreetingMorning,
                >= 11 and <= 14 => def.Audio.GreetingNoon,
                > 17 and <= 24 => def.Audio.GreetingEvening,
                _ => null
            };
            if (string.IsNullOrEmpty(fileName)) return null;
            p = GetSkinFile(def, fileName);
            return File.Exists(p) ? p : null;
        }
        return null;
    }

    /// <summary>获取皮肤的喂食音效路径（无则 null）。</summary>
    public string? GetFeedSfxPath(Skin skin)
    {
        if (string.IsNullOrEmpty(skin.Audio.FeedSfx)) return null;
        var p = GetSkinFile(skin, skin.Audio.FeedSfx);
        return File.Exists(p) ? p : null;
    }

    /// <summary>获取皮肤的退出语音路径（无则回退默认皮肤）。</summary>
    public string? GetFarewellPath(Skin skin)
    {
        if (!string.IsNullOrEmpty(skin.Audio.Farewell))
        {
            var p = GetSkinFile(skin, skin.Audio.Farewell);
            if (File.Exists(p)) return p;
        }
        var def = GetDefaultSkin(skin.Role);
        if (def != null && def.Id != skin.Id && !string.IsNullOrEmpty(def.Audio.Farewell))
        {
            var p = GetSkinFile(def, def.Audio.Farewell!);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // ===== 解锁与装备 =====

    /// <summary>判断皮肤是否已解锁。</summary>
    public bool IsUnlocked(string skinId)
        => UserService.Instance.CurrentUser?.UnlockedSkins.Contains(skinId) ?? false;

    /// <summary>解锁皮肤（加入已解锁列表，不扣金币）。</summary>
    public void Unlock(string skinId)
    {
        var user = UserService.Instance.CurrentUser;
        if (user == null) return;
        if (!user.UnlockedSkins.Contains(skinId))
        {
            user.UnlockedSkins.Add(skinId);
            UserService.Instance.SaveCurrentProfile();
            Logger.Info("Skin", $"解锁皮肤: {skinId}");
        }
    }

    /// <summary>装备皮肤（需已解锁）。返回是否成功。</summary>
    public bool Equip(string skinId)
    {
        var skin = GetSkin(skinId);
        if (skin == null) return false;
        var user = UserService.Instance.CurrentUser;
        if (user == null) return false;
        if (!user.UnlockedSkins.Contains(skinId))
        {
            Logger.Warn("Skin", $"皮肤未解锁，无法装备: {skinId}");
            return false;
        }
        user.SetCurrentSkin(skin.Role, skinId);
        UserService.Instance.SaveCurrentProfile();
        Logger.Info("Skin", $"装备皮肤: {skinId}（角色={skin.Role}）");
        return true;
    }

    /// <summary>购买皮肤（扣金币 + 解锁）。返回是否成功。</summary>
    public bool Purchase(string skinId, out string failReason)
    {
        failReason = "";
        var skin = GetSkin(skinId);
        if (skin == null) { failReason = "皮肤不存在"; return false; }
        var user = UserService.Instance.CurrentUser;
        if (user == null) { failReason = "未登录"; return false; }
        if (user.UnlockedSkins.Contains(skinId)) { failReason = "已拥有"; return false; }

        var mgmt = ConfigService.LoadPetManagement();
        var price = mgmt.Constants.SkinPrice;

        // 好感度折扣
        var affinity = user.GetAffinity(skin.Role);
        var level = AffinityService.Instance.GetLevel(affinity);
        var finalPrice = (int)Math.Round(price * level.SkinDiscount);

        if (user.Coins < finalPrice) { failReason = $"金币不足（需 {finalPrice}，当前 {user.Coins}）"; return false; }

        user.Coins -= finalPrice;
        user.UnlockedSkins.Add(skinId);
        UserService.Instance.SaveCurrentProfile();
        Logger.Info("Skin", $"购买皮肤: {skinId}，花费 {finalPrice} 金币（原价 {price}，{level.Name} {level.SkinDiscount:P0}）");
        return true;
    }

    /// <summary>初始化新用户的默认皮肤（解锁所有 is_default 皮肤）。</summary>
    public void InitDefaultSkins(UserProfile user)
    {
        foreach (var skin in GetAllSkins().Where(s => s.IsDefault))
        {
            if (!user.UnlockedSkins.Contains(skin.Id))
                user.UnlockedSkins.Add(skin.Id);
        }
        // 为每个角色设置默认皮肤为当前皮肤
        foreach (var role in ConfigService.Load().Roles.Keys)
        {
            var def = GetDefaultSkin(role);
            if (def != null && string.IsNullOrEmpty(user.GetCurrentSkin(role)))
                user.SetCurrentSkin(role, def.Id);
        }
    }
}
