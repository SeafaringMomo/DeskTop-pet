"""Agent 循环单元测试（不联网、无框架依赖）。"""

from __future__ import annotations

import pytest

from agent_service.core.agent_loop import AgentLoop
from agent_service.domain.events import AgentEventKind
from tests.conftest import BrokenLLM, FakeLLM, FakeTools

MESSAGES = [{"role": "user", "content": "帮我加个待办"}]


async def collect(loop: AgentLoop, messages=MESSAGES, **kw):
    events = []
    async for ev in loop.run(messages, session_id="s1", **kw):
        events.append(ev)
    return events


async def test_plain_text_reply():
    """无工具调用时应串行产出 session → step → chunk → step_finish → done。"""
    loop = AgentLoop(FakeLLM([{"content": "你好呀"}]), FakeTools(), max_tool_iterations=3)
    events = await collect(loop)

    kinds = [e.kind for e in events]
    assert kinds[0] is AgentEventKind.SESSION
    assert AgentEventKind.CHUNK in kinds
    assert kinds[-1] is AgentEventKind.DONE

    text = "".join(e.data["content"] for e in events if e.kind is AgentEventKind.CHUNK)
    assert text == "你好呀"
    assert events[-1].data["content"] == "你好呀"


async def test_tool_call_then_final_answer():
    """工具调用轮应产出 tool_start/tool_finish，并在回填后继续第二轮。"""
    llm = FakeLLM(
        [
            {"tool_calls": [{"id": "c1", "name": "todo_add", "arguments": '{"content":"买牛奶"}'}]},
            {"content": "已添加"},
        ]
    )
    tools = FakeTools()
    loop = AgentLoop(llm, tools, max_tool_iterations=3)
    events = await collect(loop)

    kinds = [e.kind for e in events]
    assert kinds.count(AgentEventKind.TOOL_START) == 1
    assert kinds.count(AgentEventKind.TOOL_FINISH) == 1
    assert events[-1].data["content"] == "已添加"

    assert tools.invocations == [("todo_add", {"content": "买牛奶"})]

    # 第二轮请求必须携带 assistant(tool_calls) 与 tool 结果
    second_msgs = llm.calls[1]["messages"]
    roles = [m["role"] for m in second_msgs]
    assert "assistant" in roles and "tool" in roles
    tool_msg = next(m for m in second_msgs if m["role"] == "tool")
    assert tool_msg["tool_call_id"] == "c1"


async def test_invalid_tool_arguments_does_not_break_loop():
    """参数 JSON 非法时应回填错误结果并继续，而不是中断。"""
    llm = FakeLLM(
        [
            {"tool_calls": [{"id": "c1", "name": "todo_add", "arguments": "{坏掉的 json"}]},
            {"content": "抱歉，参数有问题"},
        ]
    )
    tools = FakeTools()
    loop = AgentLoop(llm, tools, max_tool_iterations=3)
    events = await collect(loop)

    finishes = [e for e in events if e.kind is AgentEventKind.TOOL_FINISH]
    assert len(finishes) == 1
    assert finishes[0].data["is_error"] is True
    assert tools.invocations == []  # 未真正执行
    assert events[-1].data["content"] == "抱歉，参数有问题"


async def test_max_iterations_forces_final_answer():
    """用尽轮数时应再发一次无工具请求，避免返回空回复。"""
    tool_round = {
        "tool_calls": [{"id": "c1", "name": "todo_add", "arguments": "{}"}]
    }
    llm = FakeLLM([tool_round, tool_round, {"content": "收尾答复"}])
    loop = AgentLoop(llm, FakeTools(), max_tool_iterations=2)
    events = await collect(loop)

    # 最后一次调用必须不带工具
    assert llm.calls[-1]["tools"] is None
    assert events[-1].data["content"] == "收尾答复"


async def test_llm_failure_emits_error_and_done():
    """LLM 抛异常时应产出 error + done，保证 SSE 正常收尾。"""
    loop = AgentLoop(BrokenLLM(), FakeTools(), max_tool_iterations=2)
    events = await collect(loop)

    kinds = [e.kind for e in events]
    assert AgentEventKind.ERROR in kinds
    assert kinds[-1] is AgentEventKind.DONE
    err = next(e for e in events if e.kind is AgentEventKind.ERROR)
    assert "模拟 LLM 故障" in err.data["error"]


async def test_empty_reply_uses_fallback():
    """LLM 返回空文本时给出兜底话术。"""
    loop = AgentLoop(FakeLLM([{"content": ""}]), FakeTools(), max_tool_iterations=2)
    events = await collect(loop)
    assert events[-1].data["content"].startswith("（这次没能")


@pytest.mark.parametrize("max_iter", [1, 8])
async def test_tools_are_passed_every_round(max_iter: int):
    """每一轮都应带上工具 schema（模型可能中途决定调用）。"""
    llm = FakeLLM([{"content": "ok"}])
    loop = AgentLoop(llm, FakeTools(), max_tool_iterations=max_iter)
    await collect(loop)
    assert llm.calls[0]["tools"]
    assert llm.calls[0]["tools"][0]["function"]["name"] == "todo_add"
