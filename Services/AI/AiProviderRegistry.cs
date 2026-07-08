using System.Collections.Concurrent;

namespace DesktopPetCSharp.Services.AI;

/// <summary>
/// AI 提供方注册表。运行时可注册多个提供方并切换默认。
/// 实现为单例，全局可访问 <see cref="Instance"/>。
/// </summary>
public sealed class AiProviderRegistry
{
    private readonly ConcurrentDictionary<string, IAiProvider> _providers = new();
    private string? _defaultName;

    public static AiProviderRegistry Instance { get; } = new();

    /// <summary>注册一个提供方。同名覆盖。</summary>
    public void Register(IAiProvider provider)
        => _providers[provider.Name] = provider;

    /// <summary>注销指定提供方。</summary>
    public bool Unregister(string name) => _providers.TryRemove(name, out _);

    /// <summary>获取所有已注册提供方名称。</summary>
    public IEnumerable<string> Names => _providers.Keys;

    /// <summary>当前默认提供方（用于 UI 切换、回退）。</summary>
    public string? DefaultName
    {
        get => _defaultName ??= _providers.Keys.FirstOrDefault();
        set => _defaultName = value;
    }

    /// <summary>当前默认提供方实例。若无则返回 null。</summary>
    public IAiProvider? Default
        => _defaultName != null && _providers.TryGetValue(_defaultName, out var p)
            ? p
            : _providers.Values.FirstOrDefault();

    /// <summary>按名称获取提供方。</summary>
    public IAiProvider? Get(string name)
        => _providers.TryGetValue(name, out var p) ? p : null;

    /// <summary>切换默认提供方。</summary>
    public bool TrySetDefault(string name)
    {
        if (!_providers.ContainsKey(name)) return false;
        _defaultName = name;
        return true;
    }
}
