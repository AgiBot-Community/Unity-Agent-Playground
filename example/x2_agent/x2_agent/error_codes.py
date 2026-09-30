"""统一的错误代码定义和异常类。"""
from typing import Optional


# ASR 相关错误码 (31xx)
ERR_ASR_FAILED = 3101
ERR_ASR_EMPTY_RESULT = 3102

# TTS 相关错误码 (33xx)
ERR_TTS_FAILED = 3301
ERR_TTS_TIMEOUT = 3302

# LLM 相关错误码 (32xx)
ERR_LLM_FAILED = 3201
ERR_LLM_EMPTY_RESULT = 3202

# 保留的网关鉴权细分码；当前鉴权失败使用握手 HTTP 401。
ERR_AUTH_FAILED = 4001
ERR_CLOCK_SKEW = 4002
ERR_SIGNATURE_EXPIRED = 4003

# 保留的会话细分码；当前网关不发送这些错误帧。
ERR_SESSION_NOT_FOUND = 4101
ERR_SESSION_DISCONNECTED = 4102

# 权限、Agent 参数校验与网关会话设置错误 (409x)
ERR_NO_CONTROL_PERMISSION = 4091
ERR_INVALID_SKILL_PARAM = 4092
ERR_INVALID_SESSION_COMMAND = 4093


class AgentError(Exception):
    """Agent 基础异常类，包含错误代码。"""
    def __init__(self, code: int, message: str, cause: Optional[Exception] = None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.cause = cause
        self.__cause__ = cause


class AsrError(AgentError):
    """ASR 识别异常。"""
    def __init__(self, message: str, cause: Optional[Exception] = None):
        super().__init__(ERR_ASR_FAILED, message, cause)


class TtsError(AgentError):
    """TTS 合成异常。"""
    def __init__(self, message: str, cause: Optional[Exception] = None):
        super().__init__(ERR_TTS_FAILED, message, cause)


class LlmError(AgentError):
    """LLM 推理异常。"""
    def __init__(self, message: str, cause: Optional[Exception] = None):
        super().__init__(ERR_LLM_FAILED, message, cause)


def is_recoverable_error(error: Exception) -> bool:
    """判断错误是否可恢复（允许重试）。"""
    if isinstance(error, AgentError):
        # 网络相关错误可以重试
        if isinstance(error.cause, (TimeoutError, ConnectionError)):
            return True
        # 空结果可能是暂时性问题
        if error.code in (ERR_ASR_EMPTY_RESULT, ERR_LLM_EMPTY_RESULT, ERR_TTS_TIMEOUT):
            return True
    return False
