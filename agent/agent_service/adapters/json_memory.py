"""记忆适配器（实现 MemoryPort）。

- 长期记忆：<workspace>/MEMORY.md（整份读写）
- 每日记忆：<workspace>/memory/YYYY-MM-DD.md（增量追加）
- 捕获：正则触发 + 分类 + 关键词重合度去重（移植自 helper 的 capture.py，去掉 print）
- 检索：关键词包含 + 带行号上下文
"""

from __future__ import annotations

import logging
import re
from datetime import datetime, timedelta
from pathlib import Path

from ..ports.memory import MemoryItem, MemorySearchHit

logger = logging.getLogger("gent.agent.memory")

# 触发规则：(正则, 分类)
_TRIGGERS: list[tuple[str, str]] = [
    (r"记住|记下|remember|keep in mind", "fact"),
    (r"我喜欢|我偏好|prefer|like|love|hate|讨厌|不喜欢", "preference"),
    (r"决定了|decision|用这个|选定|确定用|就用", "decision"),
    (r"\+\d{10,}|\d{3,4}[-\s]?\d{7,8}", "entity"),
    (r"[\w.-]+@[\w.-]+\.\w+", "entity"),
    (r"我的\w+是|is my|我的电话|我的邮箱|我的地址|我的名字", "entity"),
    (r"事实上|实际上|the fact is|it turns out", "fact"),
]

# 中文停用词（用于去重时的关键词提取）
_STOPWORDS = frozenset(
    ["的", "了", "是", "在", "我", "有", "和", "就", "不", "人", "都", "一", "一个", "上", "也", "很", "到", "说", "要", "去", "你", "会", "着", "没有", "看", "好", "自己", "这", "那", "什么", "这个", "那个", "可以", "就是", "这样", "然后", "还是", "但是", "因为", "所以", "如果", "虽然", "可能", "需要", "应该", "或者", "而且", "已经", "还有", "一直", "的话", "一下", "一些", "一点", "东西", "知道", "觉得", "喜欢", "偏好", "用户", "记住", "记下", "决定", "选定"]
)

_SENTENCE_SPLIT = re.compile(r"[。！？.!?]\s*|\n+")
_PREFIX_CLEAN = re.compile(r"^(用户|我|你|assistant|user)[：:]\s*")
# 触发词前缀：去掉「记住 / 帮我记下」这类祈使语，只留事实内容，
# 否则同一件事换个说法就会被当成新记忆重复写入
_TRIGGER_PREFIX = re.compile(
    r"^(?:请|帮我|麻烦)?\s*(?:记住|记下|remember|keep in mind)[，,：:、\s]*",
    re.IGNORECASE,
)


