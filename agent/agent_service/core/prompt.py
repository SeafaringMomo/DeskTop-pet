"""系统提示组装（核心层，纯函数式，易测试）。

优先级（高 → 低），后者不覆盖前者，只追加：
    1. C# 传入的 system_prompt（运行时身份 + 好感度等级提示）
    2. workspace/IDENTITY.md（长期人格）
    3. workspace/USER.md（用户画像）
    4. workspace/SOUL.md（语气模板）
    5. 长期记忆 MEMORY.md
    6. 最近若干天的记忆摘要

设计取舍：不把模板内容硬编码在代码里（AGENTS.md §12），全部来自工作空间文件。
"""

from __future__ import annotations

from ..ports.memory import MemoryPort
from ..ports.workspace import WorkspacePort

# 各文档注入时的标题前缀
_DOC_TITLES = {
    "IDENTITY": "你的身份信息",
    "USER": "用户信息",
    "SOUL": "人格模板",
}


class PromptBuilder:
    """构造系统提示。"""

    def __init__(self, workspace: WorkspacePort, memory: MemoryPort) -> None:
        self._workspace = workspace
        self._memory = memory

    def build(self, runtime_prompt: str | None = None) -> str:
        """组装最终系统提示。

        Args:
            runtime_prompt: C# 传入的运行时提示（可为空）

        Returns:
            拼接后的系统提示；若全部为空返回空串
        """
        parts: list[str] = []

        if runtime_prompt and runtime_prompt.strip():
            parts.append(runtime_prompt.strip())

        for doc_name, title in _DOC_TITLES.items():
            content = self._workspace.load_doc(doc_name)
            if content and content.strip():
                parts.append(f"## {title}\n\n{content.strip()}")

        long_term = self._memory.long_term()
        if long_term and long_term.strip():
            parts.append(f"## 长期记忆\n\n{long_term.strip()}")

        digest = self._memory.recent_digest()
        if digest and digest.strip():
            parts.append(f"## 最近记忆摘要\n\n{digest.strip()}")

        return "\n\n".join(parts)
