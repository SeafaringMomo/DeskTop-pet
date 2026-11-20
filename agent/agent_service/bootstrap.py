"""依赖装配（bootstrap）。

唯一知道「哪个实现接哪个端口」的地方 = 组合根。
测试通过替换本模块的容器即可得到完整的内存实现。
"""

from __future__ import annotations

import logging

from .adapters.file_workspace import FileWorkspace
from .adapters.json_memory import JsonMarkdownMemory
from .adapters.openai_llm import OpenAIChatLLM
from .api.app_factory import AppContext
from .core.agent_loop import AgentLoop
from .core.prompt import PromptBuilder
from .core.settings import AgentSettings
from .services.chat_service import ChatService
from .tools.composite import CompositeToolbox
from .tools.local import LocalToolbox
from .tools.remote import RemoteToolbox

logger = logging.getLogger("gent.agent.bootstrap")


def build_context(settings: AgentSettings) -> AppContext:
    """根据设置装配全部依赖（同步部分）。

    注：远程工具清单的拉取是异步的，见 <see cref="refresh_remote_tools_async"/>。
    """
    missing = settings.validate()
    if missing:
        raise ValueError(f"缺少必需配置：{', '.join(missing)}")

    workspace = FileWorkspace(settings.workspace_path)
    created = workspace.ensure_initialized(settings.role)
    logger.info("工作空间：%s（本次新建 %d 个文档）", workspace.root, len(created))

    memory = JsonMarkdownMemory(workspace.root)
    llm = OpenAIChatLLM(
        model=settings.llm_model,
        api_key=settings.llm_api_key,
        base_url=settings.llm_base_url or None,
        timeout=settings.llm_timeout,
        max_tokens=settings.max_tokens,
    )

    local_tools = LocalToolbox(
        memory=memory,
        workspace_root=workspace.root,
        enable_exec=settings.enable_exec,
        enable_web=settings.enable_web,
        brave_api_key=settings.brave_api_key,
    )

    remote: RemoteToolbox | None = None
    sources: list = [local_tools]
    if settings.tool_bridge_url:
        remote = RemoteToolbox(settings.tool_bridge_url, token=settings.tool_bridge_token)
        sources.append(remote)
    tools = CompositeToolbox(sources)

    loop = AgentLoop(llm, tools, max_tool_iterations=settings.max_tool_iterations)
    chat_service = ChatService(
        loop=loop,
        prompt_builder=PromptBuilder(workspace, memory),
        memory=memory,
        history_limit=settings.history_limit,
        temperature=settings.temperature,
    )

    logger.info(
        "依赖装配完成：model=%s，工具=%d 个，exec=%s，web=%s，远程桥=%s",
        settings.llm_model,
        len(tools.list_specs()),
        settings.enable_exec,
        settings.enable_web,
        bool(settings.tool_bridge_url),
    )

    return AppContext(
        settings=settings,
        chat_service=chat_service,
        memory=memory,
        workspace=workspace,
        tools=tools,
        remote_tools=remote,
    )


async def refresh_remote_tools_async(ctx: AppContext) -> int:
    """拉取远程（C#）工具清单到组合工具集中。

    必须在事件循环内调用（httpx.AsyncClient 需要）。
    失败时降级：本地工具照常可用，仅远程能力缺失。

    Returns:
        远程工具数量
    """
    if ctx.remote_tools is None:
        return 0
    specs = await ctx.remote_tools.refresh()
    ctx.tools.rebuild()
    logger.info("工具清单刷新完成：共 %d 个（远程 %d 个）", len(ctx.tools.list_specs()), len(specs))
    return len(specs)
