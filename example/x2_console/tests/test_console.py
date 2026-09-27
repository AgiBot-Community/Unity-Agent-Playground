import asyncio
import json
from pathlib import Path
import tempfile
import threading
import sys
import unittest

import websockets

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from x2_console.client import ConnectionSettings, ConsoleClient, EventBuffer
from x2_console.model import LogHistory, display_time
from x2_console.gateway import T


class ConsoleModelTests(unittest.TestCase):
    def test_history_is_bounded_and_search_includes_stack(self):
        history = LogHistory(capacity=2)
        for i in range(3):
            history.add(dict(level="error" if i == 2 else "info", source="Unity",
                             message="hello " + str(i), stackTrace="FailureMethod" if i == 2 else ""))
        self.assertEqual(len(history.records), 2)
        self.assertEqual(history.counts, {"info": 2, "warning": 0, "error": 1})
        self.assertEqual(len(history.filtered(level="error", source="Unity", query="failuremethod")), 1)
        self.assertEqual(history.filtered(source="Agent"), [])
        history.clear()
        self.assertEqual(history.counts["error"], 0)

    def test_export_preserves_unicode_and_stack_only_for_matching_records(self):
        history = LogHistory()
        history.add(dict(level="warning", source="Unity", message="警告\n第二行", stackTrace="method:42"))
        history.add(dict(level="info", source="Agent", message="not exported"))
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "logs.jsonl"
            self.assertEqual(history.export(path, level="warning"), 1)
            record = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(record["message"], "警告\n第二行")
            self.assertEqual(record["stackTrace"], "method:42")
            self.assertNotIn("id", record)

    def test_ui_backpressure_cannot_hide_connection_state(self):
        events = EventBuffer(capacity=2)
        for i in range(10):
            events.log("info", "Unity", str(i))
        events.update(state="offline", detail="closed")
        snapshot, logs = events.drain()
        self.assertEqual([r["message"] for r in logs], ["8", "9"])
        self.assertEqual(snapshot["state"], "offline")
        self.assertEqual(snapshot["uiDropped"], 8)

    def test_bad_numbers_and_endpoints_are_rejected(self):
        for value in ("NaN", "inf", "-1", "6", "bad"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                ConsoleClient._number(value, 0.2, 5, "distance")
        for settings in (ConnectionSettings(host="ws://localhost"), ConnectionSettings(port=0),
                         ConnectionSettings(path="/wrong?query")):
            with self.assertRaises(ValueError):
                settings.validate()
        self.assertIn("[::1]", ConnectionSettings(host="::1").uri)
        self.assertNotIn("secret-value", repr(ConnectionSettings(app_secret="secret-value")))
        self.assertEqual(display_time(None), "--:--:--")
        with self.assertRaises(TypeError):
            ConnectionSettings(mode="doubao")


class ConsoleConnectionTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.client = ConsoleClient()
        self.logs = []
        self.snapshot = {}

    async def asyncTearDown(self):
        self.client.close()
        await self.until(lambda: self.client.stopped)

    async def until(self, predicate, timeout=5):
        deadline = asyncio.get_running_loop().time() + timeout
        while asyncio.get_running_loop().time() < deadline:
            self.snapshot, records = self.client.events.drain()
            self.logs.extend(records)
            if predicate():
                return
            await asyncio.sleep(0.01)
        self.fail("Condition not reached: " + str(self.snapshot))

    @staticmethod
    async def online(ws):
        await ws.send(json.dumps({"type": T["sync"], "state": "online", "agentId": "robot-id",
                                  "controlActive": False, "controlEnabled": False,
                                  "canManage": True, "audioActive": False}))
        await ws.send(json.dumps({"type": T["runtime_log"], "robotCid": "cid-test",
                                  "level": "error", "message": "engine error", "stackTrace": "example:42",
                                  "timestampMs": 123, "sequence": 3}))

    async def test_manual_commands_log_metadata_and_parameter_validation(self):
        received = []
        async def server(ws):
            await self.online(ws)
            async for raw in ws:
                frame = json.loads(raw)
                received.append(frame)
                if frame["type"] == T["session_control"]:
                    await ws.send(json.dumps({
                        "type": T["session_state"], "robotCid": "cid-test", "revision": 1,
                        "controlOwnerCid": "cid-test", "managementOwnerCid": "cid-test",
                        "audioOwnerCid": "cid-agent",
                        "sessions": [{"robotCid": "cid-test", "role": "controller",
                                      "controlEnabled": True, "priority": 1000}]}))
        async with websockets.serve(server, "127.0.0.1", 0) as listener:
            self.client.connect(ConnectionSettings(port=listener.sockets[0].getsockname()[1], reconnect=False))
            await self.until(lambda: self.snapshot["state"] == "online" and self.snapshot["robot_cid"] == "cid-test")
            self.assertTrue(any(r["stackTrace"] == "example:42" and r["timestampMs"] == 123 for r in self.logs))
            self.assertFalse(self.snapshot["control_active"])
            self.client.command("set_control", enabled=True)
            await self.until(lambda: self.snapshot["control_active"])
            received.clear()
            self.client.command("skill", skill_type="movement", skill="walk", value="NaN")
            await self.until(lambda: any("距离" in r["message"] for r in self.logs))
            self.assertEqual(received, [])
            self.client.command("skill", skill_type="gesture", skill="wave_hands")
            self.client.command("skill", skill_type="emotion", skill="happy")
            await self.until(lambda: len(received) == 2)
            self.assertEqual(received[0]["skillName"], "wave_hands")
            self.assertTrue(all(f["robotCid"] == "cid-test" and f["agentId"] == "robot-id" for f in received))
            self.assertEqual(received[-1]["skillName"], "happy")
            self.client.command("test_audio")
            await self.until(lambda: any("未知指令" in r["message"] for r in self.logs))
            self.assertEqual(len(received), 2)
            self.client.disconnect()
            await self.until(lambda: not self.client.active)

    async def test_immediate_cancel_and_window_shutdown_release_worker(self):
        self.client.connect(ConnectionSettings(port=65432))
        self.client.disconnect()
        await self.until(lambda: not self.client.active)
        self.client.close()
        await self.until(lambda: self.client.stopped)

    async def test_busy_gateway_has_actionable_error_and_no_retry_when_disabled(self):
        def busy(connection, request):
            return connection.respond(503, "busy")
        async with websockets.serve(lambda ws: None, "127.0.0.1", 0, process_request=busy) as listener:
            self.client.connect(ConnectionSettings(port=listener.sockets[0].getsockname()[1], reconnect=False))
            await self.until(lambda: not self.client.active)
            self.assertTrue(any("达到上限" in r["message"] for r in self.logs))

    async def test_controller_role_and_priority_updates_use_same_socket(self):
        received = []
        async def server(ws):
            self.assertEqual(ws.request.headers["X-Client-Role"], "controller")
            self.assertEqual(ws.request.headers["X-Audio-Enabled"], "false")
            self.assertEqual(ws.request.headers["X-Control-Enabled"], "false")
            await self.online(ws)
            await ws.send(json.dumps({
                "type": T["session_state"], "robotCid": "cid-test", "revision": 1,
                "controlOwnerCid": "cid-agent", "managementOwnerCid": "cid-test", "audioOwnerCid": "cid-agent",
                "sessions": [
                    {"robotCid": "cid-test", "name": "console", "role": "controller",
                     "priority": 1000, "controlEnabled": False},
                    {"robotCid": "cid-agent", "name": "agent", "role": "agent", "priority": 50},
                ],
            }))
            async for raw in ws:
                frame = json.loads(raw)
                received.append(frame)
                if frame["type"] == T["session_priority"]:
                    await ws.send(json.dumps({
                        "type": T["session_state"], "robotCid": "cid-test", "revision": 2,
                        "controlOwnerCid": "cid-agent", "managementOwnerCid": "cid-test", "audioOwnerCid": "cid-agent",
                        "sessions": [{"robotCid": "cid-agent", "role": "agent", "priority": frame["priority"]}],
                    }))
        async with websockets.serve(server, "127.0.0.1", 0) as listener:
            self.client.connect(ConnectionSettings(port=listener.sockets[0].getsockname()[1], reconnect=False))
            await self.until(lambda: len(self.snapshot["sessions"]) == 2)
            self.assertFalse(self.snapshot["control_active"])
            self.assertTrue(self.snapshot["can_manage"])
            self.assertFalse(self.snapshot["audio_active"])
            self.client.command("set_priority", robot_cid="cid-agent", priority="800")
            await self.until(lambda: len(self.snapshot["sessions"]) == 1)
            self.assertEqual(received[0]["targetRobotCid"], "cid-agent")
            self.assertEqual(self.snapshot["sessions"][0]["priority"], 800)
            self.client.command("set_priority", robot_cid="cid-agent", priority="20.5")
            await self.until(lambda: any("优先级必须是整数" in r["message"] for r in self.logs))
            self.assertEqual(len(received), 1)
            self.client.disconnect()
            await self.until(lambda: not self.client.active)

    async def test_old_gateway_auto_release_keeps_management_and_stop_reclaims(self):
        received = []
        enabled, revision, agent_priority = True, 0, 50
        async def state(ws):
            nonlocal revision
            revision += 1
            await ws.send(json.dumps({
                "type": T["session_state"], "robotCid": "cid-test", "revision": revision,
                "controlOwnerCid": "cid-test" if enabled else "cid-agent",
                "managementOwnerCid": "cid-test", "audioOwnerCid": "cid-agent",
                "sessions": [
                    {"robotCid": "cid-test", "role": "controller", "priority": 1000,
                     "controlEnabled": enabled, "controlActive": enabled},
                    {"robotCid": "cid-agent", "role": "agent", "priority": agent_priority},
                ],
            }))
        async def server(ws):
            nonlocal enabled, agent_priority
            await ws.send(json.dumps({"type": T["sync"], "state": "online", "robotCid": "cid-test",
                                      "controlActive": True, "controlEnabled": True, "canManage": True}))
            await state(ws)
            async for raw in ws:
                frame = json.loads(raw)
                received.append(frame)
                if frame["type"] == T["session_control"]:
                    enabled = frame["enabled"]
                    await state(ws)
                elif frame["type"] == T["session_priority"]:
                    agent_priority = frame["priority"]
                    await state(ws)
        async with websockets.serve(server, "127.0.0.1", 0) as listener:
            self.client.connect(ConnectionSettings(port=listener.sockets[0].getsockname()[1], reconnect=False))
            await self.until(lambda: self.snapshot["state"] == "online")
            self.assertEqual(received[0]["type"], T["session_control"])
            self.assertIs(received[0]["enabled"], False)
            self.assertFalse(self.snapshot["control_enabled"])
            self.assertFalse(self.snapshot["control_active"])
            self.assertTrue(self.snapshot["can_manage"])
            self.client.command("set_priority", robot_cid="cid-agent", priority=800)
            await self.until(lambda: any(r.get("priority") == 800 for r in self.snapshot["sessions"]))
            self.client.command("skill", skill_type="gesture", skill="wave_hands")
            await self.until(lambda: any("没有动作控制权" in r["message"] for r in self.logs))
            self.assertFalse(any(f["type"] == T["skill"] for f in received))
            self.client.command("interrupt")
            await self.until(lambda: any(f["type"] == T["interrupt"] for f in received))
            self.assertEqual([f["type"] for f in received[-2:]], [T["session_control"], T["interrupt"]])
            self.assertTrue(self.snapshot["control_active"])
            self.assertEqual(self.snapshot["sessions"][0]["priority"], 1000)
            self.client.disconnect()
            await self.until(lambda: not self.client.active)

    async def test_audio_is_ignored_and_monitoring_never_replies(self):
        received, connections = [], []
        async def server(ws):
            connections.append(ws)
            await self.online(ws)
            for kind in ("start", "append", "commit"):
                await ws.send(json.dumps({"type": "agentsdk.audio_request." + kind, "audio": "not-decoded"}))
            for name in ("power", "network"):
                await ws.send(json.dumps({"type": T["state"], "stateName": name, "stateValue": "ok"}))
            async for raw in ws:
                received.append(json.loads(raw))
        async with websockets.serve(server, "127.0.0.1", 0) as listener:
            self.client.connect(ConnectionSettings(port=listener.sockets[0].getsockname()[1], reconnect=False))
            await self.until(lambda: self.snapshot["power"] == "ok" and self.snapshot["network"] == "ok")
            self.assertEqual(len(connections), 1)
            self.assertEqual(received, [])
            self.assertNotIn("asr", self.snapshot)
            self.assertFalse(hasattr(self.client, "_agent"))
            self.assertTrue(any("已忽略" in r["message"] for r in self.logs))
            self.client.disconnect()
            await self.until(lambda: not self.client.active)

    async def test_unsupported_old_gateway_is_disconnected_instead_of_holding_control(self):
        async def server(ws):
            await ws.send(json.dumps({"type": T["sync"], "state": "online"}))
            await ws.wait_closed()
        async with websockets.serve(server, "127.0.0.1", 0) as listener:
            self.client.connect(ConnectionSettings(port=listener.sockets[0].getsockname()[1], reconnect=False))
            await self.until(lambda: not self.client.active)
            self.assertTrue(any("模拟器版本过旧" in r["message"] for r in self.logs))

    async def test_connection_drop_reconnects_and_manual_commands_are_not_replayed(self):
        connections = []
        async def server(ws):
            connections.append(ws)
            await self.online(ws)
            if len(connections) == 1:
                await ws.close()
            else:
                await ws.wait_closed()
        async with websockets.serve(server, "127.0.0.1", 0) as listener:
            self.client.connect(ConnectionSettings(port=listener.sockets[0].getsockname()[1], reconnect=True))
            await self.until(lambda: len(connections) == 2 and self.snapshot["state"] == "online", timeout=6)
            self.assertEqual(self.snapshot["tx"], 0)
            self.client.disconnect()
            await self.until(lambda: not self.client.active)

    async def test_command_waiting_for_lock_cannot_cross_reconnection(self):
        connections, received = [], []
        held, release = threading.Event(), threading.Event()
        async def server(ws):
            connections.append(ws)
            await self.online(ws)
            async for raw in ws:
                received.append(json.loads(raw))
        async with websockets.serve(server, "127.0.0.1", 0) as listener:
            settings = ConnectionSettings(port=listener.sockets[0].getsockname()[1], reconnect=False)
            self.client.connect(settings)
            await self.until(lambda: self.snapshot["state"] == "online")
            async def hold_commands():
                async with self.client._command_lock:
                    held.set()
                    while not release.is_set():
                        await asyncio.sleep(0.01)
            holding = asyncio.run_coroutine_threadsafe(hold_commands(), self.client._loop)
            try:
                await self.until(held.is_set)
                self.client.command("skill", skill_type="gesture", skill="wave_hands")
                await asyncio.sleep(0.05)
                self.client.disconnect()
                await self.until(lambda: not self.client.active)
                self.client.connect(settings)
                await self.until(lambda: len(connections) == 2 and self.snapshot["state"] == "online")
                release.set()
                await asyncio.wrap_future(holding)
                await self.until(lambda: any("连接已切换" in r["message"] for r in self.logs))
                self.assertEqual(received, [])
            finally:
                release.set()
            self.client.disconnect()
            await self.until(lambda: not self.client.active)
