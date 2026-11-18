"""服务入口：解析参数 → 装配依赖 → 启动 uvicorn。

日志统一写 stdout，由 C# 主进程捕获后转发到 Logger("Agent")。
"""

from __future__ import annotations

import logging
import sys

from .api.app_factory import create_app
from .bootstrap import build_context
from .core.settings import parse_args


def configure_logging(level: str) -> None:
    """配置根 logger（stdout，含时间与模块名）。"""
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)-5s [%(name)s] %(message)s",
        stream=sys.stdout,
        force=True,
    )


def main(argv: list[str] | None = None) -> int:
    """启动服务。

    Returns:
        进程退出码（0 正常）
    """
    settings = parse_args(argv)
    configure_logging(settings.log_level)
    logger = logging.getLogger("gent.agent.main")

    try:
        ctx = build_context(settings)
    except ValueError as exc:
        logger.error("启动失败：%s", exc)
        return 2

    app = create_app(ctx)

    import uvicorn

    logger.info(
        "Agent 服务启动：host=%s port=%d user=%s role=%s",
        settings.host,
        settings.port,
        settings.user,
        settings.role,
    )
    uvicorn.run(app, host=settings.host, port=settings.port, log_level="warning")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
