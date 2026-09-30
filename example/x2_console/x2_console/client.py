"""Management-only gateway client. Network work lives on one asyncio thread."""
import asyncio
from collections import deque
from dataclasses import dataclass, field
import json
import math
import threading
import time
import uuid
from typing import Dict, Any, Optional, Callable

import websockets

from .gateway import T, build_headers, envelope

DEFAULT_PATH = "/api/V1/open-portal/app/wss/agent-sdk"

# 控制台常量
CONSOLE_PRIORITY = 1000  # 固定的控制台管理优先级
EVENT_BUFFER_CAPACITY = 1500  # 事件缓冲区容量
CONSOLE_RECONNECT_DELAY_SECONDS = 3  # 重连延迟
CONTROL_SWITCH_TIMEOUT_SECONDS = 2  # 控制权切换超时


@dataclass(frozen=True)
class ConnectionSettings:
    host: str = "127.0.0.1"
    port: int = 9002
    path: str = DEFAULT_PATH
    app_id: str = "demo-app"
    app_key: str = "demo-key"
    app_secret: str = field(default="demo-secret", repr=False)
    reconnect: bool = True

    def validate(self):
        if not self.host.strip() or any(c in self.host for c in "/?# \t\r\n"):
            raise ValueError("主机只填写 IP 或主机名，不包含 ws:// 或路径。")
        if not 1 <= self.port <= 65535:
            raise ValueError("端口必须在 1–65535 之间。")
        if not self.path.startswith("/") or any(c in self.path for c in "?# \r\n"):
            raise ValueError("网关路径必须以 / 开头，且不包含查询参数或空格。")
        if not all((self.app_id, self.app_key, self.app_secret)):
            raise ValueError("应用 ID、Key 和签名密钥不能为空。")

    @property
    def uri(self):
        host = self.host.strip()
        if ":" in host and not host.startswith("["):
            host = "[" + host + "]"
        return f"ws://{host}:{self.port}{self.path}"


class EventBuffer:
    """Separate snapshots from bounded log events so a flood cannot hide disconnection."""
    def __init__(self, capacity: int = EVENT_BUFFER_CAPACITY):
        self._lock = threading.Lock()
        self._logs: deque = deque(maxlen=capacity)
        self._snapshot: Dict[str, Any] = dict(
            state="offline", detail="尚未连接", rx=0, tx=0,
            activity="等待连接", skill="", robot_cid="", power="—", network="—",
            sessions=[], control_active=False, audio_active=False,
            control_enabled=False, can_manage=False, control_switch_supported=False)
        self.dropped = 0

    def update(self, **values):
        with self._lock:
            self._snapshot.update(values)

    def log(self, level, source, message, stack="", **metadata):
        entry = dict(timestampMs=int(time.time() * 1000), level=level, source=source,
                     message=str(message)[:16384], stackTrace=str(stack)[:32768])
        entry.update(metadata)
        with self._lock:
            if len(self._logs) == self._logs.maxlen:
                self.dropped += 1
            self._logs.append(entry)

    def drain(self, limit=100):
        with self._lock:
            logs = [self._logs.popleft() for _ in range(min(limit, len(self._logs)))]
            return dict(self._snapshot, uiDropped=self.dropped), logs


