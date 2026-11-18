"""Agent 事件模型（领域层）。

这些事件是 Python → C# 的唯一 UI 通信契约，与 SSE 事件名一一对应。
枚举值即 SSE 的 ``event:`` 字段，禁止随意改动（C# 侧依赖）。
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Any


class AgentEventKind(str, Enum):
    """事件类型。值直接作为 SSE 的 event 名。"""

    SESSION = "session"
    STEP_START = "step_start"
    CHUNK = "chunk"
    TOOL_START = "tool_start"
    TOOL_FINISH = "tool_finish"
    STEP_FINISH = "step_finish"
    DONE = "done"
    ERROR = "error"


@dataclass(slots=True)
class AgentEvent:
    """一条流式事件。

    Attributes:
        kind: 事件类型
        data: 事件负载（会被 json.dumps 后作为 SSE data）
    """

    kind: AgentEventKind
    data: dict[str, Any] = field(default_factory=dict)

    # ===== 构造快捷方法（避免调用方拼裸 dict） =====

    @classmethod
    def session(cls, session_id: str) -> AgentEvent:
        return cls(AgentEventKind.SESSION, {"session_id": session_id})

    @classmethod
    def step_start(cls, step: int, max_steps: int) -> AgentEvent:
        return cls(AgentEventKind.STEP_START, {"step": step, "max_steps": max_steps})

    @classmethod
    def chunk(cls, content: str, step: int = 1) -> AgentEvent:
        return cls(AgentEventKind.CHUNK, {"content": content, "step": step})

    @classmethod
    def tool_start(cls, tool: str, args: dict[str, Any] | None = None) -> AgentEvent:
        return cls(AgentEventKind.TOOL_START, {"tool": tool, "args": args or {}})

    @classmethod
    def tool_finish(
        cls, tool: str, result: str, is_error: bool = False
    ) -> AgentEvent:
        return cls(
            AgentEventKind.TOOL_FINISH,
            {"tool": tool, "result": result, "is_error": is_error},
        )

    @classmethod
    def step_finish(cls, step: int) -> AgentEvent:
        return cls(AgentEventKind.STEP_FINISH, {"step": step})

    @classmethod
    def done(cls, content: str, session_id: str) -> AgentEvent:
        return cls(AgentEventKind.DONE, {"content": content, "session_id": session_id})

    @classmethod
    def error(cls, message: str, error_type: str = "AgentError") -> AgentEvent:
        return cls(AgentEventKind.ERROR, {"error": message, "type": error_type})

    def to_sse(self) -> dict[str, str]:
        """转换为 sse-starlette 需要的 {"event":..., "data":...} 结构。"""
        import json

        return {
            "event": self.kind.value,
            "data": json.dumps(self.data, ensure_ascii=False),
        }
