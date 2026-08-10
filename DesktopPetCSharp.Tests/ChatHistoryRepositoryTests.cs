using System.IO;
using DesktopPetCSharp.Services;
using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Tests;

/// <summary>
/// ChatHistoryRepository 单元测试：验证会话与消息 CRUD，重点验证 P1.1 修复
/// （assistant 的 tool_calls、tool 角色消息的 tool_call_id/tool_name 持久化与重放）。
/// 对应 AGENTS.md §7.3 OpenAI 兼容接口已知限制。
/// </summary>
public class ChatHistoryRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ChatHistoryRepository _repo;

    public ChatHistoryRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DesktopPetCSharpChatTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "test_chat.db");
        _repo = new ChatHistoryRepository(dbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void CreateSession_AssignsId()
    {
        var session = _repo.CreateSession("路飞", "local-klee");

        Assert.True(session.Id > 0);
        Assert.Equal("新对话", session.Title);
        Assert.Equal("路飞", session.Role);
        Assert.Equal("local-klee", session.Provider);
    }

    [Fact]
    public void AddMessage_PersistsUserMessage()
    {
        var session = _repo.CreateSession("路飞", "local-klee");

        _repo.AddMessage(session.Id, "user", "你好");

        var messages = _repo.GetMessages(session.Id);
        Assert.Single(messages);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("你好", messages[0].Content);
        Assert.Null(messages[0].ToolCalls);
        Assert.Null(messages[0].ToolCallId);
        Assert.Null(messages[0].ToolName);
    }

    [Fact]
    public void AddMessage_UpdatesSessionTitle_OnFirstMessage()
    {
        var session = _repo.CreateSession("路飞", "local-klee");

        _repo.AddMessage(session.Id, "user", "这是我刚才说的一段较长的话用于测试标题截断功能");

        var sessions = _repo.ListSessions();
        Assert.Single(sessions);
        // 标题应被截断为前 20 字 + "…"
        Assert.Contains("…", sessions[0].Title);
    }

    [Fact]
    public void AddMessage_PersistsAssistantToolCalls()
    {
        var session = _repo.CreateSession("路飞", "openai");
        var toolCallsJson = @"[{""id"":""call_abc"",""type"":""function"",""function"":{""name"":""addTodo"",""arguments"":""{}""}}]";

        _repo.AddMessage(session.Id, "assistant", "", toolCalls: toolCallsJson);

        var messages = _repo.GetMessages(session.Id);
        Assert.Single(messages);
        Assert.Equal("assistant", messages[0].Role);
        Assert.Equal(toolCallsJson, messages[0].ToolCalls);
    }

    [Fact]
    public void AddMessage_PersistsToolRoleMessage_WithToolCallIdAndName()
    {
        var session = _repo.CreateSession("路飞", "openai");

        _repo.AddMessage(session.Id, "tool", "已添加待办：买菜",
            toolCallId: "call_abc", toolName: "addTodo");

        var messages = _repo.GetMessages(session.Id);
        Assert.Single(messages);
        Assert.Equal("tool", messages[0].Role);
        Assert.Equal("call_abc", messages[0].ToolCallId);
        Assert.Equal("addTodo", messages[0].ToolName);
    }

    [Fact]
    public void GetMessages_ReturnsAllMessages_InOrder()
    {
        var session = _repo.CreateSession("路飞", "openai");

        _repo.AddMessage(session.Id, "user", "帮我加个待办");
        _repo.AddMessage(session.Id, "assistant", "", toolCalls: @"[{""id"":""call_1""}]");
        _repo.AddMessage(session.Id, "tool", "已添加", toolCallId: "call_1", toolName: "addTodo");
        _repo.AddMessage(session.Id, "assistant", "好的，已帮你加好啦");

        var messages = _repo.GetMessages(session.Id);
        Assert.Equal(4, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("assistant", messages[1].Role);
        Assert.NotNull(messages[1].ToolCalls);
        Assert.Equal("tool", messages[2].Role);
        Assert.Equal("call_1", messages[2].ToolCallId);
        Assert.Equal("assistant", messages[3].Role);
        Assert.Null(messages[3].ToolCalls);
    }

    [Fact]
    public void DeleteSession_RemovesSessionAndMessages()
    {
        var session = _repo.CreateSession("路飞", "local-klee");
        _repo.AddMessage(session.Id, "user", "测试");
        _repo.AddMessage(session.Id, "assistant", "回复");

        _repo.DeleteSession(session.Id);

        Assert.Empty(_repo.ListSessions());
        Assert.Empty(_repo.GetMessages(session.Id));
    }

    [Fact]
    public void ListSessions_ReturnedByUpdatedAtDesc()
    {
        var s1 = _repo.CreateSession("路飞", "local-klee");
        Thread.Sleep(10);
        var s2 = _repo.CreateSession("索隆", "openai");
        _repo.AddMessage(s2.Id, "user", "更新会话2");

        var sessions = _repo.ListSessions();
        Assert.Equal(2, sessions.Count);
        // 最新更新的在前
        Assert.Equal(s2.Id, sessions[0].Id);
    }

    [Fact]
    public void ClearAll_RemovesAllData()
    {
        var s1 = _repo.CreateSession("路飞", "local-klee");
        _repo.AddMessage(s1.Id, "user", "msg1");
        var s2 = _repo.CreateSession("索隆", "openai");
        _repo.AddMessage(s2.Id, "user", "msg2");

        _repo.ClearAll();

        Assert.Empty(_repo.ListSessions());
    }
}
