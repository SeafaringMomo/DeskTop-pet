"""服务层：编排用例（登录式的一次会话、记忆检索）。

与 api 的区别：api 只做 HTTP/SSE 协议转换，业务逻辑都在这里，便于单独测试。
"""

from .chat_service import ChatService

__all__ = ["ChatService"]
