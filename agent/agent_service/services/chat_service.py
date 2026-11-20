"""聊天服务：对外用例入口。

职责（按顺序）：
1. 归一化 C# 传入的历史
2. 组装系统提示（运行时提示 + 人格文档 + 记忆）
3. 交给 AgentLoop 执行
4. 对话结束后做记忆捕获（失败不影响主流程）

不依赖 FastAPI：可被 CLI / 测试直接调用。
"""

from __future__ import annotations

import logging

from ..core.agent_loop import AgentLoop
from ..core.prompt import PromptBuilder
from ..domain.messages import normalize_history
from ..ports.memory import MemoryPort

logger = logging.getLogger("gent.agent.chat")


class ChatService:
    """一次对话的完整用例。"""

    def __init__(
        self,
        loop: AgentLoop,
        prompt_builder: PromptBuilder,
        memory: MemoryPort,
        history_limit: int = 40,
        temperature: float = 0.7,
    ) -> None:
        self._loop = loop
        self._prompt = prompt_builder
        self._memory = memory
        self._history_limit = max(0, history_limit)
        self._temperature = temperature

    @property
    def tool_count(self) -> int:
        """当前可用工具数量（供 /health 与日志使用）。"""
        return self._loop.tool_count

    async def chat(
        self,
        message: str,
        history: list[dict],
        session_id: str,
        runtime_prompt: str | None = None,
    ):
        """执行一次对话，产出事件流。

        Args:
            message: 本轮用户输入
            history: C# 传来的历史（OpenAI 格式）
            session_id: 会话 ID
            runtime_prompt: C# 运行时身份提示

        Yields:
            AgentEvent
        """
        from ..domain.messages import to_openai_messages, trim_history

        clean_history = normalize_history(history or [])
        clean_history = trim_history(clean_history, self._history_limit)

        system_prompt = self._prompt.build(runtime_prompt)
        messages = to_openai_messages(system_prompt, clean_history, message)

        logger.info(
            "对话开始：session=%s，历史=%d 条，系统提示=%d 字符，工具=%d 个",
            session_id,
            len(clean_history),
            len(system_prompt),
            self._loop.tool_count,
        )

        async for event in self._loop.run(
            messages, session_id, temperature=self._temperature
        ):
            yield event

        # 主流程结束后捕获记忆（用户消息里可能含偏好/事实）
        self._capture_memory(message)

    def _capture_memory(self, message: str) -> None:
        """捕获并存储记忆。异常只记录不抛出。"""
        try:
            items = self._memory.capture(message)
            if items:
                written = self._memory.store(items)
                # 不打印记忆正文（隐私），只记条数与分类
                logger.info(
                    "记忆捕获：识别 %d 条，写入 %d 条，分类=%s",
                    len(items),
                    written,
                    ",".join(sorted({i.category for i in items})),
                )
        except Exception as exc:  # noqa: BLE001
            logger.warning("记忆捕获失败：%s", exc)
