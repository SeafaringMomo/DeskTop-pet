"""记忆端口：屏蔽记忆存储实现（Markdown 文件 / 内存 / 将来的向量库）。"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Protocol, runtime_checkable


@dataclass(slots=True)
class MemoryItem:
    """一条记忆。"""

    content: str
    category: str = "fact"
    timestamp: str = ""


@dataclass(slots=True)
class MemorySearchHit:
    """记忆检索结果片段。"""

    source: str
    snippet: str
    score: float = 1.0
    metadata: dict[str, str] = field(default_factory=dict)


@runtime_checkable
class MemoryPort(Protocol):
    """记忆端口。"""

    def capture(self, text: str) -> list[MemoryItem]:
        """从一段文本（通常是用户消息）中识别值得记忆的条目。"""
        ...

    def store(self, items: list[MemoryItem]) -> int:
        """持久化记忆条目，返回实际写入条数（去重后的）。"""
        ...

    def search(self, keyword: str, limit: int = 5) -> list[MemorySearchHit]:
        """按关键词检索记忆。"""
        ...

    def long_term(self) -> str:
        """返回长期记忆全文（用于拼装系统提示）。"""
        ...

    def recent_digest(self, days: int = 2, max_chars: int = 1200) -> str:
        """返回最近若干天的记忆摘要（用于拼装系统提示）。"""
        ...
