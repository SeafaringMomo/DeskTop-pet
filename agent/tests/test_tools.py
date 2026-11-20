"""工具层测试：本地工具的安全边界、组合工具的路由、远程工具降级。"""

from __future__ import annotations

from pathlib import Path

import pytest

from agent_service.adapters.json_memory import JsonMarkdownMemory
from agent_service.tools.composite import CompositeToolbox
from agent_service.tools.local import LocalToolbox
from agent_service.tools.remote import RemoteToolbox


@pytest.fixture
def toolbox(tmp_path: Path) -> LocalToolbox:
    root = tmp_path / "ws"
    root.mkdir()
    (root / "MEMORY.md").write_text("# 长期记忆\n", encoding="utf-8")
    return LocalToolbox(
        memory=JsonMarkdownMemory(str(root)),
        workspace_root=str(root),
        enable_exec=True,
        enable_web=False,
    )


# ===== 本地工具 =====


async def test_memory_add_and_search(toolbox: LocalToolbox):
    res = await toolbox.invoke("memory_add", {"content": "用户喜欢喝茶", "category": "preference"})
    assert not res.is_error

    found = await toolbox.invoke("memory_search", {"keyword": "喝茶"})
    assert not found.is_error
    assert "喝茶" in found.content


async def test_memory_add_too_short(toolbox: LocalToolbox):
    res = await toolbox.invoke("memory_add", {"content": "a"})
    assert res.is_error


async def test_memory_search_requires_keyword(toolbox: LocalToolbox):
    res = await toolbox.invoke("memory_search", {})
    assert res.is_error


async def test_unknown_tool_returns_error(toolbox: LocalToolbox):
    res = await toolbox.invoke("not_exists", {})
    assert res.is_error
    assert "未知工具" in res.content


async def test_exec_blocks_dangerous_command(toolbox: LocalToolbox):
    res = await toolbox.invoke("exec_run", {"command": "rm -rf /"})
    assert res.is_error
    assert "危险" in res.content


async def test_exec_blocks_non_whitelisted(toolbox: LocalToolbox):
    res = await toolbox.invoke("exec_run", {"command": "telnet example.com"})
    assert res.is_error
    assert "白名单" in res.content


async def test_exec_allows_whitelisted(toolbox: LocalToolbox):
    res = await toolbox.invoke("exec_run", {"command": "echo hello"})
    assert not res.is_error
    assert "hello" in res.content


async def test_exec_disabled_by_default(tmp_path: Path):
    box = LocalToolbox(
        memory=JsonMarkdownMemory(str(tmp_path)),
        workspace_root=str(tmp_path),
        enable_exec=False,
        enable_web=False,
    )
    assert "exec_run" not in [s.name for s in box.list_specs()]


def test_web_tools_disabled_when_no_key(tmp_path: Path):
    box = LocalToolbox(
        memory=JsonMarkdownMemory(str(tmp_path)),
        workspace_root=str(tmp_path),
        enable_web=True,
        brave_api_key="",
    )
    names = [s.name for s in box.list_specs()]
    assert "web_fetch" in names
    assert "web_search" not in names


def test_all_specs_have_valid_schema(toolbox: LocalToolbox):
    for spec in toolbox.list_specs():
        schema = spec.to_openai_schema()
        assert schema["type"] == "function"
        assert schema["function"]["parameters"]["type"] == "object"


# ===== 组合工具 =====


class StaticTools:
    def __init__(self, names, label):
        from agent_service.ports.tools import ToolExecutionResult, ToolSpec

        self._specs = [ToolSpec(n, n) for n in names]
        self._label = label
        self._result = ToolExecutionResult

    def list_specs(self):
        return self._specs

    async def invoke(self, name, arguments):
        return self._result(f"{self._label}:{name}")


async def test_composite_routes_to_owner():
    box = CompositeToolbox([StaticTools(["a"], "local"), StaticTools(["b"], "remote")])
    assert (await box.invoke("a", {})).content == "local:a"
    assert (await box.invoke("b", {})).content == "remote:b"


async def test_composite_first_wins_on_conflict():
    box = CompositeToolbox([StaticTools(["x"], "local"), StaticTools(["x"], "remote")])
    assert (await box.invoke("x", {})).content == "local:x"
    assert len(box.list_specs()) == 1


async def test_composite_unknown_tool():
    box = CompositeToolbox([StaticTools(["a"], "local")])
    assert (await box.invoke("zzz", {})).is_error


# ===== 远程工具 =====


async def test_remote_unavailable_returns_error():
    box = RemoteToolbox("")
    res = await box.invoke("todo_add", {})
    assert res.is_error


async def test_remote_refresh_without_url():
    box = RemoteToolbox("")
    assert await box.refresh() == []


async def test_remote_refresh_failure_degrades(monkeypatch):
    box = RemoteToolbox("http://127.0.0.1:1")

    class Boom:
        async def get(self, _path):
            raise RuntimeError("连接被拒绝")

    monkeypatch.setattr(box, "_get_client", lambda: Boom())
    assert await box.refresh() == []
    assert box.list_specs() == []
