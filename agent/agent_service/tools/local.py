"""本地工具集（实现 ToolPort）。

直接使用 Python 函数实现，不依赖 hello-agents 的 Tool 类：
- 少一层继承与注册开销
- schema 与执行逻辑在同一个类里，便于测试
- 与端口契约一一对应

工具清单：
    memory_search / memory_add / memory_update_longterm / memory_list
    exec_run（默认关闭，需 --enable-exec）
    web_fetch
    web_search（需 BRAVE_API_KEY）
"""

from __future__ import annotations

import logging
import re
import subprocess
from collections.abc import Callable
from typing import Any

from ..ports.memory import MemoryPort
from ..ports.tools import ToolExecutionResult, ToolSpec

logger = logging.getLogger("gent.agent.tools")

# 命令白名单（仅允许读/查询类，不含 rm/mv 等破坏性操作）
_ALLOWED_COMMANDS = frozenset(
    {
        "ls", "cat", "echo", "pwd", "grep", "find", "head", "tail", "wc",
        "sort", "uniq", "git", "uv", "python", "python3", "node",
    }
)

# 危险模式（即使命令在白名单内也拦截）
_DANGEROUS_PATTERNS = [
    re.compile(p, re.IGNORECASE)
    for p in (
        r"rm\s+-[rf]", r"\bsudo\b", r"chmod\s+777", r">\s*/dev/",
        r"\bmkfs\b", r"\bdd\s+if=", r">\s*/etc/", r"\bshutdown\b",
        r"\breboot\b", r"init\s+[06]", r":\(\)\s*\{", r">\s*\$HOME", r">\s*~",
        r"\bcurl\b.*\|\s*sh", r"\bwget\b.*\|\s*sh",
    )
]

_MAX_OUTPUT = 8_000
_EXEC_TIMEOUT = 20


