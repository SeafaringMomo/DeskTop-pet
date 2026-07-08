using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DesktopPetCSharp.Models;

/// <summary>
/// 待办项模型，移植自 todohelper20260629_csharp/Models/TodoItem.cs。
/// 增加 INotifyPropertyChanged 用于 WPF 双向绑定，并补充更新时间字段。
/// </summary>
public sealed class TodoItem : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private bool _isDone;

    public long Id { get; set; }

    /// <summary>任务内容</summary>
    public string Content
    {
        get => _content;
        set { if (_content != value) { _content = value; OnPropertyChanged(); } }
    }

    /// <summary>创建时间（UTC ISO 8601 存盘）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最后更新时间</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>是否已完成</summary>
    public bool IsDone
    {
        get => _isDone;
        set { if (_isDone != value) { _isDone = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDoneDisplay)); } }
    }

    /// <summary>列表展示用：未完成显示空，已完成显示 ✓</summary>
    [JsonIgnore]
    public string IsDoneDisplay => IsDone ? "✓" : "○";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
