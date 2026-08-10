using System.IO;
using DesktopPetCSharp.Services;
using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Tests;

/// <summary>
/// TodoRepository 单元测试：验证待办 CRUD 操作。
/// 使用临时 SQLite 数据库，测试完成后清理。
/// </summary>
public class TodoRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly TodoRepository _repo;

    public TodoRepositoryTests()
    {
        // 在临时目录创建测试数据库
        var tempDir = Path.Combine(Path.GetTempPath(), "DesktopPetCSharpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _tempDbPath = Path.Combine(tempDir, "test_todo.db");
        _repo = new TodoRepository(_tempDbPath);
    }

    public void Dispose()
    {
        try
        {
            var dir = Path.GetDirectoryName(_tempDbPath);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void Constructor_CreatesDatabaseFile()
    {
        Assert.True(File.Exists(_tempDbPath));
    }

    [Fact]
    public void Insert_AssignsId()
    {
        var item = new TodoItem
        {
            Content = "测试任务",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDone = false
        };

        _repo.Insert(item);

        Assert.True(item.Id > 0);
    }

    [Fact]
    public void GetAll_ReturnsInsertedItems()
    {
        _repo.Insert(new TodoItem
        {
            Content = "任务1",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDone = false
        });
        _repo.Insert(new TodoItem
        {
            Content = "任务2",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDone = false
        });

        var all = _repo.GetAll().ToList();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Content == "任务1");
        Assert.Contains(all, t => t.Content == "任务2");
    }

    [Fact]
    public void Update_TogglesIsDone()
    {
        var item = new TodoItem
        {
            Content = "待切换",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDone = false
        };
        _repo.Insert(item);

        item.IsDone = true;
        item.UpdatedAt = DateTime.Now;
        _repo.Update(item);

        var all = _repo.GetAll().ToList();
        var updated = all.First(t => t.Id == item.Id);
        Assert.True(updated.IsDone);
    }

    [Fact]
    public void Delete_RemovesItem()
    {
        var item = new TodoItem
        {
            Content = "待删除",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDone = false
        };
        _repo.Insert(item);

        _repo.Delete(item.Id);

        var all = _repo.GetAll().ToList();
        Assert.DoesNotContain(all, t => t.Id == item.Id);
    }

    [Fact]
    public void GetAll_ReturnsItemsOrderedByIsDoneThenIdDesc()
    {
        // 插入 3 个：1 已完成、2 未完成
        _repo.Insert(new TodoItem { Content = "未完成1", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, IsDone = false });
        _repo.Insert(new TodoItem { Content = "已完成", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, IsDone = true });
        _repo.Insert(new TodoItem { Content = "未完成2", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, IsDone = false });

        var all = _repo.GetAll().ToList();

        // 未完成的在前，按 Id 倒序
        Assert.False(all.First().IsDone);
        Assert.True(all.Last().IsDone);
    }
}
