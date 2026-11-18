"""DeskTopGent Python Agent 微服务包。

分层（依赖只允许从外向内）：
    api        → services → core → ports → domain
    adapters   → ports / domain（实现外部依赖：OpenAI、hello-agents、C# HTTP 桥）
    tools      → hello-agents Tool 基类（最外层，仅由 adapters 装配）
"""

__version__ = "0.1.0"
