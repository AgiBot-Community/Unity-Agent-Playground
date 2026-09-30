"""性能指标收集和监控。"""
import time
from typing import Any, Dict, List, Optional
from dataclasses import dataclass, field
from collections import defaultdict, deque
import threading


@dataclass
class MetricSample:
    """单个指标样本。"""
    timestamp: float
    value: float
    labels: Dict[str, str] = field(default_factory=dict)


class PerformanceMetrics:
    """性能指标收集器。"""

    def __init__(self, max_samples: int = 1000):
        if max_samples < 1:
            raise ValueError("max_samples must be positive")
        self._metrics = defaultdict(lambda: deque(maxlen=max_samples))
        self._lock = threading.Lock()
        self._counters: Dict[str, float] = defaultdict(float)

    def record_duration(self, metric_name: str, duration_ms: float,
                       labels: Optional[Dict[str, str]] = None) -> None:
        """记录耗时指标。

        Args:
            metric_name: 指标名称
            duration_ms: 耗时（毫秒）
            labels: 可选的标签字典
        """
        sample = MetricSample(
            timestamp=time.time(),
            value=duration_ms,
            labels=labels or {}
        )
        with self._lock:
            self._metrics[metric_name].append(sample)

    def increment_counter(self, counter_name: str, value: float = 1.0) -> None:
        """增加计数器。

        Args:
            counter_name: 计数器名称
            value: 增加值
        """
        with self._lock:
            self._counters[counter_name] += value

    def get_stats(self, metric_name: str) -> Dict[str, float]:
        """获取指标的统计信息。

        Args:
            metric_name: 指标名称

        Returns:
            包含 min, max, avg, p50, p95, p99 的字典
        """
        with self._lock:
            values = [sample.value for sample in self._metrics.get(metric_name, [])]
        return self._stats(values)

    @staticmethod
    def _stats(values: List[float]) -> Dict[str, float]:
        if not values:
            return {}

        values = sorted(values)
        count = len(values)

        def percentile(p: float) -> float:
            k = (count - 1) * p
            f = int(k)
            c = f + 1 if f + 1 < count else f
            return values[f] + (k - f) * (values[c] - values[f])

        return {
            "count": count,
            "min": values[0],
            "max": values[-1],
            "avg": sum(values) / count,
            "p50": percentile(0.50),
            "p95": percentile(0.95),
            "p99": percentile(0.99),
        }

    def get_counter(self, counter_name: str) -> float:
        """获取计数器值。

        Args:
            counter_name: 计数器名称

        Returns:
            计数器当前值
        """
        with self._lock:
            return self._counters.get(counter_name, 0.0)

    def get_summary(self) -> Dict[str, Any]:
        """获取所有指标的摘要。

        Returns:
            指标摘要字典
        """
        with self._lock:
            durations = {
                name: [sample.value for sample in samples]
                for name, samples in self._metrics.items()
            }
            counters = dict(self._counters)
        summary = {name: self._stats(values) for name, values in durations.items()}
        summary["counters"] = counters
        return summary

    def reset(self) -> None:
        """重置所有指标。"""
        with self._lock:
            self._metrics.clear()
            self._counters.clear()


# 全局单例
_global_metrics = PerformanceMetrics()


def get_metrics() -> PerformanceMetrics:
    """获取全局指标收集器。"""
    return _global_metrics


# 常用指标名称常量
METRIC_ASR_DURATION = "asr_duration_ms"
METRIC_LLM_FIRST_TOKEN = "llm_first_token_ms"
METRIC_LLM_TOTAL_DURATION = "llm_total_duration_ms"
METRIC_TTS_FIRST_CHUNK = "tts_first_chunk_ms"
METRIC_TTS_TOTAL_DURATION = "tts_total_duration_ms"
METRIC_ROUND_TOTAL_DURATION = "round_total_duration_ms"

# 计数器名称常量
COUNTER_ASR_SUCCESS = "asr_success_count"
COUNTER_ASR_FAILURE = "asr_failure_count"
COUNTER_LLM_SUCCESS = "llm_success_count"
COUNTER_LLM_FAILURE = "llm_failure_count"
COUNTER_TTS_SUCCESS = "tts_success_count"
COUNTER_TTS_FAILURE = "tts_failure_count"
COUNTER_SKILL_EXECUTED = "skill_executed_count"
