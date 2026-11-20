"""工作空间端口：人格文件（IDENTITY/SOUL/USER/AGENTS）读写。

独立于记忆端口：人格文件是低频、整份读写的；记忆是高频、增量追加的。
"""

from __future__ import annotations

from typing import Protocol, runtime_checkable


@runtime_checkable
class WorkspacePort(Protocol):
    """工作空间端口。"""

    @property
    def root(self) -> str:
        """工作空间根目录的绝对路径。"""
        ...

    def ensure_initialized(self, role: str) -> list[str]:
        """确保目录与模板文件存在。

        Args:
            role: 当前角色显示名（用于初始化 IDENTITY.md）

        Returns:
            本次新建的文件名列表（空表示已存在）
        """
        ...

    def load_doc(self, name: str) -> str | None:
        """按名读取 Markdown 文档（不含扩展名），不存在返回 None。"""
        ...

    def save_doc(self, name: str, content: str) -> None:
        """保存 Markdown 文档。"""
        ...

    def list_docs(self) -> list[str]:
        """列出已存在的文档名。"""
        ...