class ConsoleClient:
    def __init__(self, events=None):
        self.events = events or EventBuffer()
        self._gate = threading.Lock()
        self._active = False
        self._closing = False
        self._task = None
        self._ws = None
        self._online = False
        self._control_active = False
        self._control_enabled = False
        self._can_manage = False
        self._control_switch_supported = False
        self._revision = -1
        self._settings = None
        self._robot_cid = ""
        self._agent_id = ""
        self._rx = self._tx = 0
        self._monitor_pending = False
        self._unexpected_audio_warned = False
        self._ready = threading.Event()
        self._loop = asyncio.new_event_loop()
        self._thread = threading.Thread(target=self._thread_main, name="x2-console-network", daemon=True)
        self._thread.start()
        if not self._ready.wait(3):
            raise RuntimeError("无法启动通信线程。")

    @property
    def active(self):
        with self._gate:
            return self._active

    @property
    def stopped(self):
        return not self._thread.is_alive()

    def _thread_main(self):
        asyncio.set_event_loop(self._loop)
        self._ready.set()
        try:
            self._loop.run_forever()
        finally:
            pending = asyncio.all_tasks(self._loop)
            for task in pending:
                task.cancel()
            self._loop.run_until_complete(asyncio.gather(*pending, return_exceptions=True))
            self._loop.close()

    def connect(self, settings):
        settings.validate()
        with self._gate:
            if self._active or self._closing:
                raise ValueError("请先断开当前连接。")
            self._active = True
        self.events.update(state="connecting", detail="正在连接…")
        self._loop.call_soon_threadsafe(self._begin, settings)

    def _begin(self, settings):
        self._task = self._loop.create_task(self._run(settings))
        self._task.add_done_callback(self._finished)

    def _finished(self, task):
        with self._gate:
            self._active = False
        self._task = None
        self.events.update(state="offline", activity="未连接")
        if not task.cancelled() and task.exception():
            self.events.log("error", "控制台", str(task.exception()))

    def disconnect(self):
        if self.active:
            self.events.update(state="disconnecting", detail="正在断开…")
            self._loop.call_soon_threadsafe(self._cancel)

    def _cancel(self):
        if self._task is not None:
            self._task.cancel()

    def close(self):
        with self._gate:
            if self._closing:
                return
            self._closing = True
        self._loop.call_soon_threadsafe(lambda: self._loop.create_task(self._shutdown()))

    async def _shutdown(self):
        if self._task is not None:
            self._task.cancel()
            await asyncio.gather(self._task, return_exceptions=True)
        self._loop.stop()

    def command(self, name, **params):
        if self._closing:
            return
        def schedule():
            task = self._loop.create_task(self._command(name, **params))
            task.add_done_callback(self._command_finished)
        self._loop.call_soon_threadsafe(schedule)

    def _command_finished(self, task):
        if not task.cancelled() and task.exception():
            self.events.log("error", "控制台", "指令未发送：" + str(task.exception()))

    async def _run(self, settings):
        self._settings = settings
        self._rx = self._tx = 0
        self.events.update(rx=0, tx=0, skill="", robot_cid="", power="—", network="—")
        try:
            while True:
                self.events.update(state="connecting", detail=f"连接 {settings.host}:{settings.port}")
                try:
                    async with websockets.connect(
                        settings.uri,
                        additional_headers=build_headers(settings.app_id, settings.app_key,
                                                         settings.app_secret, settings.path,
                                                         role="controller", audio_enabled=False, control_enabled=False,
                                                         client_name="X2 Console"),
                        compression=None, open_timeout=5, close_timeout=1,
                        max_size=1024 * 1024, max_queue=32,
                    ) as ws:
                        self._ws = ws
                        self._online = False
                        self._robot_cid = self._agent_id = ""
                        self._revision = -1
                        self._monitor_pending = False
                        self._unexpected_audio_warned = False
                        self._send_lock = asyncio.Lock()
                        self._command_lock = asyncio.Lock()
                        self._control_changed = asyncio.Event()
                        self.events.update(state="connected", detail="握手成功，等待机器人上线…")
                        self.events.log("info", "控制台", "WebSocket 已连接 · 管理与监控")
                        await self._receive(ws)
                        raise ConnectionError("机器人已关闭连接。")
                except (ValueError, FileNotFoundError) as exc:
                    self.events.log("error", "控制台", str(exc))
                    self.events.update(detail=str(exc))
                    break
                except Exception as exc:
                    detail = str(exc)
                    if isinstance(exc, websockets.exceptions.InvalidStatus):
                        code = exc.response.status_code
                        detail = {401: "鉴权失败，请核对高级连接中的凭据。",
                                  503: "网关连接数已达到上限，请关闭不用的客户端。",
                                  404: "网关路径不存在，请核对高级连接设置。"}.get(code, f"握手失败 HTTP {code}")
                    self.events.log("warning", "控制台", detail)
                    self.events.update(detail=detail)
                    if not settings.reconnect:
                        break
                finally:
                    self._online = False
                    self._ws = None
                self.events.update(state="reconnecting", activity="等待重连")
                await asyncio.sleep(CONSOLE_RECONNECT_DELAY_SECONDS)
        except asyncio.CancelledError:
            self.events.update(detail="连接已断开")
            raise
        finally:
            self.events.update(robot_cid="", sessions=[], control_active=False, audio_active=False,
                               can_manage=False, control_switch_supported=False, power="—", network="—")

    async def _receive(self, ws):
        async for raw in ws:
            self._rx += 1
            self.events.update(rx=self._rx)
            try:
                frame = json.loads(raw)
                if not isinstance(frame, dict):
                    raise ValueError("消息不是 JSON 对象")
            except (ValueError, TypeError) as exc:
                self.events.log("warning", "协议", "无法解析网关消息：" + str(exc))
                continue
            if frame.get("robotCid"):
                self._robot_cid = frame["robotCid"]
                self.events.update(robot_cid=self._robot_cid)
            if frame.get("agentId"):
                self._agent_id = frame["agentId"]
            kind = frame.get("type")
            if str(kind).startswith("agentsdk.audio_request."):
                if not self._unexpected_audio_warned:
                    self._unexpected_audio_warned = True
                    self.events.log("warning", "控制台", "已忽略意外收到的音频；管理控制台不参与语音处理。")
                continue
            if kind == T["runtime_log"]:
                level = frame.get("level", "info")
                message = str(frame.get("message", ""))
                source = "协议" if message.startswith("gw ") else "Unity"
                self.events.log(level if level in ("info", "warning", "error") else "info", source,
                                message, frame.get("stackTrace", ""),
                                timestampMs=frame.get("timestampMs", int(time.time() * 1000)),
                                sequence=frame.get("sequence"), logType=frame.get("logType"),
                                droppedCount=frame.get("droppedCount", 0),
                                sessionDroppedCount=frame.get("sessionDroppedCount", 0),
                                truncated=frame.get("truncated", False))
                continue
            if kind == T["sync"]:
                self._online = frame.get("state") == "online"
                self._control_active = frame.get("controlActive", True)
                self._control_enabled = frame.get("controlEnabled", True)
                self._can_manage = frame.get("canManage", self._control_active)
                self._control_switch_supported = "controlEnabled" in frame
                if self._online and not self._control_switch_supported:
                    raise ValueError("模拟器版本过旧，无法安全进入管理监控状态，请更新模拟器。")
                self._monitor_pending = self._online and self._control_enabled
                self.events.update(state="online" if self._online and not self._monitor_pending else "connected",
                                   detail="正在交还动作控制…" if self._monitor_pending else "机器人在线 · 管理与监控",
                                   activity="监控已就绪",
                                   control_active=self._control_active,
                                   audio_active=frame.get("audioActive", False),
                                   control_enabled=self._control_enabled, can_manage=self._can_manage,
                                   control_switch_supported=self._control_switch_supported)
                self._control_changed.set()
                self.events.log("info", "Unity", "机器人状态 · " + str(frame.get("state", "")))
                # Older gateways support release but may ignore the handshake flag.
                if self._monitor_pending:
                    await self._send(envelope(T["session_control"], self._settings.app_id, self._robot_cid,
                                              "evt-monitor-" + uuid.uuid4().hex[:12], enabled=False))
            elif kind == T["session_state"]:
                revision = frame.get("revision", 0)
                if revision < self._revision:
                    continue
                self._revision = revision
                self._control_active = frame.get("controlOwnerCid") == self._robot_cid
                self._can_manage = frame.get("managementOwnerCid", frame.get("controlOwnerCid")) == self._robot_cid
                own = next((r for r in frame.get("sessions", []) if r.get("robotCid") == self._robot_cid), {})
                self._control_enabled = own.get("controlEnabled", self._control_enabled)
                if not self._control_enabled:
                    self._monitor_pending = False
                self.events.update(sessions=frame.get("sessions", []),
                                   state="online" if self._online and not self._monitor_pending else "connected",
                                   detail="正在交还动作控制…" if self._monitor_pending else "机器人在线 · 管理与监控",
                                   control_active=self._control_active,
                                   audio_active=frame.get("audioOwnerCid") == self._robot_cid,
                                   control_enabled=self._control_enabled, can_manage=self._can_manage)
                self._control_changed.set()
            elif kind == T["state"]:
                name = frame.get("stateName")
                if name in ("power", "network"):
                    self.events.update(**{name: str(frame.get("stateValue", "—"))[:80]})
            elif kind == T["skill_state"]:
                text = f"{frame.get('skillName', '')} · {frame.get('state', '')}"
                self.events.update(skill=text)
                self.events.log("warning" if frame.get("state") == "failed" else "info", "Unity", "技能 " + text)
            elif kind == T["error"]:
                self.events.log("error", "Unity", str(frame.get("errorMsg", "")))

    async def _send(self, frame):
        connection = self._ws
        if connection is None:
            raise ConnectionError("尚未连接机器人。")
        frame = dict(frame, robotCid=self._robot_cid or "cid-console",
                     cid=self._robot_cid or "cid-console",
                     agentId=self._agent_id or self._settings.app_id)
        async with self._send_lock:
            if self._ws is not connection:
                raise ConnectionError("连接已切换，请重新发送指令。")
            await connection.send(json.dumps(frame, ensure_ascii=False))
        if self._ws is not connection:
            raise ConnectionError("连接已切换，请重新发送指令。")
        self._tx += 1
        self.events.update(tx=self._tx)

    @staticmethod
    def _number(value, low, high, label):
        try:
            number = float(value)
        except (ValueError, TypeError):
            raise ValueError(label + "必须是数字。") from None
        if not math.isfinite(number) or not low <= number <= high:
            raise ValueError(f"{label}必须在 {low:g}–{high:g} 之间。")
        return number

    async def _command(self, name, **params):
        if not self._online:
            raise ConnectionError("请先连接并等待机器人上线。")
        connection = self._ws
        def require_connection():
            if self._ws is not connection or not self._online:
                raise ConnectionError("连接已切换，请重新发送指令。")
        async with self._command_lock:
            require_connection()
            event = "evt-console-" + uuid.uuid4().hex[:12]
            def message(kind, **values):
                return envelope(T[kind], self._settings.app_id, self._robot_cid, event, **values)
            if name == "set_control":
                if not self._control_switch_supported:
                    raise ValueError("当前模拟器不支持动作接管开关，请更新模拟器。")
                enabled = params.get("enabled")
                if not isinstance(enabled, bool):
                    raise ValueError("动作接管开关必须为布尔值。")
                await self._send(message("session_control", enabled=enabled))
                self.events.log("info", "控制台", "已请求" + ("接管动作" if enabled else "交还动作控制给 Agent"))
                return
            if name == "set_priority":
                if not self._can_manage:
                    raise ConnectionError("当前控制台没有优先级管理权限。")
                priority = self._number(params.get("priority"), 0, 999, "优先级")
                if not priority.is_integer():
                    raise ValueError("优先级必须是整数。")
                await self._send(message("session_priority", targetRobotCid=params["robot_cid"],
                                         priority=int(priority)))
                self.events.log("info", "控制台", "已请求更新客户端优先级 · " + str(int(priority)))
                return
            if name == "interrupt" and not self._control_active and self._can_manage and self._control_switch_supported:
                self._control_changed.clear()
                await self._send(message("session_control", enabled=True))
                deadline = asyncio.get_running_loop().time() + CONTROL_SWITCH_TIMEOUT_SECONDS
                while not self._control_active:
                    require_connection()
                    remaining = deadline - asyncio.get_running_loop().time()
                    if remaining <= 0:
                        raise ConnectionError("收回动作控制权超时，请确认连接状态。")
                    try:
                        await asyncio.wait_for(self._control_changed.wait(), remaining)
                    except asyncio.TimeoutError:
                        raise ConnectionError("收回动作控制权超时，请确认连接状态。") from None
                    self._control_changed.clear()
                require_connection()
            if not self._control_active:
                raise ConnectionError("当前会话没有动作控制权；请先启用“控制台接管动作”。")
            if name == "interrupt":
                await self._send(message("interrupt", interruptType="chat", interruptTips="控制台停止"))
                self.events.update(activity="已发送停止指令")
            elif name == "skill":
                skill_type, skill = params["skill_type"], params["skill"]
                known = {"gesture": {"wave_hands", "open_arms"},
                         "movement": {"walk", "turn", "stop"},
                         "emotion": {"happy", "sad", "surprised", "angry", "love", "neutral"}}
                if skill not in known.get(skill_type, set()):
                    raise ValueError("不支持的技能。")
                values = {}
                if skill == "walk":
                    values["distanceM"] = self._number(params.get("value", 1), 0.2, 5, "距离")
                elif skill == "turn":
                    values["angleDeg"] = self._number(params.get("value", 90), -360, 360, "角度")
                elif skill_type == "emotion":
                    values["durationMs"] = 3000
                await self._send(message("skill", skillType=skill_type, skillName=skill, skillParam=values))
                self.events.log("info", "控制台", f"已发送技能 · {skill_type}/{skill}")
            else:
                raise ValueError("未知指令。")