class JsonMarkdownMemory:
    """基于 Markdown 文件的记忆实现。"""

    def __init__(self, workspace_root: str, duplicate_threshold: float = 0.7) -> None:
        self._root = Path(workspace_root).expanduser().resolve()
        self._memory_dir = self._root / "memory"
        self._threshold = duplicate_threshold
        self._patterns = [(re.compile(p, re.IGNORECASE), c) for p, c in _TRIGGERS]

    # ===== MemoryPort =====

    def capture(self, text: str) -> list[MemoryItem]:
        """从文本中识别记忆条目（不落盘）。"""
        items: list[MemoryItem] = []
        seen: set[str] = set()

        for sentence in _SENTENCE_SPLIT.split(text or ""):
            sentence = sentence.strip()
            if len(sentence) < 5:
                continue

            category = next(
                (c for pattern, c in self._patterns if pattern.search(sentence)),
                None,
            )
            if not category:
                continue

            content = _TRIGGER_PREFIX.sub("", sentence)
            content = _PREFIX_CLEAN.sub("", content).strip().strip("\"'“”‘’")
            if len(content) < 5:
                continue
            if category == "preference" and not content.startswith("用户"):
                content = f"用户{content}"

            key = content.lower()
            if key in seen:
                continue
            if self._is_duplicate(content):
                continue

            seen.add(key)
            items.append(
                MemoryItem(
                    content=content,
                    category=category,
                    timestamp=datetime.now().strftime("%H:%M"),
                )
            )
        return items

    def store(self, items: list[MemoryItem]) -> int:
        """写入今日记忆文件，返回写入条数。"""
        if not items:
            return 0
        try:
            self._memory_dir.mkdir(parents=True, exist_ok=True)
            path = self._daily_path()
            if not path.exists():
                path.write_text(f"# {datetime.now():%Y-%m-%d}\n", encoding="utf-8")

            with path.open("a", encoding="utf-8") as fh:
                for item in items:
                    stamp = item.timestamp or datetime.now().strftime("%H:%M")
                    fh.write(f"\n## {stamp} - 自动捕获\n\n- [{item.category}] {item.content}\n")
            logger.info("写入 %d 条记忆到 %s", len(items), path.name)
            return len(items)
        except OSError as exc:
            logger.error("写入记忆失败：%s", exc)
            return 0

    def search(self, keyword: str, limit: int = 5) -> list[MemorySearchHit]:
        """按关键词检索长期记忆与每日记忆。"""
        keyword = (keyword or "").strip()
        if not keyword:
            return []

        hits: list[MemorySearchHit] = []
        lower = keyword.lower()

        long_term = self.long_term()
        if long_term and lower in long_term.lower():
            hits.append(MemorySearchHit(source="MEMORY.md", snippet=long_term[:800]))

        if self._memory_dir.exists():
            for path in sorted(self._memory_dir.glob("*.md"), reverse=True):
                if len(hits) >= limit:
                    break
                try:
                    text = path.read_text(encoding="utf-8")
                except OSError:
                    continue
                if lower in text.lower():
                    hits.append(
                        MemorySearchHit(
                            source=f"memory/{path.name}",
                            snippet=_context_snippet(text, lower),
                        )
                    )
        return hits[:limit]

    def long_term(self) -> str:
        path = self._root / "MEMORY.md"
        if not path.exists():
            return ""
        try:
            return path.read_text(encoding="utf-8")
        except OSError:
            return ""

    def recent_digest(self, days: int = 2, max_chars: int = 1200) -> str:
        """拼接最近 N 天每日记忆的尾部片段。"""
        if not self._memory_dir.exists():
            return ""
        chunks: list[str] = []
        for i in range(days):
            path = self._memory_dir / f"{(datetime.now() - timedelta(days=i)):%Y-%m-%d}.md"
            if not path.exists():
                continue
            try:
                chunks.append(path.read_text(encoding="utf-8").strip())
            except OSError:
                continue
        digest = "\n\n".join(chunks)
        return digest[-max_chars:] if len(digest) > max_chars else digest

    # ===== 内部 =====

    def _daily_path(self) -> Path:
        return self._memory_dir / f"{datetime.now():%Y-%m-%d}.md"

    def _is_duplicate(self, content: str) -> bool:
        """关键词覆盖率去重。

        中文无空格，按整句提取关键词会让「记住我喜欢深色主题」与
        「用户喜欢深色主题」完全不相交；因此改用 2-gram 切分。
        相似度用「较短一方被覆盖的比例」，避免长文本天然拉低分数。
        """
        keywords = _extract_keywords(content)
        if not keywords:
            return False

        candidates = [self.long_term(), self.recent_digest(days=2, max_chars=10_000)]
        if self._daily_path().exists():
            try:
                candidates.append(self._daily_path().read_text(encoding="utf-8"))
            except OSError:
                pass

        for text in candidates:
            if not text:
                continue
            other = _extract_keywords(text)
            if not other:
                continue
            matched = len(keywords & other)
            coverage = matched / min(len(keywords), len(other))
            if coverage >= self._threshold:
                return True
        return False


def _extract_keywords(text: str) -> set[str]:
    """提取中英文关键词。

    中文按 2-gram 切分（无分词依赖），英文按 3 字母以上单词。
    """
    chinese_runs = re.findall(r"[一-鿿]{2,}", text)
    words: set[str] = set()
    for run in chinese_runs:
        if len(run) == 2:
            words.add(run)
            continue
        # 相邻两字组合，过滤纯停用词组合
        for i in range(len(run) - 1):
            gram = run[i : i + 2]
            if gram not in _STOPWORDS:
                words.add(gram)
    words.update(w.lower() for w in re.findall(r"[a-zA-Z]{3,}", text))
    return words


def _context_snippet(text: str, keyword: str, context_lines: int = 2) -> str:
    """返回关键词附近的带行号上下文。"""
    lines = text.split("\n")
    picked: set[int] = set()
    for i, line in enumerate(lines):
        if keyword in line.lower():
            picked.update(
                range(max(0, i - context_lines), min(len(lines), i + context_lines + 1))
            )
    if not picked:
        return text[:400]
    return "\n".join(f"{i + 1:4d} | {lines[i]}" for i in sorted(picked))