class LocalToolbox:
    """进程内工具集合。"""

    def __init__(
        self,
        memory: MemoryPort,
        workspace_root: str,
        *,
        enable_exec: bool = False,
        enable_web: bool = True,
        brave_api_key: str = "",
    ) -> None:
        self._memory = memory
        self._workspace_root = workspace_root
        self._brave_key = brave_api_key

        self._handlers: dict[str, Callable[[dict[str, Any]], Any]] = {
            "memory_search": self._memory_search,
            "memory_add": self._memory_add,
            "memory_update_longterm": self._memory_update_longterm,
            "memory_list": self._memory_list,
        }
        if enable_exec:
            self._handlers["exec_run"] = self._exec_run
        if enable_web:
            self._handlers["web_fetch"] = self._web_fetch
            if brave_api_key:
                self._handlers["web_search"] = self._web_search

    # ===== ToolPort =====

    def list_specs(self) -> list[ToolSpec]:
        return [_SPECS[name] for name in self._handlers if name in _SPECS]

    async def invoke(self, name: str, arguments: dict[str, Any]) -> ToolExecutionResult:
        handler = self._handlers.get(name)
        if handler is None:
            return ToolExecutionResult(f"未知工具：{name}", is_error=True)
        try:
            return handler(arguments)
        except Exception as exc:  # noqa: BLE001 - 工具失败不得中断对话
            logger.warning("工具 %s 执行异常：%s", name, exc)
            return ToolExecutionResult(f"工具执行异常：{exc}", is_error=True)

    # ===== 记忆工具 =====

    def _memory_search(self, args: dict[str, Any]) -> ToolExecutionResult:
        keyword = str(args.get("keyword", "")).strip()
        if not keyword:
            return ToolExecutionResult("请提供搜索关键词", is_error=True)
        hits = self._memory.search(keyword, limit=int(args.get("limit", 5) or 5))
        if not hits:
            return ToolExecutionResult(f"未找到与「{keyword}」相关的记忆")
        body = "\n\n".join(f"**{h.source}**:\n```\n{h.snippet}\n```" for h in hits)
        return ToolExecutionResult(f"找到 {len(hits)} 处匹配「{keyword}」：\n\n{body}")

    def _memory_add(self, args: dict[str, Any]) -> ToolExecutionResult:
        from ..ports.memory import MemoryItem

        content = str(args.get("content", "")).strip()
        if len(content) < 2:
            return ToolExecutionResult("记忆内容太短", is_error=True)
        item = MemoryItem(content=content, category=str(args.get("category") or "fact"))
        written = self._memory.store([item])
        return ToolExecutionResult(
            f"已记录：{content[:60]}…" if written else "已存在相似记忆，未重复写入"
        )

    def _memory_update_longterm(self, args: dict[str, Any]) -> ToolExecutionResult:
        content = str(args.get("content", "")).strip()
        if not content:
            return ToolExecutionResult("内容不能为空", is_error=True)
        from pathlib import Path

        path = Path(self._workspace_root) / "MEMORY.md"
        try:
            current = path.read_text(encoding="utf-8") if path.exists() else "# 长期记忆\n"
            path.write_text(f"{current}\n\n## 新增\n\n{content}\n", encoding="utf-8")
            logger.info("更新长期记忆，新增 %d 字符", len(content))
            return ToolExecutionResult("已更新长期记忆")
        except OSError as exc:
            return ToolExecutionResult(f"写入长期记忆失败：{exc}", is_error=True)

    def _memory_list(self, _args: dict[str, Any]) -> ToolExecutionResult:
        from pathlib import Path

        root = Path(self._workspace_root)
        lines = ["# 记忆文件列表"]
        if (root / "MEMORY.md").exists():
            lines.append("- MEMORY.md（长期记忆）")
        memory_dir = root / "memory"
        if memory_dir.exists():
            for path in sorted(memory_dir.glob("*.md"), reverse=True)[:30]:
                lines.append(f"- memory/{path.name}")
        return ToolExecutionResult("\n".join(lines))

    # ===== 命令执行 =====

    def _exec_run(self, args: dict[str, Any]) -> ToolExecutionResult:
        command = str(args.get("command", "")).strip()
        if not command:
            return ToolExecutionResult("command 不能为空", is_error=True)

        blocked = next((p for p in _DANGEROUS_PATTERNS if p.search(command)), None)
        if blocked:
            logger.warning("拦截危险命令：%s", command)
            return ToolExecutionResult("命令包含危险操作，已拒绝执行", is_error=True)

        first = command.split()[0]
        if first not in _ALLOWED_COMMANDS:
            return ToolExecutionResult(
                f"命令 {first} 不在白名单内，允许：{', '.join(sorted(_ALLOWED_COMMANDS))}",
                is_error=True,
            )

        logger.info("执行命令：%s", command)
        try:
            proc = subprocess.run(
                command,
                shell=True,
                cwd=self._workspace_root,
                capture_output=True,
                text=True,
                timeout=_EXEC_TIMEOUT,
                check=False,  # 非零退出码由下方按 is_error 处理，不抛异常
            )
        except subprocess.TimeoutExpired:
            return ToolExecutionResult(f"命令超时（>{_EXEC_TIMEOUT}s）", is_error=True)

        output = (proc.stdout or "") + (proc.stderr or "")
        if len(output) > _MAX_OUTPUT:
            output = output[:_MAX_OUTPUT] + "\n…(输出已截断)"
        return ToolExecutionResult(
            output or f"（无输出，退出码 {proc.returncode}）",
            is_error=proc.returncode != 0,
        )

    # ===== 网页 =====

    def _web_fetch(self, args: dict[str, Any]) -> ToolExecutionResult:
        url = str(args.get("url", "")).strip()
        if not url.startswith(("http://", "https://")):
            return ToolExecutionResult("仅支持 http/https URL", is_error=True)
        try:
            import httpx

            resp = httpx.get(url, timeout=15, follow_redirects=True)
            resp.raise_for_status()
            return ToolExecutionResult(_html_to_text(resp.text)[:_MAX_OUTPUT])
        except Exception as exc:  # noqa: BLE001
            return ToolExecutionResult(f"抓取失败：{exc}", is_error=True)

    def _web_search(self, args: dict[str, Any]) -> ToolExecutionResult:
        query = str(args.get("query", "")).strip()
        if not query:
            return ToolExecutionResult("query 不能为空", is_error=True)
        count = min(int(args.get("count", 5) or 5), 10)
        try:
            import httpx

            resp = httpx.get(
                "https://api.search.brave.com/res/v1/web/search",
                params={"q": query, "count": count},
                headers={
                    "Accept": "application/json",
                    "X-Subscription-Token": self._brave_key,
                },
                timeout=15,
            )
            resp.raise_for_status()
            results = resp.json().get("web", {}).get("results", [])[:count]
            if not results:
                return ToolExecutionResult(f"未找到「{query}」的结果")
            lines = [
                f"- {r.get('title')}\n  {r.get('url')}\n  {r.get('description', '')}"
                for r in results
            ]
            return ToolExecutionResult("\n".join(lines))
        except Exception as exc:  # noqa: BLE001
            return ToolExecutionResult(f"搜索失败：{exc}", is_error=True)


