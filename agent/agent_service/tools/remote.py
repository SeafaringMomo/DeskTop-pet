"""远程 C# 工具代理（实现 ToolPort）。

C# 侧的 AgentToolBridge 暴露：
    GET  /tools          → {"tools":[{"name","description","parameters"}]}
    POST /tools/invoke   → {"content","is_error"}

设计为「懒加载 + 失败降级」：C# 桥不可用时本地工具照常工作，
只是待办等远程能力暂时不可见。
"""

from __future__ import annotations

import logging
from collections.abc import Callable
from typing import Any

from ..ports.tools import ToolExecutionResult, ToolSpec

logger = logging.getLogger("gent.agent.tools.remote")


class RemoteToolbox:
    """通过 HTTP 调用 C# 工具桥。"""

    def __init__(
        self,
        base_url: str,
        token: str = "",
        timeout: float = 15.0,
        getter: Callable[[], Any] | None = None,
    ) -> None:
        self._base_url = base_url.rstrip("/")
        self._token = token
        self._timeout = timeout
        self._client: Any = getter() if getter else None
        self._specs: list[ToolSpec] | None = None

    @property
    def available(self) -> bool:
        """是否配置了工具桥地址。"""
        return bool(self._base_url)

    def _get_client(self) -> Any:
        if self._client is None:
            import httpx

            headers = {"X-Agent-Token": self._token} if self._token else {}
            self._client = httpx.AsyncClient(
                base_url=self._base_url,
                headers=headers,
                timeout=self._timeout,
            )
        return self._client

    async def refresh(self) -> list[ToolSpec]:
        """从 C# 拉取工具清单（失败时清空并记录警告）。"""
        if not self.available:
            self._specs = []
            return []

        try:
            resp = await self._get_client().get("/tools")
            resp.raise_for_status()
            raw_tools = resp.json().get("tools", [])
            self._specs = [
                ToolSpec(
                    name=t["name"],
                    description=t.get("description", ""),
                    parameters=t.get("parameters") or {"type": "object", "properties": {}},
                )
                for t in raw_tools
                if t.get("name")
            ]
            logger.info("从 C# 工具桥加载 %d 个工具：%s",
                        len(self._specs), ", ".join(s.name for s in self._specs))
        except Exception as exc:  # noqa: BLE001
            logger.warning("加载 C# 工具失败，远程工具暂不可用：%s", exc)
            self._specs = []
        return self._specs

    def list_specs(self) -> list[ToolSpec]:
        return list(self._specs or [])

    async def invoke(self, name: str, arguments: dict[str, Any]) -> ToolExecutionResult:
        if not self.available:
            return ToolExecutionResult("C# 工具桥未配置", is_error=True)
        try:
            resp = await self._get_client().post(
                "/tools/invoke", json={"name": name, "args": arguments}
            )
            resp.raise_for_status()
            body = resp.json()
            return ToolExecutionResult(
                str(body.get("content", "")), bool(body.get("is_error", False))
            )
        except Exception as exc:  # noqa: BLE001
            logger.warning("调用远程工具 %s 失败：%s", name, exc)
            return ToolExecutionResult(f"远程工具调用失败：{exc}", is_error=True)

    async def aclose(self) -> None:
        if self._client is not None and hasattr(self._client, "aclose"):
            await self._client.aclose()
            self._client = None
