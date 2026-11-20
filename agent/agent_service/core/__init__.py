"""核心层：纯编排逻辑，只依赖 domain / ports。

不导入 openai、fastapi、hello_agents —— 因此可以脱离网络与框架做单元测试。
"""

from .agent_loop import AgentLoop
from .prompt import PromptBuilder
from .settings import AgentSettings

__all__ = ["AgentLoop", "AgentSettings", "PromptBuilder"]
