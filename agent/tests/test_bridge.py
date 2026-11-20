"""远程工具桥集成测试。

用一个最小的本地 HTTP 服务模拟 C# 的 AgentToolBridge，
验证：清单加载 → 工具注册 → 调用路由 → 失败降级。
"""

from __future__ import annotations

import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from typing import ClassVar

import pytest

from agent_service.tools.composite import CompositeToolbox
from agent_service.tools.local import LocalToolbox
from agent_service.tools.remote import RemoteToolbox

TOOLS = [
    {
        "name": "todo_add",
        "description": "新增待办",
        "parameters": {
            "type": "object",
            "properties": {"content": {"type": "string"}},
            "required": ["content"],
        },
    },
    {
        "name": "todo_list",
        "description": "列出待办",
        "parameters": {"type": "object", "properties": {}},
    },
]


class _Handler(BaseHTTPRequestHandler):
    """模拟 C# 工具桥（仅实现测试所需路径）。"""

    token = "bridge-token"
    calls: ClassVar[list[dict]] = []

    def _json(self, status: int, payload: dict) -> None:
        body = json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path != "/tools":
            self._json(404, {"error": "not found"})
            return
        if self.headers.get("X-Agent-Token") != self.token:
            self._json(401, {"error": "unauthorized"})
            return
        self._json(200, {"tools": TOOLS})

    def do_POST(self):
        if self.headers.get("X-Agent-Token") != self.token:
            self._json(401, {"error": "unauthorized"})
            return
        length = int(self.headers.get("Content-Length", "0"))
        payload = json.loads(self.rfile.read(length) or b"{}")
        _Handler.calls.append(payload)
        name = payload.get("name")
        if name == "todo_add":
            self._json(200, {"content": "已添加，Id=1", "is_error": False})
        else:
            self._json(200, {"content": "未知工具", "is_error": True})

    def log_message(self, *_args):  # 静默
        pass


@pytest.fixture(scope="module")
def bridge_url():
    server = HTTPServer(("127.0.0.1", 0), _Handler)
    port = server.server_address[1]
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    yield f"http://127.0.0.1:{port}"
    server.shutdown()


async def test_remote_refresh_loads_tools(bridge_url: str):
    box = RemoteToolbox(bridge_url, token="bridge-token")
    specs = await box.refresh()
    assert [s.name for s in specs] == ["todo_add", "todo_list"]
    assert specs[0].description == "新增待办"
    await box.aclose()


async def test_remote_invoke_routes_to_bridge(bridge_url: str):
    _Handler.calls.clear()
    box = RemoteToolbox(bridge_url, token="bridge-token")
    await box.refresh()
    res = await box.invoke("todo_add", {"content": "买牛奶"})
    assert res.content == "已添加，Id=1"
    assert res.is_error is False
    assert _Handler.calls == [{"name": "todo_add", "args": {"content": "买牛奶"}}]
    await box.aclose()


async def test_remote_bad_token_degrades(bridge_url: str):
    box = RemoteToolbox(bridge_url, token="wrong")
    assert await box.refresh() == []
    await box.aclose()


async def test_composite_merges_local_and_remote(bridge_url: str, tmp_path):
    from agent_service.adapters.json_memory import JsonMarkdownMemory

    local = LocalToolbox(
        memory=JsonMarkdownMemory(str(tmp_path)),
        workspace_root=str(tmp_path),
        enable_exec=False,
        enable_web=False,
    )
    remote = RemoteToolbox(bridge_url, token="bridge-token")
    await remote.refresh()

    box = CompositeToolbox([local, remote])
    names = {s.name for s in box.list_specs()}
    assert "memory_add" in names
    assert "todo_add" in names
    await remote.aclose()


async def test_composite_rebuild_picks_up_late_remote(bridge_url: str, tmp_path):
    """远程工具在启动后才加载时，rebuild 应让它可见。"""
    from agent_service.adapters.json_memory import JsonMarkdownMemory

    local = LocalToolbox(
        memory=JsonMarkdownMemory(str(tmp_path)),
        workspace_root=str(tmp_path),
        enable_exec=False,
        enable_web=False,
    )
    remote = RemoteToolbox(bridge_url, token="bridge-token")
    box = CompositeToolbox([local, remote])
    assert "todo_add" not in {s.name for s in box.list_specs()}

    await remote.refresh()
    box.rebuild()
    assert "todo_add" in {s.name for s in box.list_specs()}
    assert (await box.invoke("todo_add", {"content": "x"})).content == "已添加，Id=1"
    await remote.aclose()
