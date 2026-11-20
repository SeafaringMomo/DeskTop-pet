"""组合工具集：把多个 ToolPort 合并为一个。

命名冲突策略：先注册者优先（本地工具优先于远程），冲突时记警告。
这是「微服务组合」在工具维度上的体现：核心循环不知道工具来自哪个服务。
"""

from __future__ import annotations

import logging
from typing import Any

from ..ports.tools import ToolExecutionResult, ToolPort, ToolSpec

logger = logging.getLogger("gent.agent.tools.composite")


class CompositeToolbox:
    """多来源工具聚合。"""

    def __init__(self, sources: list[ToolPort]) -> None:
        self._sources = [s for s in sources if s is not None]
        self._owner: dict[str, ToolPort] = {}
        self._build_index()

    def _build_index(self) -> None:
        """（重）建工具名 → 来源映射。"""
        self._owner = {}
        for source in self._sources:
            for spec in source.list_specs():
                if spec.name in self._owner:
                    logger.warning("工具名冲突，忽略后者：%s", spec.name)
                    continue
                self._owner[spec.name] = source

        if self._owner:
            logger.info("工具就绪（%d 个）：%s", len(self._owner), ", ".join(self._owner))

    def rebuild(self) -> None:
        """工具来源的清单发生运行时变化后重建索引（如远程工具加载完成）。"""
        self._build_index()

    def list_specs(self) -> list[ToolSpec]:
        specs: list[ToolSpec] = []
        for source in self._sources:
            specs.extend(s for s in source.list_specs() if self._owner.get(s.name) is source)
        return specs

    async def invoke(self, name: str, arguments: dict[str, Any]) -> ToolExecutionResult:
        source = self._owner.get(name)
        if source is None:
            return ToolExecutionResult(f"未注册的工具：{name}", is_error=True)
        return await source.invoke(name, arguments)
