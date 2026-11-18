"""FastAPI 应用工厂。

只做三件事：鉴权中间件、依赖容器、路由注册。
业务逻辑全部在 services 层。

端点：
    GET  /health
    POST /v1/chat/stream
    POST /v1/session/reset
    POST /v1/memory/search
"""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from dataclasses import dataclass
from typing import Any

from fastapi import Depends, FastAPI, Header, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from sse_starlette.sse import EventSourceResponse

from ..core.settings import AgentSettings
from ..domain.events import AgentEvent
from ..ports.memory import MemoryPort
from ..ports.workspace import WorkspacePort
from ..services.chat_service import ChatService

logger = logging.getLogger("gent.agent.api")


# ===== 请求/响应模型 =====


class ChatRequest(BaseModel):
    """聊天请求（由 C# 发送）。"""

    message: str = Field(min_length=1)
    session_id: str = "default"
    user: str = ""
    role: str = ""
    level_hint: str = ""
    system_prompt: str = ""
    history: list[dict[str, Any]] = Field(default_factory=list)
    options: dict[str, Any] = Field(default_factory=dict)


class SessionResetRequest(BaseModel):
    session_id: str = "default"


class MemorySearchRequest(BaseModel):
    keyword: str = Field(min_length=1)
    limit: int = 5


@dataclass(slots=True)
class AppContext:
    """依赖容器：测试时可整体替换。"""

    settings: AgentSettings
    chat_service: ChatService
    memory: MemoryPort
    workspace: WorkspacePort
    tools: object | None = None
    """组合工具集（供 /health 与远程工具刷新使用）"""

    remote_tools: object | None = None
    """远程 C# 工具集（未配置时为 None）"""


def create_app(ctx: AppContext) -> FastAPI:
    """构建 FastAPI 应用。

    Args:
        ctx: 依赖容器（由 bootstrap 装配，测试可传假实现）
    """
    @asynccontextmanager
    async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
        """启动时拉取一次远程工具清单（C# 工具桥）。

        在事件循环内执行，失败时降级为仅本地工具。
        """
        from ..bootstrap import refresh_remote_tools_async

        try:
            count = await refresh_remote_tools_async(ctx)
            logger.info("启动完成，远程工具 %d 个", count)
        except Exception as exc:  # noqa: BLE001 - 启动不应因远程工具失败而中断
            logger.warning("拉取远程工具失败，仅使用本地工具：%s", exc)
        yield

    app = FastAPI(
        title="DeskTopGent Agent",
        version="0.1.0",
        docs_url=None,
        redoc_url=None,
        lifespan=lifespan,
    )

    def verify_token(x_agent_token: str | None = Header(default=None)) -> None:
        """简单令牌鉴权：仅允许本机 C# 主进程调用。"""
        if ctx.settings.token and x_agent_token != ctx.settings.token:
            logger.warning("拒绝未授权请求：token 不匹配")
            raise HTTPException(status_code=401, detail="unauthorized")

    # ===== 端点 =====

    @app.get("/health")
    async def health() -> dict[str, Any]:
        return {
            "status": "ok",
            "version": "0.1.0",
            "user": ctx.settings.user,
            "role": ctx.settings.role,
            "tools": ctx.chat_service.tool_count,
            "model": ctx.settings.llm_model,
        }

    @app.post("/v1/chat/stream", dependencies=[Depends(verify_token)])
    async def chat_stream(req: ChatRequest, request: Request) -> EventSourceResponse:
        """SSE 流式对话。

        事件契约见 domain/events.py 的 AgentEventKind。
        """
        logger.info(
            "收到流式请求：session=%s，user=%s，role=%s，消息长度=%d",
            req.session_id,
            req.user or ctx.settings.user,
            req.role or ctx.settings.role,
            len(req.message),
        )

        async def generator() -> AsyncIterator[dict[str, str]]:
            try:
                async for event in ctx.chat_service.chat(
                    message=req.message,
                    history=req.history,
                    session_id=req.session_id,
                    runtime_prompt=req.system_prompt,
                ):
                    if await request.is_disconnected():
                        logger.info("客户端断开，中止生成：session=%s", req.session_id)
                        return
                    yield event.to_sse()
            except Exception as exc:
                logger.exception("流式对话异常")
                yield AgentEvent.error(str(exc), type(exc).__name__).to_sse()
                yield AgentEvent.done("", req.session_id).to_sse()

        return EventSourceResponse(generator())

    @app.post("/v1/session/reset", dependencies=[Depends(verify_token)])
    async def session_reset(req: SessionResetRequest) -> dict[str, bool]:
        """会话重置（当前服务无服务端会话状态，仅记日志）。"""
        logger.info("会话重置：session=%s", req.session_id)
        return {"ok": True}

    @app.post("/v1/memory/search", dependencies=[Depends(verify_token)])
    async def memory_search(req: MemorySearchRequest) -> dict[str, Any]:
        """检索记忆（供 C# 设置页/调试使用）。"""
        hits = ctx.memory.search(req.keyword, req.limit)
        return {
            "items": [
                {"source": h.source, "snippet": h.snippet, "score": h.score} for h in hits
            ]
        }

    @app.exception_handler(Exception)
    async def unhandled(_request: Request, exc: Exception) -> JSONResponse:
        logger.exception("未处理异常")
        return JSONResponse(status_code=500, content={"error": str(exc)})

    return app

