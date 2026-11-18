"""领域层：纯数据模型与错误定义，不依赖任何第三方库或框架。"""

from .errors import AgentError, LLMError, ToolError, WorkspaceError
from .events import AgentEvent, AgentEventKind
from .messages import normalize_history, to_openai_messages

__all__ = [
    "AgentError",
    "AgentEvent",
    "AgentEventKind",
    "LLMError",
    "ToolError",
    "WorkspaceError",
    "normalize_history",
    "to_openai_messages",
]
