"""统一配置管理和验证。"""
import os
from dataclasses import dataclass, field, fields
from typing import Optional
from urllib.parse import urlsplit

from .asr import ASR_WS_URL
from .llm import ARK_API_URL
from .tts import TTS_WS_URL
from .sentence_tts import TTS_WS_URL as TTS_SENTENCE_WS_URL
from .config import load_env


@dataclass
class AgentConfig:
    """Agent 配置类，使用数据类进行类型验证。"""

    # 必需的 API 凭据
    speech_key: str = field(
        repr=False, metadata={"description": "豆包语音 API Key (ASR + TTS)"}
    )
    ark_key: str = field(
        repr=False, metadata={"description": "火山方舟 API Key (LLM)"}
    )

    # 网关连接配置
    host: str = "localhost"
    port: int = 9002
    path: str = "/api/V1/open-portal/app/wss/agent-sdk"
    app_id: str = "demo-app"
    app_key: str = field(default="demo-key", repr=False)
    app_secret: str = field(default="demo-secret", repr=False)

    # 服务配置
    llm_model: str = "doubao-seed-2-0-mini-260428"
    tts_speaker: str = "zh_female_wanqudashu_moon_bigtts"
    tts_mode: str = "bidirectional"  # "bidirectional" 或 "sentence"
    asr_resource_id: str = "volc.bigasr.sauc.duration"
    asr_after_commit: bool = False
    asr_ws_url: str = ASR_WS_URL
    ark_api_url: str = ARK_API_URL
    tts_ws_url: str = TTS_WS_URL
    tts_sentence_ws_url: str = TTS_SENTENCE_WS_URL

    # 系统提示词
    system_prompt: str = (
        "你是人形机器人X2的语音助手，名叫灵犀。"
        "回答口语化、简洁（一般不超过两句话），不要用列表和markdown。"
        "你可以通过 robot_skill 工具做动作和表情：动作有挥手"
        "（wave_hands）、张开双臂（open_arms）；"
        "表情有开心（happy）、"
        "难过（sad）、惊讶（surprised）、生气（angry）、爱心"
        "（love）。用户表达这类意图时调用工具，同时必须给一句"
        "简短的口头回应（如'好呀，我这就挥手'），不能只调用工具"
        "不说话。还可以走（walk）、转（turn）、停（stop）。"
    )

    # 其他配置
    history_turns: int = 5
    greeting: str = "你好，我是灵犀，有什么可以帮您？"
    save_audio: Optional[str] = None
    save_input: Optional[str] = None
    skills_file: Optional[str] = None
    log_level: str = "INFO"
    log_file: Optional[str] = None
    metrics_file: Optional[str] = None

    # 协议版本
    protocol_version: str = "1.0.0"

    def __post_init__(self):
        """验证配置有效性。"""
        if not self.speech_key:
            raise ValueError("缺少语音 API Key")
        if not self.ark_key:
            raise ValueError("缺少方舟 API Key")
        if type(self.port) is not int or not 1 <= self.port <= 65535:
            raise ValueError("端口必须在 1-65535 之间")
        if not self.host.strip() or not self.path.startswith("/"):
            raise ValueError("网关 host 不能为空，path 必须以 / 开头")
        if self.tts_mode not in ("bidirectional", "sentence"):
            raise ValueError("tts_mode 必须是 'bidirectional' 或 'sentence'")
        if type(self.history_turns) is not int or self.history_turns < 0:
            raise ValueError("history_turns 不能为负数")
        if self.log_level not in ("DEBUG", "INFO", "WARNING", "ERROR"):
            raise ValueError("无效的日志级别")
        for name in ("asr_ws_url", "ark_api_url", "tts_ws_url", "tts_sentence_ws_url"):
            address = urlsplit(getattr(self, name))
            schemes = ("http", "https") if name == "ark_api_url" else ("ws", "wss")
            if address.scheme not in schemes or not address.hostname:
                raise ValueError(f"{name} 必须是有效的 {'/'.join(schemes)} URL")

    @classmethod
    def from_env(cls, env_file: Optional[str] = None, **overrides) -> "AgentConfig":
        """从环境变量和 .env 文件加载配置。

        优先级: 命令行参数 > 环境变量 > .env 文件 > 默认值
        """
        load_env(env_file)
        values = dict(
            speech_key=os.environ.get("DOUBAO_SPEECH_API_KEY", ""),
            ark_key=os.environ.get("ARK_API_KEY", ""),
            llm_model=os.environ.get("DOUBAO_LLM_MODEL", cls.llm_model),
            tts_speaker=os.environ.get("DOUBAO_TTS_SPEAKER", cls.tts_speaker),
            asr_resource_id=os.environ.get("DOUBAO_ASR_RESOURCE_ID", cls.asr_resource_id),
            asr_ws_url=os.environ.get("ASR_WS_URL", cls.asr_ws_url),
            ark_api_url=os.environ.get("ARK_API_URL", cls.ark_api_url),
            tts_ws_url=os.environ.get("TTS_WS_URL", cls.tts_ws_url),
            tts_sentence_ws_url=os.environ.get("TTS_SENTENCE_WS_URL", cls.tts_sentence_ws_url),
        )
        values.update(overrides)
        return cls(**values)

    @classmethod
    def from_args(cls, args) -> "AgentConfig":
        """Validate after argparse has merged CLI, environment and defaults."""
        return cls(**{item.name: getattr(args, item.name)
                      for item in fields(cls) if hasattr(args, item.name)})

    def to_dict(self) -> dict:
        """转换为字典，隐藏敏感信息。"""
        config_dict = {
            "host": self.host,
            "port": self.port,
            "llm_model": self.llm_model,
            "tts_speaker": self.tts_speaker,
            "tts_mode": self.tts_mode,
            "history_turns": self.history_turns,
            "protocol_version": self.protocol_version,
        }
        return config_dict
