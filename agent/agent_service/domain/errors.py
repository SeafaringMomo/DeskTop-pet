"""领域异常定义。

所有异常都带 code，便于上层（C# / 日志）做稳定判断，不依赖异常类名。
"""


class AgentError(Exception):
    """Agent 领域异常基类。"""

    code = "AGENT_ERROR"

    def __init__(self, message: str, *, code: str | None = None) -> None:
        super().__init__(message)
        if code:
            self.code = code


class LLMError(AgentError):
    """LLM 调用失败（网络、鉴权、限流、响应格式）。"""

    code = "LLM_ERROR"


class ToolError(AgentError):
    """工具执行失败（参数错误、远程桥不可达）。"""

    code = "TOOL_ERROR"


class WorkspaceError(AgentError):
    """工作空间读写失败。"""

    code = "WORKSPACE_ERROR"
