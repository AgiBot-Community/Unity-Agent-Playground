"""日志配置和工具函数。"""
import logging
import sys
from typing import Optional


def setup_logger(name: str, level: str = "INFO",
                 log_file: Optional[str] = None) -> logging.Logger:
    """配置并返回一个日志记录器。

    Args:
        name: 日志记录器名称，通常使用 __name__
        level: 日志级别 (DEBUG, INFO, WARNING, ERROR)
        log_file: 可选的日志文件路径

    Returns:
        配置好的 Logger 实例
    """
    logger = logging.getLogger(name)
    level = level.upper()
    if level not in ("DEBUG", "INFO", "WARNING", "ERROR"):
        raise ValueError("Invalid log level: " + level)
    logger.setLevel(level)
    logger.propagate = False

    # 格式化器
    formatter = logging.Formatter(
        '%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        datefmt='%Y-%m-%d %H:%M:%S'
    )

    # 控制台处理器
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setFormatter(formatter)
    handlers = [console_handler]

    # 文件处理器（如果指定）
    if log_file:
        file_handler = logging.FileHandler(log_file, encoding='utf-8')
        file_handler.setFormatter(formatter)
        handlers.append(file_handler)

    for handler in list(logger.handlers):
        if getattr(handler, "_x2_handler", False):
            logger.removeHandler(handler)
            handler.close()
    for handler in handlers:
        handler._x2_handler = True
        logger.addHandler(handler)

    return logger


def get_logger(name: str) -> logging.Logger:
    """获取日志记录器，沿用入口配置的父日志器，不在导入时添加处理器。

    Args:
        name: 日志记录器名称

    Returns:
        Logger 实例
    """
    return logging.getLogger(name)
