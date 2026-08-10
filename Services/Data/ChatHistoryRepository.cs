using System.IO;
using DesktopPetCSharp.Models;
using Microsoft.Data.Sqlite;

namespace DesktopPetCSharp.Services;

/// <summary>
/// 对话记录仓储：把用户与 AI 的对话持久化到 SQLite。
/// 数据库路径按用户隔离：user_data/users/&lt;name&gt;/chat_history.db。
///
/// 表结构：
/// - ChatSession(Id, Title, Role, Provider, CreatedAt, UpdatedAt)
/// - ChatMessage(Id, SessionId, Role, Content, Timestamp, ToolCalls)
/// </summary>
public sealed class ChatHistoryRepository
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    /// <param name="dbPath">数据库文件完整路径。由 UserService.CurrentUserDir 决定。</param>
    public ChatHistoryRepository(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        _connectionString = $"Data Source={_dbPath}";
        InitializeDatabase();
    }

    public string DbPath => _dbPath;

    private void InitializeDatabase()
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS ChatSession (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Role TEXT NOT NULL,
                Provider TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ChatMessage (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                Content TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                ToolCalls TEXT,
                FOREIGN KEY(SessionId) REFERENCES ChatSession(Id)
            );
            CREATE INDEX IF NOT EXISTS idx_chatmsg_session ON ChatMessage(SessionId);";
        cmd.ExecuteNonQuery();

        // 向后兼容：旧表缺少 ToolCallId / ToolName 列，缺失则补齐
        // 用于 OpenAI 多轮工具调用持久化（P1.1 修复）
        TryAddColumn(conn, "ChatMessage", "ToolCallId", "TEXT");
        TryAddColumn(conn, "ChatMessage", "ToolName", "TEXT");
    }

    /// <summary>尝试给表添加列。若列已存在则静默忽略（兼容已存在的旧表）。</summary>
    private static void TryAddColumn(SqliteConnection conn, string table, string column, string type)
    {
        try
        {
            var check = conn.CreateCommand();
            check.CommandText = $"PRAGMA table_info({table});";
            using var r = check.ExecuteReader();
            while (r.Read())
            {
                if (r.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
            alter.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // 列已存在或其他问题，忽略
        }
    }

    private SqliteConnection OpenConn()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>创建新会话。</summary>
    public ChatSession CreateSession(string role, string provider)
    {
        var now = DateTime.Now.ToString("o");
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ChatSession (Title, Role, Provider, CreatedAt, UpdatedAt)
            VALUES (@title, @role, @provider, @now, @now);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@title", "新对话");
        cmd.Parameters.AddWithValue("@role", role ?? "");
        cmd.Parameters.AddWithValue("@provider", provider ?? "");
        cmd.Parameters.AddWithValue("@now", now);
        var id = (long)cmd.ExecuteScalar()!;
        return new ChatSession
        {
            Id = id,
            Title = "新对话",
            Role = role ?? "",
            Provider = provider ?? "",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 添加一条消息到指定会话。若会话标题为"新对话"则自动更新为首条消息前 20 字。
    /// </summary>
    /// <param name="toolCalls">assistant 消息携带的 tool_calls JSON（OpenAI 协议）</param>
    /// <param name="toolCallId">tool 角色消息对应的调用 ID</param>
    /// <param name="toolName">tool 角色消息对应的工具名</param>
    public void AddMessage(long sessionId, string role, string content,
        string? toolCalls = null, string? toolCallId = null, string? toolName = null)
    {
        var now = DateTime.Now.ToString("o");
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ChatMessage (SessionId, Role, Content, Timestamp, ToolCalls, ToolCallId, ToolName)
            VALUES (@sid, @role, @content, @ts, @tc, @tid, @tn);
            UPDATE ChatSession SET UpdatedAt = @ts WHERE Id = @sid;
            UPDATE ChatSession SET Title = @title
            WHERE Id = @sid AND Title = '新对话';";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@role", role ?? "");
        cmd.Parameters.AddWithValue("@content", content ?? "");
        cmd.Parameters.AddWithValue("@ts", now);
        cmd.Parameters.AddWithValue("@tc", (object?)toolCalls ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tid", (object?)toolCallId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tn", (object?)toolName ?? DBNull.Value);
        var title = (content ?? "").Length > 20
            ? (content ?? "").Substring(0, 20) + "…"
            : (content ?? "");
        cmd.Parameters.AddWithValue("@title", string.IsNullOrEmpty(title) ? "新对话" : title);
        cmd.ExecuteNonQuery();
    }

    /// <summary>列出所有会话（按更新时间降序）。</summary>
    public List<ChatSession> ListSessions()
    {
        var list = new List<ChatSession>();
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Role, Provider, CreatedAt, UpdatedAt FROM ChatSession ORDER BY UpdatedAt DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ChatSession
            {
                Id = r.GetInt64(0),
                Title = r.GetString(1),
                Role = r.GetString(2),
                Provider = r.IsDBNull(3) ? "" : r.GetString(3),
                CreatedAt = r.GetString(4),
                UpdatedAt = r.GetString(5)
            });
        }
        return list;
    }

    /// <summary>获取指定会话的所有消息（按时间升序）。</summary>
    public List<ChatMessageRecord> GetMessages(long sessionId)
    {
        var list = new List<ChatMessageRecord>();
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, SessionId, Role, Content, Timestamp, ToolCalls, ToolCallId, ToolName FROM ChatMessage WHERE SessionId = @sid ORDER BY Id ASC;";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ChatMessageRecord
            {
                Id = r.GetInt64(0),
                SessionId = r.GetInt64(1),
                Role = r.GetString(2),
                Content = r.GetString(3),
                Timestamp = r.GetString(4),
                ToolCalls = r.IsDBNull(5) ? null : r.GetString(5),
                ToolCallId = r.IsDBNull(6) ? null : r.GetString(6),
                ToolName = r.IsDBNull(7) ? null : r.GetString(7)
            });
        }
        return list;
    }

    /// <summary>删除指定会话（连同其所有消息）。</summary>
    public void DeleteSession(long sessionId)
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ChatMessage WHERE SessionId = @sid; DELETE FROM ChatSession WHERE Id = @sid;";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>清空所有会话与消息。</summary>
    public void ClearAll()
    {
        using var conn = OpenConn();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ChatMessage; DELETE FROM ChatSession;";
        cmd.ExecuteNonQuery();
    }
}
