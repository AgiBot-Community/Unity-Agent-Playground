"""Bounded console log history and filtering, shared by the GUI and tests."""
from collections import deque
from datetime import datetime
import json


def display_time(timestamp_ms):
    try:
        return datetime.fromtimestamp(float(timestamp_ms) / 1000).strftime("%H:%M:%S.%f")[:-3]
    except (ValueError, TypeError, OverflowError, OSError):
        return "--:--:--"


class LogHistory:
    def __init__(self, capacity=1200):
        self.records = deque(maxlen=capacity)
        self.counts = {"info": 0, "warning": 0, "error": 0}
        self._next_id = 0

    def add(self, record):
        self._next_id += 1
        record = dict(record, id=str(self._next_id))
        if record.get("level") not in self.counts:
            record["level"] = "info"
        self.records.append(record)
        self.counts[record["level"]] += 1
        return record

    def clear(self):
        self.records.clear()
        self.counts = dict.fromkeys(self.counts, 0)

    @staticmethod
    def matches(record, level="", source="", query=""):
        return (not level or record.get("level") == level) and (
            not source or record.get("source") == source) and (
            not query or query.casefold() in (
                str(record.get("message", "")) + "\n" + str(record.get("stackTrace", ""))
            ).casefold())

    def filtered(self, level="", source="", query=""):
        return [r for r in self.records if self.matches(r, level, source, query)]

    def export(self, path, **filters):
        records = self.filtered(**filters)
        with open(path, "w", encoding="utf-8", newline="\n") as stream:
            for record in records:
                stream.write(json.dumps({k: v for k, v in record.items() if k != "id"},
                                        ensure_ascii=False) + "\n")
        return len(records)
