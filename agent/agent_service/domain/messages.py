"""消息模型与归一化（领域层）。

职责：
- 把 C# 传来的 history（OpenAI 格式的 dict 列表）清洗为安全的结构
- 校验 role 合法性与字段完整性
- 不依赖 hello-agents，便于单元测试

为什么需要归一化：C# 的 SQLite 里 role 可能出现脏值，或 tool 消息缺少
tool_call_id；直接透传给严格的服务端会被 400 拒绝。
"""

from __future__ import annotations

from collections.abc import Iterable
from typing import Any

VALID_ROLES = frozenset({"user", "assistant", "tool", "system"})

# 单条消息最大字符数，防止异常历史撑爆上下文
MAX_CONTENT_CHARS = 20_000


def _clean_tool_calls(raw: Any) -> list[dict[str, Any]] | None:
    """清洗 assistant 消息的 tool_calls 字段。

    Args:
        raw: 原始值，可能是 list 或已经是 JSON 字符串（C# 侧存的是字符串）

    Returns:
        合法的 tool_calls 列表；无法解析时返回 None（调用方应丢弃该字段）
    """
    import json

    if raw is None:
        return None
    if isinstance(raw, str):
        try:
            raw = json.loads(raw)
        except (json.JSONDecodeError, TypeError):
            return None
    if not isinstance(raw, list) or not raw:
        return None

    cleaned: list[dict[str, Any]] = []
    for item in raw:
        if not isinstance(item, dict):
            continue
        fn = item.get("function")
        if not isinstance(fn, dict):
            continue
        call_id = item.get("id") or ""
        name = fn.get("name") or ""
        if not call_id or not name:
            continue
        cleaned.append(
            {
                "id": str(call_id),
                "type": "function",
                "function": {
                    "name": str(name),
                    "arguments": fn.get("arguments") or "{}",
                },
            }
        )
    return cleaned or None


def normalize_history(raw_history: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    """归一化来自 C# 的历史消息列表。

    规则：
    1. 只保留 role 合法的消息
    2. content 截断到 MAX_CONTENT_CHARS
    3. assistant 的 tool_calls 若非法则丢弃该字段（保留 content）
    4. tool 消息缺少 tool_call_id 时丢弃（否则严格服务端会拒绝整条请求）
    5. 工具调用必须成对：assistant.tool_calls 的每个 id 都要有紧随其后的 tool 消息，
       tool 消息的 tool_call_id 也必须能对应到前一条 assistant 消息（见 _repair_tool_pairs）

    Args:
        raw_history: OpenAI 格式的消息字典列表

    Returns:
        清洗后的消息列表（新对象，不修改入参）
    """
    result: list[dict[str, Any]] = []
    for msg in raw_history or []:
        if not isinstance(msg, dict):
            continue
        role = (msg.get("role") or "").strip()
        if role not in VALID_ROLES:
            continue

        content = msg.get("content")
        content = "" if content is None else str(content)
        if len(content) > MAX_CONTENT_CHARS:
            content = content[:MAX_CONTENT_CHARS] + "…"

        item: dict[str, Any] = {"role": role, "content": content}

        if role == "assistant":
            calls = _clean_tool_calls(msg.get("tool_calls"))
            if calls:
                item["tool_calls"] = calls

        if role == "tool":
            call_id = msg.get("tool_call_id")
            if not call_id:
                # 无 tool_call_id 的 tool 消息会被严格服务端拒绝，直接丢弃
                continue
            item["tool_call_id"] = str(call_id)

        result.append(item)
    return _repair_tool_pairs(result)


def _repair_tool_pairs(messages: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """修复工具调用与工具结果的不配对。

    严格 OpenAI 兼容服务端要求：
    - assistant.tool_calls 里的每个 id 必须有对应的 tool 消息，否则 400；
    - tool 消息必须紧跟在发起调用的 assistant 消息之后。

    历史可能因为「轮数截断」或数据库里只有文本而出现不配对，处理方式：
    - tool 消息找不到所属 assistant（孤立 tool）→ 丢弃；
    - assistant 的工具调用结果不全（流中断、部分回填）→ 整组丢弃，
      因为此时 tool 结果无法正常附着；若该 assistant 有文本则降级为纯文本消息。
    """
    repaired: list[dict[str, Any]] = []
    pending: set[str] = set()      # 当前 assistant 发起、尚未收到结果的调用 id
    group: list[dict[str, Any]] = []  # 当前 assistant 及其已收到的 tool 消息

    def flush() -> None:
        """结算当前工具调用组：结果齐全才提交，否则降级/丢弃。"""
        nonlocal pending, group
        if group:
            assistant = group[0]
            if not pending:
                repaired.extend(group)
            else:
                # 结果不全：丢弃 tool 消息，assistant 仅在有文本时保留
                assistant.pop("tool_calls", None)
                if assistant.get("content"):
                    repaired.append(assistant)
        pending = set()
        group = []

    for msg in messages:
        role = msg["role"]

        if role == "tool":
            call_id = msg.get("tool_call_id", "")
            if not group or call_id not in pending:
                # 孤立 tool 消息（截断导致），丢弃
                continue
            pending.discard(call_id)
            group.append(msg)
            continue

        flush()
        if role == "assistant" and msg.get("tool_calls"):
            # 先挂起，等它的 tool 结果收齐再一起提交（见 flush）
            pending = {c["id"] for c in msg["tool_calls"]}
            group = [msg]
            continue
        repaired.append(msg)

    flush()
    return repaired


def trim_history(
    history: list[dict[str, Any]],
    limit: int,
) -> list[dict[str, Any]]:
    """按条数截断历史，且不切断工具调用配对。

    直接 ``history[-limit:]`` 可能以 tool 消息开头，使下游服务端收到孤立 tool；
    这里在截断后前移到最近的非 tool 消息，保证窗口从「完整的一轮」开始。

    Args:
        history: 已归一化的历史
        limit: 保留的最大条数（<=0 表示不截断）

    Returns:
        截断后的历史
    """
    if limit <= 0 or len(history) <= limit:
        return history
    start = len(history) - limit
    while start < len(history) and history[start].get("role") == "tool":
        start += 1
    return history[start:]


def to_openai_messages(
    system_prompt: str | None,
    history: Iterable[dict[str, Any]],
    user_message: str,
) -> list[dict[str, Any]]:
    """组装最终发给 LLM 的消息列表。

    Args:
        system_prompt: 系统提示（可为空）
        history: 已归一化的历史消息
        user_message: 本轮用户输入

    Returns:
        [system?] + history + user
    """
    messages: list[dict[str, Any]] = []
    if system_prompt:
        messages.append({"role": "system", "content": system_prompt})
    messages.extend(history)
    messages.append({"role": "user", "content": user_message})
    return messages
