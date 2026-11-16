using System.IO;
using DesktopGent.Models;
using Microsoft.Data.Sqlite;

namespace DesktopGent.Services.Data;

/// <summary>
/// 待办数据访问层，移植自 todohelper20260629_csharp/Services/TodoRepository.cs。
/// 数据库文件默认放在项目根 user_data/data/todo.db，方便用户查看与备份。
/// 扩展了 Delete 和 ClearDone 方法，并使用线程安全连接。
/// </summary>
public sealed class TodoRepository
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    /// <param name="dbPath">数据库文件完整路径。由 App 决定位置（项目根 user_data/data/todo.db）。</param>
    public TodoRepository(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connectionString = $"Data Source={_dbPath}";
        InitializeDatabase();
        System.Diagnostics.Debug.WriteLine($"[TodoRepository] 数据库路径: {_dbPath}");
    }

    public string DbPath => _dbPath;

    private void InitializeDatabase()
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Todo (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                IsDone INTEGER NOT NULL
            );";
        cmd.ExecuteNonQuery();

        // 兼容旧表（如果是从 todohelper 项目迁移来的）：缺列才补，避免重复 ALTER 抛 SqliteException
        if (!ColumnExists(conn, "UpdatedAt"))
        {
            cmd.CommandText = "ALTER TABLE Todo ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT '';";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>检查 Todo 表是否存在指定列。用 PRAGMA table_info 避免 ALTER 重复抛异常。</summary>
    private static bool ColumnExists(SqliteConnection conn, string columnName)
    {
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(Todo);";
        using var reader = checkCmd.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info 返回列：cid, name, type, notnull, dflt_value, pk
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private SqliteConnection OpenConn()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public IEnumerable<TodoItem> GetAll()
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Content, CreatedAt, UpdatedAt, IsDone FROM Todo ORDER BY IsDone ASC, Id DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return new TodoItem
            {
                Id = reader.GetInt64(0),
                Content = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                UpdatedAt = string.IsNullOrEmpty(reader.GetString(3)) ? DateTime.Now : DateTime.Parse(reader.GetString(3)),
                IsDone = reader.GetInt32(4) == 1
            };
        }
    }

    public void Insert(TodoItem item)
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Todo (Content, CreatedAt, UpdatedAt, IsDone) VALUES (@c, @d, @u, @i); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@c", item.Content);
        cmd.Parameters.AddWithValue("@d", item.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@u", item.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@i", item.IsDone ? 1 : 0);
        var scalar = cmd.ExecuteScalar();
        item.Id = scalar is long l ? l : Convert.ToInt64(scalar);
    }

    public void Update(TodoItem item)
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Todo SET Content = @c, UpdatedAt = @u, IsDone = @i WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@c", item.Content);
        cmd.Parameters.AddWithValue("@u", item.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@i", item.IsDone ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", item.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Todo WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void ClearDone()
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Todo WHERE IsDone = 1;";
        cmd.ExecuteNonQuery();
    }
}
