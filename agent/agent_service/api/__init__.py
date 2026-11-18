"""API 层：HTTP/SSE 协议转换，不含业务逻辑。"""

from .app_factory import create_app

__all__ = ["create_app"]
