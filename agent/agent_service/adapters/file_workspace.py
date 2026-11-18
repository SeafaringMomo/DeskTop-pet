"""文件工作空间适配器（实现 WorkspacePort）。

人格文档为 Markdown，全部位于 <workspace>/ 下。
模板从包内 workspace/templates/ 复制；角色名以 {role} 占位符注入。
"""

from __future__ import annotations

import logging
from pathlib import Path

from ..domain.errors import WorkspaceError

logger = logging.getLogger("gent.agent.workspace")

# 需要维护的文档清单（不含扩展名）
DOC_NAMES = ("IDENTITY", "SOUL", "USER", "MEMORY", "AGENTS")

# 模板目录（包内）
_TEMPLATE_DIR = Path(__file__).resolve().parent.parent / "workspace" / "templates"


class FileWorkspace:
    """基于文件系统的工作空间。"""

    def __init__(self, root: str) -> None:
        if not root:
            raise WorkspaceError("workspace_path 不能为空")
        self._root = Path(root).expanduser().resolve()

    @property
    def root(self) -> str:
        return str(self._root)

    def ensure_initialized(self, role: str) -> list[str]:
        """创建目录结构与缺失文档。"""
        created: list[str] = []
        try:
            self._root.mkdir(parents=True, exist_ok=True)
            (self._root / "memory").mkdir(exist_ok=True)

            for name in DOC_NAMES:
                target = self._root / f"{name}.md"
                if target.exists():
                    continue
                template = _TEMPLATE_DIR / f"{name}.md"
                if template.exists():
                    content = template.read_text(encoding="utf-8")
                else:
                    content = f"# {name}\n\n（待配置）\n"
                target.write_text(content.replace("{role}", role), encoding="utf-8")
                created.append(name)

            if created:
                logger.info("工作空间初始化完成，新建文档：%s", ", ".join(created))
        except OSError as exc:
            raise WorkspaceError(f"初始化工作空间失败：{exc}") from exc
        return created

    def load_doc(self, name: str) -> str | None:
        path = self._root / f"{name}.md"
        if not path.exists():
            return None
        try:
            return path.read_text(encoding="utf-8")
        except OSError as exc:
            logger.warning("读取文档 %s 失败：%s", name, exc)
            return None

    def save_doc(self, name: str, content: str) -> None:
        self._root.mkdir(parents=True, exist_ok=True)
        try:
            (self._root / f"{name}.md").write_text(content, encoding="utf-8")
        except OSError as exc:
            raise WorkspaceError(f"保存文档 {name} 失败：{exc}") from exc

    def list_docs(self) -> list[str]:
        return [n for n in DOC_NAMES if (self._root / f"{n}.md").exists()]
