"""工具端口：屏蔽「工具来自哪里」。

- 本地 Python 工具（记忆/文件/命令/搜索）
- 远程 C# 工具（待办等，经 HTTP 桥）

对核心循环完全一致。
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Protocol, runtime_checkable


@dataclass(slots=True)
class ToolSpec:
    """工具定义（OpenAI function schema 的中间表示）。"""

    name: str
    description: str
    parameters: dict[str, Any] = field(
        default_factory=lambda: {"type": "object", "properties": {}}
    )

    def to_openai_schema(self) -> dict[str, Any]:
        return {
            "type": "function",
            "function": {
                "name": self.name,
                "description": self.description,
                "parameters": self.parameters,
            },
        }


@dataclass(slots=True)
class ToolExecutionResult:
    """工具执行结果。"""

    content: str
    is_error: bool = False


@runtime_checkable
class ToolPort(Protocol):
    """工具集合端口。"""

    def list_specs(self) -> list[ToolSpec]:
        """列出所有可用工具（用于构造 OpenAI tools 参数）。"""
        ...

    async def invoke(self, name: str, arguments: dict[str, Any]) -> ToolExecutionResult:
        """执行工具。

        实现方必须自行捕获异常并转换为 is_error=True 的结果，
        不得让异常冒泡打断对话循环。
        """
        ...
