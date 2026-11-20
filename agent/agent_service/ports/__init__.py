"""端口层：抽象出所有外部依赖，core 层只依赖这些协议。

这样做的好处（微服务/六边形架构的核心）：
- 单元测试用内存实现即可，不需要网络、不需要 hello-agents
- 替换 LLM / 工具来源 / 记忆存储实现时不触碰核心编排逻辑
"""

from .llm import ChatStreamResult, LLMPort, StreamDelta
from .memory import MemoryPort
from .tools import ToolPort, ToolSpec
from .workspace import WorkspacePort

__all__ = [
    "ChatStreamResult",
    "LLMPort",
    "MemoryPort",
    "StreamDelta",
    "ToolPort",
    "ToolSpec",
    "WorkspacePort",
]
