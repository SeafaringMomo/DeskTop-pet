"""Agent 主循环（核心层）。

一次请求 = 最多 N 轮「LLM 流式输出 → 执行工具 → 回填结果」。
只依赖 LLMPort / ToolPort，可完全用假实现测试。

与 helper 原实现的关键差异（均为修复）：
1. 不回退到同步入口（会抛「同步入口不能在运行中的事件循环内调用」）
2. 工具参数 JSON 解析失败时回填错误结果而不是中断
3. 达到最大轮数时补一次「无工具」收尾请求，避免返回空回复
4. 历史完全由调用方传入，不维护全局可变状态
"""

from __future__ import annotations

import json
import logging
from collections.abc import AsyncIterator
from typing import Any

from ..domain.events import AgentEvent
from ..ports.llm import LLMPort
from ..ports.tools import ToolPort

logger = logging.getLogger("gent.agent.loop")


class AgentLoop:
    """工具调用循环编排器。"""

    def __init__(
        self,
        llm: LLMPort,
        tools: ToolPort,
        max_tool_iterations: int = 8,
    ) -> None:
        self._llm = llm
        self._tools = tools
        self._max_tool_iterations = max(1, max_tool_iterations)

    @property
    def tool_count(self) -> int:
        """当前可用工具数量（供日志/健康检查使用）。"""
        return len(self._tools.list_specs())

    async def run(
        self,
        messages: list[dict[str, Any]],
        session_id: str,
        **options: Any,
    ) -> AsyncIterator[AgentEvent]:
        """执行一轮完整对话，产出事件流。

        Args:
            messages: 已含 system / history / user 的完整消息列表
            session_id: 会话 ID（原样回传给调用方）
            **options: 透传给 LLMPort.stream_chat

        Yields:
            AgentEvent（session / step_start / chunk / tool_* / step_finish / done / error）
        """
        yield AgentEvent.session(session_id)

        tool_schemas = [spec.to_openai_schema() for spec in self._tools.list_specs()]
        final_text = ""

        try:
            for step in range(1, self._max_tool_iterations + 1):
                yield AgentEvent.step_start(step, self._max_tool_iterations)

                # 1) 流式调用 LLM，实时输出文本增量
                async for delta in self._llm.stream_chat(messages, tool_schemas, **options):
                    if delta.content:
                        yield AgentEvent.chunk(delta.content, step)

                result = self._llm.last_result()
                if result is None:
                    logger.warning("LLM 未返回累积结果，终止循环（step=%d）", step)
                    break

                # 2) 记录本轮文本；有内容时每次都更新（工具轮可能带前言文本）
                if result.content:
                    final_text = result.content

                # 3) 无工具调用 → 结束
                if not result.has_tool_calls():
                    yield AgentEvent.step_finish(step)
                    break

                # 4) 把 assistant(tool_calls) 加入历史
                messages.append(result.to_assistant_message())

                # 5) 依次执行工具并回填
                for call in result.tool_calls:
                    tool_name = call.get("name", "")
                    call_id = call.get("id", "")
                    raw_args = call.get("arguments") or "{}"

                    try:
                        arguments = json.loads(raw_args) if isinstance(raw_args, str) else raw_args
                        if not isinstance(arguments, dict):
                            raise TypeError("参数必须是 JSON 对象")
                    except (json.JSONDecodeError, TypeError, ValueError) as exc:
                        logger.warning("工具 %s 参数解析失败：%s", tool_name, exc)
                        messages.append(
                            {
                                "role": "tool",
                                "tool_call_id": call_id,
                                "content": f"错误：工具参数格式不正确（{exc}）",
                            }
                        )
                        yield AgentEvent.tool_finish(
                            tool_name, f"参数格式不正确：{exc}", is_error=True
                        )
                        continue

                    yield AgentEvent.tool_start(tool_name, arguments)
                    logger.info("调用工具 %s，参数=%s", tool_name, _safe_truncate(raw_args))

                    exec_result = await self._tools.invoke(tool_name, arguments)

                    messages.append(
                        {
                            "role": "tool",
                            "tool_call_id": call_id,
                            "content": exec_result.content,
                        }
                    )
                    yield AgentEvent.tool_finish(
                        tool_name, exec_result.content, is_error=exec_result.is_error
                    )
                    logger.info(
                        "工具 %s 返回（is_error=%s）：%s",
                        tool_name,
                        exec_result.is_error,
                        _safe_truncate(exec_result.content, 200),
                    )

                yield AgentEvent.step_finish(step)
            else:
                # for-else：用尽全部轮数仍未得到最终答复 → 补一次无工具收尾
                logger.warning("达到最大工具轮数 %d，发起无工具收尾请求", self._max_tool_iterations)
                async for delta in self._llm.stream_chat(messages, None, **options):
                    if delta.content:
                        final_text += delta.content
                        yield AgentEvent.chunk(delta.content, self._max_tool_iterations)
                tail = self._llm.last_result()
                if tail and tail.content:
                    final_text = final_text or tail.content

            if not final_text:
                final_text = "（这次没能想清楚该怎么回答，能换个说法再问一次吗？）"
                logger.warning("最终回复为空，使用兜底文本")

            yield AgentEvent.done(final_text, session_id)

        except Exception as exc:
            logger.exception("Agent 循环异常")
            yield AgentEvent.error(str(exc), type(exc).__name__)
            # 出错也要发 done，保证 C# 端能正常关闭流
            yield AgentEvent.done(final_text, session_id)


def _safe_truncate(text: str, limit: int = 500) -> str:
    """截断过长文本用于日志，避免日志爆量。"""
    if len(text) <= limit:
        return text
    return text[:limit] + f"…(共 {len(text)} 字符)"
