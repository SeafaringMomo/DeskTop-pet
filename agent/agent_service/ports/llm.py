"""LLM 端口：定义流式对话 + 工具调用所需的抽象。

核心循环只认识这个协议，不关心背后是 OpenAI SDK、hello-agents 还是假实现。
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from dataclasses import dataclass, field
from typing import Any, Protocol, runtime_checkable


@dataclass(slots=True)
class StreamDelta:
    """流式增量：要么是文本，要么是工具调用增量。

    Attributes:
        content: 文本增量（None 表示本增量不含文本）
        tool_call_index: 工具调用序号（OpenAI 协议里同一次回复可并行多个工具）
        tool_call_id: 工具调用 ID（通常只在首个增量里出现）
        tool_name: 工具名（通常只在首个增量里出现）
        arguments_delta: 参数 JSON 的片段
    """

    content: str | None = None
    tool_call_index: int | None = None
    tool_call_id: str | None = None
    tool_name: str | None = None
    arguments_delta: str | None = None


@dataclass(slots=True)
class ChatStreamResult:
    """一轮流式调用结束后的累积结果。"""

    content: str = ""
    """累积的文本内容"""

    tool_calls: list[dict[str, Any]] = field(default_factory=list)
    """累积的完整工具调用；每项 {"id","name","arguments"}"""

    finish_reason: str | None = None
    """结束原因：stop / tool_calls / length …"""

    def has_tool_calls(self) -> bool:
        return bool(self.tool_calls)

    def to_assistant_message(self) -> dict[str, Any]:
        """转换为可追加到 messages 的 assistant 消息（OpenAI 协议）。"""
        msg: dict[str, Any] = {
            "role": "assistant",
            "content": self.content or None,
        }
        if self.tool_calls:
            msg["tool_calls"] = [
                {
                    "id": tc["id"],
                    "type": "function",
                    "function": {
                        "name": tc["name"],
                        "arguments": tc["arguments"],
                    },
                }
                for tc in self.tool_calls
            ]
        return msg


@runtime_checkable
class LLMPort(Protocol):
    """流式对话端口。

    实现方需保证：
    - 每个文本增量都通过 StreamDelta.content 产出（不得缓冲到结束才一次性给出）
    - 工具调用参数按增量拼接，结束时在 ChatStreamResult.tool_calls 中给出完整参数
    - 失败时抛 domain.errors.LLMError
    """

    @property
    def model(self) -> str:
        """模型标识（仅用于日志与 UI 展示）。"""
        ...

    def stream_chat(
        self,
        messages: list[dict[str, Any]],
        tools: list[dict[str, Any]] | None = None,
        **options: Any,
    ) -> AsyncIterator[StreamDelta]:
        """流式发起一次对话。

        Args:
            messages: OpenAI 格式消息列表
            tools: OpenAI 格式工具 schema 列表（为空表示不启用工具）
            **options: temperature / max_tokens 等

        Yields:
            StreamDelta
        """
        ...

    def last_result(self) -> ChatStreamResult | None:
        """获取上一次 stream_chat 消费完成后的累积结果。

        设计为显式方法而非返回值，是为了让 stream_chat 保持纯流式语义
        （调用方可能在中途取消，此时不应有结果）。
        """
        ...
