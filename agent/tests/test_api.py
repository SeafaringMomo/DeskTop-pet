"""API 层测试：鉴权、SSE 事件契约、健康检查。

使用 FastAPI TestClient（无需真实网络端口）。
"""

from __future__ import annotations

import json
from pathlib import Path

from fastapi.testclient import TestClient

from agent_service.adapters.file_workspace import FileWorkspace
from agent_service.adapters.json_memory import JsonMarkdownMemory
from agent_service.api.app_factory import AppContext, create_app
from agent_service.core.agent_loop import AgentLoop
from agent_service.core.prompt import PromptBuilder
from agent_service.core.settings import AgentSettings
from agent_service.services.chat_service import ChatService
from tests.conftest import BrokenLLM, FakeLLM, FakeTools

TOKEN = "test-token"


def make_client(script, tmp_path: Path, token: str = TOKEN) -> TestClient:
    workspace = FileWorkspace(str(tmp_path / "ws"))
    workspace.ensure_initialized("测试角色")
    memory = JsonMarkdownMemory(workspace.root)
    settings = AgentSettings(
        token=token,
        user="tester",
        role="测试角色",
        workspace_path=workspace.root,
        llm_model="fake",
    )
    loop = AgentLoop(FakeLLM(script), FakeTools(), max_tool_iterations=3)
    service = ChatService(loop, PromptBuilder(workspace, memory), memory)
    ctx = AppContext(settings=settings, chat_service=service, memory=memory, workspace=workspace)
    return TestClient(create_app(ctx))


def parse_sse(text: str) -> list[tuple[str, dict]]:
    """把 SSE 响应体解析为 (event, data) 列表。

    兼容 sse-starlette 输出的 CRLF 行尾。
    """
    events: list[tuple[str, dict]] = []
    normalized = text.replace("\r\n", "\n")
    for block in normalized.strip().split("\n\n"):
        name, payload = None, None
        for line in block.splitlines():
            if line.startswith("event:"):
                name = line[len("event:"):].strip()
            elif line.startswith("data:"):
                payload = line[len("data:"):].strip()
        if name and payload:
            events.append((name, json.loads(payload)))
    return events


def test_health(tmp_path: Path):
    client = make_client([{"content": "hi"}], tmp_path)
    resp = client.get("/health")
    assert resp.status_code == 200
    body = resp.json()
    assert body["status"] == "ok"
    assert body["user"] == "tester"
    assert body["tools"] == 1


def test_stream_requires_token(tmp_path: Path):
    client = make_client([{"content": "hi"}], tmp_path)
    resp = client.post("/v1/chat/stream", json={"message": "你好"})
    assert resp.status_code == 401


def test_stream_events_contract(tmp_path: Path):
    client = make_client([{"content": "你好呀"}], tmp_path)
    resp = client.post(
        "/v1/chat/stream",
        json={"message": "你好", "session_id": "s1"},
        headers={"X-Agent-Token": TOKEN},
    )
    assert resp.status_code == 200

    events = parse_sse(resp.text)
    names = [n for n, _ in events]
    assert names[0] == "session"
    assert "chunk" in names
    assert names[-1] == "done"

    done = dict(events)["done"]
    assert done["content"] == "你好呀"
    assert done["session_id"] == "s1"

    chunks = "".join(d["content"] for n, d in events if n == "chunk")
    assert chunks == "你好呀"


def test_stream_tool_events(tmp_path: Path):
    script = [
        {"tool_calls": [{"id": "c1", "name": "todo_add", "arguments": '{"content":"写周报"}'}]},
        {"content": "好啦"},
    ]
    client = make_client(script, tmp_path)
    resp = client.post(
        "/v1/chat/stream",
        json={"message": "加个待办"},
        headers={"X-Agent-Token": TOKEN},
    )
    events = parse_sse(resp.text)
    names = [n for n, _ in events]
    assert "tool_start" in names and "tool_finish" in names

    start = next(d for n, d in events if n == "tool_start")
    assert start["tool"] == "todo_add"
    assert start["args"] == {"content": "写周报"}


def test_stream_error_path_still_returns_done(tmp_path: Path):
    workspace = FileWorkspace(str(tmp_path / "ws"))
    workspace.ensure_initialized("A")
    memory = JsonMarkdownMemory(workspace.root)
    settings = AgentSettings(token=TOKEN, workspace_path=workspace.root, llm_model="x")
    loop = AgentLoop(BrokenLLM(), FakeTools(), max_tool_iterations=2)
    service = ChatService(loop, PromptBuilder(workspace, memory), memory)
    ctx = AppContext(settings=settings, chat_service=service, memory=memory, workspace=workspace)
    client = TestClient(create_app(ctx))

    resp = client.post(
        "/v1/chat/stream",
        json={"message": "hi"},
        headers={"X-Agent-Token": TOKEN},
    )
    names = [n for n, _ in parse_sse(resp.text)]
    assert "error" in names
    assert names[-1] == "done"


def test_memory_search_endpoint(tmp_path: Path):
    client = make_client([{"content": "hi"}], tmp_path)
    # 先发一条含偏好关键词的消息，触发记忆捕获
    chat_resp = client.post(
        "/v1/chat/stream",
        json={"message": "记住我喜欢简洁的回复"},
        headers={"X-Agent-Token": TOKEN},
    )
    assert chat_resp.status_code == 200

    resp = client.post(
        "/v1/memory/search",
        json={"keyword": "简洁"},
        headers={"X-Agent-Token": TOKEN},
    )
    assert resp.status_code == 200
    assert resp.json()["items"]


def test_session_reset(tmp_path: Path):
    client = make_client([{"content": "hi"}], tmp_path)
    resp = client.post(
        "/v1/session/reset",
        json={"session_id": "s1"},
        headers={"X-Agent-Token": TOKEN},
    )
    assert resp.json() == {"ok": True}
