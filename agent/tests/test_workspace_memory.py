"""工作空间与记忆适配器测试（真实文件系统，临时目录）。"""

from __future__ import annotations

from pathlib import Path

import pytest

from agent_service.adapters.file_workspace import FileWorkspace
from agent_service.adapters.json_memory import JsonMarkdownMemory
from agent_service.core.prompt import PromptBuilder
from agent_service.domain.errors import WorkspaceError
from agent_service.ports.memory import MemoryItem


@pytest.fixture
def ws(tmp_path: Path) -> FileWorkspace:
    return FileWorkspace(str(tmp_path / "ws"))


# ===== 工作空间 =====


def test_ensure_initialized_creates_docs(ws: FileWorkspace):
    created = ws.ensure_initialized("小助手")
    assert "IDENTITY" in created
    assert (Path(ws.root) / "memory").is_dir()
    assert "小助手" in (ws.load_doc("IDENTITY") or "")


def test_ensure_initialized_is_idempotent(ws: FileWorkspace):
    ws.ensure_initialized("A")
    assert ws.ensure_initialized("A") == []


def test_existing_doc_not_overwritten(ws: FileWorkspace):
    ws.ensure_initialized("A")
    ws.save_doc("USER", "自定义内容")
    ws.ensure_initialized("B")
    assert ws.load_doc("USER") == "自定义内容"


def test_load_missing_doc_returns_none(ws: FileWorkspace):
    ws.ensure_initialized("A")
    assert ws.load_doc("NOT_EXIST") is None


def test_empty_root_rejected():
    with pytest.raises(WorkspaceError):
        FileWorkspace("")


# ===== 记忆捕获 =====


@pytest.fixture
def mem(ws: FileWorkspace) -> JsonMarkdownMemory:
    ws.ensure_initialized("A")
    return JsonMarkdownMemory(ws.root)


def test_capture_preference(mem: JsonMarkdownMemory):
    items = mem.capture("我喜欢简洁一点的回复")
    assert len(items) == 1
    assert items[0].category == "preference"
    assert items[0].content.startswith("用户")


def test_capture_ignores_short_and_irrelevant(mem: JsonMarkdownMemory):
    assert mem.capture("嗯。好的。") == []
    assert mem.capture("今天天气不错") == []


def test_capture_email_is_entity(mem: JsonMarkdownMemory):
    items = mem.capture("我的邮箱是 a@b.com")
    assert items[0].category == "entity"


def test_store_writes_daily_file(mem: JsonMarkdownMemory, ws: FileWorkspace):
    written = mem.store([MemoryItem("用户喜欢深色主题", "preference")])
    assert written == 1
    files = list((Path(ws.root) / "memory").glob("*.md"))
    assert len(files) == 1
    assert "[preference]" in files[0].read_text(encoding="utf-8")


def test_duplicate_not_stored_twice(mem: JsonMarkdownMemory):
    mem.store([MemoryItem("用户喜欢深色主题", "preference")])
    items = mem.capture("记住我喜欢深色主题")
    assert items == []


def test_search_hits_daily_memory(mem: JsonMarkdownMemory):
    mem.store([MemoryItem("用户偏好简洁回复", "preference")])
    hits = mem.search("简洁")
    assert hits
    assert hits[0].source.startswith("memory/")


def test_search_empty_keyword(mem: JsonMarkdownMemory):
    assert mem.search("   ") == []


def test_long_term_read_write(mem: JsonMarkdownMemory, ws: FileWorkspace):
    ws.save_doc("MEMORY", "# 长期记忆\n\n- 用户住在杭州\n")
    assert "杭州" in mem.long_term()
    assert mem.search("杭州")[0].source == "MEMORY.md"


def test_recent_digest_limited(mem: JsonMarkdownMemory):
    mem.store([MemoryItem("用户喜欢蓝色", "preference")])
    digest = mem.recent_digest()
    assert "蓝色" in digest
    assert len(mem.recent_digest(max_chars=5)) <= 5


def test_store_empty_returns_zero(mem: JsonMarkdownMemory):
    assert mem.store([]) == 0


# ===== 提示组装 =====


def test_prompt_builder_priority_order(ws: FileWorkspace):
    ws.ensure_initialized("小助手")
    ws.save_doc("USER", "用户叫小明")
    mem = JsonMarkdownMemory(ws.root)
    mem.store([MemoryItem("用户喜欢咖啡", "preference")])

    prompt = PromptBuilder(ws, mem).build("运行时身份提示")

    assert prompt.index("运行时身份提示") < prompt.index("你的身份信息")
    assert "用户叫小明" in prompt
    assert "长期记忆" in prompt


def test_prompt_builder_empty(ws: FileWorkspace):
    """文档与记忆全部为空时，不应产出任何章节。"""
    ws.ensure_initialized("A")
    for name in ("IDENTITY", "USER", "SOUL", "MEMORY"):
        ws.save_doc(name, "")
    mem = JsonMarkdownMemory(ws.root)
    assert PromptBuilder(ws, mem).build(None) == ""