def _html_to_text(html: str) -> str:
    """极简 HTML → 文本（去脚本/样式/标签，压缩空白）。"""
    html = re.sub(r"(?is)<(script|style)[^>]*>.*?</\1>", " ", html)
    html = re.sub(r"(?s)<[^>]+>", " ", html)
    return re.sub(r"\s+", " ", html).strip()


# ===== 工具 schema =====

_SPECS: dict[str, ToolSpec] = {
    "memory_search": ToolSpec(
        "memory_search",
        "搜索历史记忆（长期记忆与每日记忆），返回带行号的上下文。",
        {
            "type": "object",
            "properties": {
                "keyword": {"type": "string", "description": "搜索关键词"},
                "limit": {"type": "integer", "description": "最多返回条数，默认 5"},
            },
            "required": ["keyword"],
        },
    ),
    "memory_add": ToolSpec(
        "memory_add",
        "把值得长期记住的信息写入今日记忆。",
        {
            "type": "object",
            "properties": {
                "content": {"type": "string", "description": "要记住的内容"},
                "category": {
                    "type": "string",
                    "enum": ["preference", "decision", "entity", "fact"],
                    "description": "分类，默认 fact",
                },
            },
            "required": ["content"],
        },
    ),
    "memory_update_longterm": ToolSpec(
        "memory_update_longterm",
        "把关键信息追加到长期记忆 MEMORY.md（跨会话永久保留）。",
        {
            "type": "object",
            "properties": {
                "content": {"type": "string", "description": "要写入长期记忆的内容"},
            },
            "required": ["content"],
        },
    ),
    "memory_list": ToolSpec(
        "memory_list",
        "列出所有记忆文件。",
        {"type": "object", "properties": {}},
    ),
    "exec_run": ToolSpec(
        "exec_run",
        "在 Agent 工作空间内执行只读白名单命令（ls/cat/grep/git 等）。",
        {
            "type": "object",
            "properties": {
                "command": {"type": "string", "description": "要执行的命令"},
            },
            "required": ["command"],
        },
    ),
    "web_fetch": ToolSpec(
        "web_fetch",
        "抓取网页并返回纯文本内容。",
        {
            "type": "object",
            "properties": {"url": {"type": "string", "description": "网页 URL"}},
            "required": ["url"],
        },
    ),
    "web_search": ToolSpec(
        "web_search",
        "使用搜索引擎检索网络信息。",
        {
            "type": "object",
            "properties": {
                "query": {"type": "string", "description": "搜索查询词"},
                "count": {"type": "integer", "description": "返回条数，默认 5"},
            },
            "required": ["query"],
        },
    ),
}
