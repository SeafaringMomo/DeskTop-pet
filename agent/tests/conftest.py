"""测试辅助：假 LLM / 假工具 / 临时工作空间。"""

from __future__ import annotations

from collections.abc import AsyncIterator
from typing import Any

from agent_service.ports.llm import ChatStreamResult, StreamDelta
from agent_service.ports.tools import ToolExecutionResult, ToolSpec


class FakeLLM:
    """可编排的假 LLM。

    按 ``script`` 顺序消费：每个元素描述一轮回复。
    - {"content": "..."} → 纯文本回复
    - {"content": "...", "tool_calls": [{"id","name","arguments"}]} → 文本 + 工具调用
    """

    def __init__(self, script: list[dict[str, Any]]) -> None:
        self._script = list(script)
        self._index = 0
        self._last: ChatStreamResult | None = None
        self.calls: list[dict[str, Any]] = []

    @property
    def model(self) -> str:
        return "fake-model"

    async def stream_chat(
        self,
        messages: list[dict[str, Any]],
        tools: list[dict[str, Any]] | None = None,
        **options: Any,
    ) -> AsyncIterator[StreamDelta]:
        self.calls.append({"messages": list(messages), "tools": tools, "options": options})

        step = self._script[self._index] if self._index < len(self._script) else {"content": ""}
        self._index += 1

        result = ChatStreamResult()
        text = step.get("content") or ""
        # 分两段产出，验证增量语义
        if text:
            half = max(1, len(text) // 2)
            for part in (text[:half], text[half:]):
                if part:
                    result.content += part
                    yield StreamDelta(content=part)

        for idx, tc in enumerate(step.get("tool_calls") or []):
            result.tool_calls.append(
                {
                    "id": tc["id"],
                    "name": tc["name"],
                    "arguments": tc.get("arguments", "{}"),
                }
            )
            yield StreamDelta(
                tool_call_index=idx,
                tool_call_id=tc["id"],
                tool_name=tc["name"],
                arguments_delta=tc.get("arguments", "{}"),
            )

        result.finish_reason = "tool_calls" if result.tool_calls else "stop"
        self._last = result

    def last_result(self) -> ChatStreamResult | None:
        return self._last


class FakeTools:
    """内存工具集合，记录调用。"""

    def __init__(
        self,
        names: tuple[str, ...] = ("todo_add",),
        results: dict[str, ToolExecutionResult] | None = None,
    ) -> None:
        self._specs = [ToolSpec(n, f"测试工具 {n}") for n in names]
        self._results = results or {}
        self.invocations: list[tuple[str, dict[str, Any]]] = []

    def list_specs(self) -> list[ToolSpec]:
        return self._specs

    async def invoke(self, name: str, arguments: dict[str, Any]) -> ToolExecutionResult:
        self.invocations.append((name, arguments))
        return self._results.get(
            name, ToolExecutionResult(f"{name} 执行成功")
        )


class BrokenLLM:
    """总是抛异常的 LLM，用于验证错误路径。"""

    model = "broken"

    async def stream_chat(self, messages, tools=None, **options) -> AsyncIterator[StreamDelta]:
        from agent_service.domain.errors import LLMError

        raise LLMError("模拟 LLM 故障")
        yield StreamDelta()  # pragma: no cover - 语法上需要成为生成器

    def last_result(self) -> ChatStreamResult | None:
        return None
