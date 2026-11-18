"""运行时配置（由命令行参数与环境变量装配，不含任何默认密钥）。

密钥只从环境变量读取（由 C# 启动 sidecar 时注入），不落日志、不落文件。
"""

from __future__ import annotations

import argparse
import os
from dataclasses import dataclass, field


@dataclass(slots=True)
class AgentSettings:
    """Agent 服务运行时设置。"""

    # ===== 服务 =====
    host: str = "127.0.0.1"
    port: int = 0
    token: str = ""

    # ===== 用户上下文 =====
    user: str = ""
    role: str = "默认角色"
    workspace_path: str = ""

    # ===== LLM =====
    llm_model: str = ""
    llm_base_url: str = ""
    llm_api_key: str = field(default="", repr=False)
    llm_timeout: int = 60
    temperature: float = 0.7
    max_tokens: int | None = None

    # ===== 循环 =====
    max_tool_iterations: int = 8
    history_limit: int = 40
    """每轮从 C# 接收的历史消息上限（条）"""

    # ===== 工具开关 =====
    enable_exec: bool = False
    """是否启用命令执行工具（默认关闭，需显式开启）"""

    enable_web: bool = True
    brave_api_key: str = field(default="", repr=False)

    # ===== C# 工具桥 =====
    tool_bridge_url: str = ""
    tool_bridge_token: str = field(default="", repr=False)

    # ===== 观测 =====
    log_level: str = "INFO"

    def validate(self) -> list[str]:
        """校验必需项，返回缺失字段名列表（供启动时快速失败）。"""
        missing: list[str] = []
        if not self.token:
            missing.append("token")
        if not self.workspace_path:
            missing.append("workspace_path")
        if not self.llm_model:
            missing.append("llm_model")
        return missing


def parse_args(argv: list[str] | None = None) -> AgentSettings:
    """从命令行 + 环境变量构造设置。

    优先级：命令行参数 > 环境变量 > 数据类默认值。

    Args:
        argv: 参数列表（默认取 sys.argv[1:]）
    """
    parser = argparse.ArgumentParser(
        prog="agent_service",
        description="DeskTopGent Python Agent 微服务",
    )
    parser.add_argument("--host", default=os.getenv("AGENT_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.getenv("AGENT_PORT", "0")))
    parser.add_argument("--token", default=os.getenv("AGENT_TOKEN", ""))
    parser.add_argument("--user", default=os.getenv("AGENT_USER", ""))
    parser.add_argument("--role", default=os.getenv("AGENT_ROLE", "默认角色"))
    parser.add_argument("--workspace", default=os.getenv("AGENT_WORKSPACE", ""))
    parser.add_argument("--tool-bridge-url", default=os.getenv("AGENT_TOOL_BRIDGE_URL", ""))
    parser.add_argument(
        "--tool-bridge-token", default=os.getenv("AGENT_TOOL_BRIDGE_TOKEN", "")
    )
    parser.add_argument("--log-level", default=os.getenv("AGENT_LOG_LEVEL", "INFO"))
    parser.add_argument(
        "--enable-exec",
        action="store_true",
        default=os.getenv("AGENT_ENABLE_EXEC", "0") == "1",
    )

    ns = parser.parse_args(argv)

    return AgentSettings(
        host=ns.host,
        port=ns.port,
        token=ns.token,
        user=ns.user,
        role=ns.role,
        workspace_path=ns.workspace,
        log_level=ns.log_level.upper(),
        enable_exec=bool(ns.enable_exec),
        tool_bridge_url=ns.tool_bridge_url,
        tool_bridge_token=ns.tool_bridge_token,
        llm_model=os.getenv("AGENT_LLM_MODEL", ""),
        llm_base_url=os.getenv("AGENT_LLM_BASE_URL", ""),
        llm_api_key=os.getenv("AGENT_LLM_API_KEY", ""),
        llm_timeout=int(os.getenv("AGENT_LLM_TIMEOUT", "60")),
        temperature=float(os.getenv("AGENT_TEMPERATURE", "0.7")),
        max_tool_iterations=int(os.getenv("AGENT_MAX_TOOL_ITERATIONS", "8")),
        brave_api_key=os.getenv("BRAVE_API_KEY", ""),
        enable_web=os.getenv("AGENT_ENABLE_WEB", "1") != "0",
    )
