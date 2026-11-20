"""消息归一化测试：脏历史不得导致请求被服务端拒绝。"""

from __future__ import annotations

from agent_service.domain.messages import normalize_history, to_openai_messages


def test_drops_unknown_roles():
    out = normalize_history(
        [
            {"role": "user", "content": "hi"},
            {"role": "hacker", "content": "x"},
            {"role": "", "content": "y"},
            "not-a-dict",
        ]
    )
    assert out == [{"role": "user", "content": "hi"}]


def test_tool_message_without_call_id_is_dropped():
    out = normalize_history(
        [{"role": "tool", "content": "结果", "tool_call_id": None}]
    )
    assert out == []


def test_tool_message_keeps_call_id():
    """配对的 tool 消息保留 tool_call_id；孤立 tool 消息会被丢弃（见下方配对测试）。"""
    out = normalize_history(
        [
            _assistant_with_calls("c1"),
            {"role": "tool", "content": "结果", "tool_call_id": "c1"},
        ]
    )
    assert out[1]["role"] == "tool"
    assert out[1]["tool_call_id"] == "c1"


def test_assistant_tool_calls_from_json_string():
    """C# 里 tool_calls 存的是 JSON 字符串，需要能解析。"""
    raw = '[{"id":"c1","type":"function","function":{"name":"todo_add","arguments":"{}"}}]'
    out = normalize_history(
        [
            {"role": "assistant", "content": "", "tool_calls": raw},
            {"role": "tool", "content": "已添加", "tool_call_id": "c1"},
        ]
    )
    assert out[0]["tool_calls"][0]["function"]["name"] == "todo_add"


def test_assistant_broken_tool_calls_dropped_but_content_kept():
    out = normalize_history(
        [{"role": "assistant", "content": "好的", "tool_calls": "{不是数组"}]
    )
    assert out[0]["content"] == "好的"
    assert "tool_calls" not in out[0]


def test_tool_calls_without_name_or_id_are_filtered():
    raw = [
        {"id": "c1", "function": {"name": "", "arguments": "{}"}},
        {"id": "", "function": {"name": "x", "arguments": "{}"}},
    ]
    out = normalize_history([{"role": "assistant", "content": "", "tool_calls": raw}])
    assert "tool_calls" not in out[0]


def test_content_truncated():
    from agent_service.domain.messages import MAX_CONTENT_CHARS

    out = normalize_history([{"role": "user", "content": "a" * (MAX_CONTENT_CHARS + 100)}])
    assert len(out[0]["content"]) == MAX_CONTENT_CHARS + 1


def test_none_content_becomes_empty_string():
    out = normalize_history([{"role": "assistant", "content": None}])
    assert out[0]["content"] == ""


def test_to_openai_messages_order():
    msgs = to_openai_messages("系统", [{"role": "user", "content": "旧"}], "新")
    assert [m["role"] for m in msgs] == ["system", "user", "user"]
    assert msgs[-1]["content"] == "新"


def test_to_openai_messages_without_system():
    msgs = to_openai_messages(None, [], "新")
    assert len(msgs) == 1


# ===== 工具调用配对修复 =====


def _assistant_with_calls(*call_ids: str, content: str = "") -> dict:
    return {
        "role": "assistant",
        "content": content,
        "tool_calls": [
            {"id": cid, "type": "function", "function": {"name": "todo_add", "arguments": "{}"}}
            for cid in call_ids
        ],
    }


def test_orphan_tool_message_is_dropped():
    """历史被截断后开头的 tool 消息没有所属 assistant，必须丢弃。"""
    out = normalize_history(
        [
            {"role": "tool", "content": "结果", "tool_call_id": "c1"},
            {"role": "user", "content": "你好"},
        ]
    )
    assert [m["role"] for m in out] == ["user"]


def test_tool_call_without_result_is_downgraded_to_text():
    """assistant 请求了工具但历史里没有对应结果 → 去掉 tool_calls，避免 400。"""
    out = normalize_history(
        [_assistant_with_calls("c1", content="我查一下"), {"role": "user", "content": "?"}]
    )
    assert out[0]["content"] == "我查一下"
    assert "tool_calls" not in out[0]


def test_empty_assistant_with_dangling_calls_is_dropped():
    out = normalize_history([_assistant_with_calls("c1")])
    assert out == []


def test_partially_answered_tool_calls_are_downgraded():
    """两个调用只回了一个结果：要么成对，要么整条降级，不能留半截。"""
    out = normalize_history(
        [
            _assistant_with_calls("c1", "c2"),
            {"role": "tool", "content": "r1", "tool_call_id": "c1"},
        ]
    )
    # c1 的结果因所属 assistant 被降级而成为孤立 tool，一并丢弃
    assert out == []


def test_complete_tool_pair_is_preserved():
    out = normalize_history(
        [
            _assistant_with_calls("c1"),
            {"role": "tool", "content": "r1", "tool_call_id": "c1"},
            {"role": "assistant", "content": "完成"},
        ]
    )
    assert out[0]["tool_calls"][0]["id"] == "c1"
    assert out[1]["tool_call_id"] == "c1"
    assert out[2]["content"] == "完成"


# ===== 截断不得切断工具配对 =====


def test_trim_history_skips_leading_tool_messages():
    from agent_service.domain.messages import trim_history

    history = [
        {"role": "user", "content": "1"},
        {"role": "assistant", "content": "a"},
        {"role": "user", "content": "2"},
        {"role": "assistant", "content": "b"},
        {"role": "tool", "content": "t", "tool_call_id": "c1"},
        {"role": "assistant", "content": "c"},
    ]
    out = trim_history(history, 3)
    assert out[0]["role"] != "tool"
    assert out[-1]["content"] == "c"


def test_trim_history_noop_when_within_limit():
    from agent_service.domain.messages import trim_history

    history = [{"role": "user", "content": "1"}]
    assert trim_history(history, 10) is history
