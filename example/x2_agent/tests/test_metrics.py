import concurrent.futures
import threading
import unittest

from x2_agent.metrics import PerformanceMetrics


class MetricsTests(unittest.TestCase):
    def test_summary_after_duration_and_counter_returns_without_deadlock(self):
        metrics = PerformanceMetrics()
        metrics.record_duration("asr", 10)
        metrics.record_duration("asr", 30)
        metrics.increment_counter("success")
        result = []
        worker = threading.Thread(target=lambda: result.append(metrics.get_summary()), daemon=True)
        worker.start()
        worker.join(1)
        self.assertFalse(worker.is_alive(), "get_summary deadlocked after recording a duration")
        self.assertEqual(result[0]["asr"]["avg"], 20)
        self.assertEqual(result[0]["counters"]["success"], 1)

    def test_concurrent_recording_and_summary_keep_counts_and_snapshot_consistent(self):
        metrics = PerformanceMetrics()

        def record():
            for _ in range(100):
                metrics.record_duration("latency", 3)
                metrics.increment_counter("success")

        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            list(pool.map(lambda _: record(), range(4)))
        snapshot = metrics.get_stats("latency")
        self.assertEqual(snapshot["count"], 400)
        self.assertEqual(snapshot["avg"], 3)
        self.assertEqual(metrics.get_counter("success"), 400)
        metrics.reset()
        self.assertEqual(metrics.get_summary(), {"counters": {}})
        self.assertEqual(snapshot["count"], 400)
