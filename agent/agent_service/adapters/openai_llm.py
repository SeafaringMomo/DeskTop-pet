"""OpenAI 兼容 LLM 适配器（实现 LLMPort）。

为什么重写而不用 helper 的 EnhancedHelloAgentsLLM：
1. helper 通过私有属性继承 hello-agents 的 LLM，版本升级易碎
2. 其类型判定 isinstance(llm, EnhancedHelloAgentsLLM) 与回退分支在 1.0.1 下会抛
   RuntimeError（事件循环内调用同步入口）
3. 本实现只依赖官方 openai SDK 的稳定公开 API，且完全可 mock

对应方案文档 §6.4 的第 1、2 项修复。
"""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from typing import Any

from ..domain.errors import LLMError
from ..ports.llm import ChatStreamResult, StreamDelta

logger = logging.getLogger("gent.agent.llm")


class OpenAIChatLLM:
    """OpenAI 兼容 Chat Completions 的流式实现。"""

    def __init__(
        self,
        model: str,
        api_key: str,
        base_url: str | None = None,
        timeout: float = 60.0,
        max_tokens: int | None = None,
    ) -> None:
        self._model = model
        self._api_key = api_key
        self._base_url = base_url or None
        self._timeout = timeout
        self._max_tokens = max_tokens
        self._last: ChatStreamResult | None = None
        self._client: Any = None

    @property
    def model(self) -> str:
        return self._model

    def _get_client(self) -> Any:
        """懒加载 AsyncOpenAI 客户端（便于测试时替换）。"""
        if self._client is None:
            try:
                from openai import AsyncOpenAI
            except ImportError as exc:  # pragma: no cover - 依赖缺失时明确报错
                raise LLMError("缺少 openai 依赖，请执行 uv sync") from exc
            self._client = AsyncOpenAI(
                api_key=self._api_key,
                base_url=self._base_url,
                timeout=self._timeout,
            )
        return self._client

    async def stream_chat(
        self,
        messages: list[dict[str, Any]],
        tools: list[dict[str, Any]] | None = None,
        **options: Any,
    ) -> AsyncIterator[StreamDelta]:
        """流式对话，实时产出文本与工具调用增量。

        Raises:
            LLMError: 鉴权失败、网络错误、响应异常
        """
        client = self._get_client()
        self._last = None
        result = ChatStreamResult()

        request: dict[str, Any] = {
            "model": self._model,
            "messages": messages,
            "stream": True,
        }
        if tools:
            request["tools"] = tools
            request["tool_choice"] = options.pop("tool_choice", "auto")
        if self._max_tokens:
            request["max_tokens"] = self._max_tokens
        temperature = options.pop("temperature", None)
        if temperature is not None:
            request["temperature"] = temperature
        # 其余参数低频，按需透传（如 frequency_penalty）
        request.update({k: v for k, v in options.items() if v is not None})

        tool_buffer: dict[int, dict[str, Any]] = {}

        try:
            stream = await client.chat.completions.create(**request)
            async for chunk in stream:
                choices = getattr(chunk, "choices", None)
                if not choices:
                    continue
                choice = choices[0]
                delta = getattr(choice, "delta", None)
                if delta is None:
                    continue

                content = getattr(delta, "content", None)
                if content:
                    result.content += content
                    yield StreamDelta(content=content)

                for tc in getattr(delta, "tool_calls", None) or []:
                    idx = getattr(tc, "index", 0) or 0
                    slot = tool_buffer.setdefault(
                        idx, {"id": "", "name": "", "arguments": ""}
                    )
                    tc_id = getattr(tc, "id", None)
                    if tc_id:
                        slot["id"] = tc_id
                    fn = getattr(tc, "function", None)
                    if fn is not None:
                        if getattr(fn, "name", None):
                            slot["name"] = fn.name
                        if getattr(fn, "arguments", None):
                            slot["arguments"] += fn.arguments

                    yield StreamDelta(
                        tool_call_index=idx,
                        tool_call_id=tc_id,
                        tool_name=getattr(fn, "name", None) if fn else None,
                        arguments_delta=getattr(fn, "arguments", None) if fn else None,
                    )

                finish_reason = getattr(choice, "finish_reason", None)
                if finish_reason:
                    result.finish_reason = finish_reason

        except Exception as exc:
            logger.error("LLM 调用失败：%s", exc)
            raise LLMError(f"LLM 调用失败：{exc}") from exc

        # 只保留参数齐全的工具调用
        result.tool_calls = [
            tc
            for _, tc in sorted(tool_buffer.items())
            if tc["id"] and tc["name"]
        ]
        self._last = result

    def last_result(self) -> ChatStreamResult | None:
        return self._last
