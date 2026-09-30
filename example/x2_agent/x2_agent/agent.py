"""Unity voice-agent session orchestration and Doubao CLI."""
import argparse
import asyncio
import base64
import json
import logging
import os
from pathlib import Path
import time
import uuid
from contextlib import aclosing
from typing import Dict, List, Optional, Any, AsyncIterator
import websockets
from .asr import StreamingAsr, asr_transcribe
from .audio import save_wav, trunc
from .config import load_env
from .gateway import T, build_headers, envelope, gateway_messages
from .llm import LlmClient
from .tts import BidiTtsClient
from .sentence_tts import tts_stream
from .agent_config import AgentConfig
from .skill_loader import SkillLoader
from .logging_config import setup_logger
from .error_codes import (
    ERR_ASR_FAILED, ERR_ASR_EMPTY_RESULT, ERR_LLM_FAILED, ERR_TTS_FAILED,
    ERR_INVALID_SKILL_PARAM,
)
from .metrics import PerformanceMetrics

logger = logging.getLogger(__name__)

# 断句和音频参数常量
SENTENCE_MIN_WEAK_CHARS = 8  # 弱标点需要凑够的最少字符数
SENTENCE_MAX_BUFFER = 40  # 超长无标点时的强制切分长度
SENTENCE_MIN_WEAK_CHARS_FIRST = 4  # 首句的最小弱标点字符数（更激进）
SENTENCE_MAX_BUFFER_FIRST = 24  # 首句的最大缓冲长度

# ASR 音频参数
ASR_CHUNK_SIZE = 6400  # 200ms @16k16bit = 6400 bytes
ASR_QUEUE_MAX_SIZE = 600  # 最多积压一分钟的音频帧

def slice_history(history: list, turns: int) -> list:
    """取最近 turns 轮对话；截断点对齐到 user 消息边界，
    避免把 assistant.tool_calls / tool 结果对从中间切开（API 会 400）。"""
    if turns <= 0 or len(history) <= turns * 2:
        return list(history)
    cut = len(history) - turns * 2
    while cut < len(history) and history[cut].get("role") != "user":
        cut += 1
    if cut >= len(history):        # 异常兜底：退回粗截
        cut = len(history) - turns * 2
    return list(history[cut:])


# 断句规则：强标点立即成句；弱标点需凑够 min_weak 字；超长无标点强制切
_SENT_STRONG = "。！？!?…\n"
_SENT_WEAK = "，、；;:,"


def cut_sentences(pending: str, min_weak: int = SENTENCE_MIN_WEAK_CHARS,
                  max_buf: int = SENTENCE_MAX_BUFFER) -> tuple:
    """把待合成文本切成完整句子。返回 (句子列表, 剩余待拼文本)。"""
    out = []
    rest = pending
    while rest:
        cut = -1
        for i, ch in enumerate(rest):
            if ch in _SENT_STRONG or (ch in _SENT_WEAK and i + 1 >= min_weak):
                cut = i
                break
        if cut < 0:
            if len(rest) >= max_buf:
                out.append(rest)
                rest = ""
            break
        end = cut + 1
        while end < len(rest) and rest[end] in _SENT_STRONG + _SENT_WEAK:
            end += 1          # 连续标点（如"？！"）并入同句
        out.append(rest[:end])
        rest = rest[end:]
    return out, rest


NO_CONTROL_REPLY = "当前语音会话没有动作控制权，这次动作不会执行。请先在控制台确认控制权归属。"


def is_direct_gesture_request(text):
    """Recognize only short affirmative gesture requests, for permission feedback, never execution."""
    text = "".join(c for c in text if not c.isspace() and c not in "，,。.!！?？、~～")
    if len(text) > 80:
        return False
    prefixes = ("麻烦你", "机器人", "请你", "帮我", "给我", "向我", "对我", "你好", "您好", "灵犀", "请", "嗯", "呃", "你")
    suffixes = ("可以吗", "好不好", "好吗", "谢谢", "一下", "吧")
    while True:
        prefix = next((p for p in prefixes if text.startswith(p)), None)
        if prefix is None:
            break
        text = text[len(prefix):]
    while True:
        suffix = next((s for s in suffixes if text.endswith(s)), None)
        if suffix is None:
            break
        text = text[:-len(suffix)]
    return text in {"挥手", "挥挥手", "挥一下手", "挥一挥手", "招手", "招招手",
                    "张开双臂", "张开你的双臂", "张开手臂", "张开你的手臂"}


class TtsFailure(RuntimeError):
    pass


class DoubaoAgent:
    def __init__(self, args):
        self.args = args
        self.robot_cid = "cid-agent-demo"
        self.history = []          # [{role, content}, ...]
        self.control_active = True
        self.audio_active = True
        self._authority_revision = -1
        self._tts_seq = 0
        self._asr = None
        self.skills = SkillLoader(getattr(args, "skills_file", None))
        self.skill_tools = self.skills.get_tool_definition()
        self.metrics = PerformanceMetrics()
        self.llm = LlmClient(getattr(args, "ark_api_url", None))
        self.tts = BidiTtsClient(args.speech_key, args.tts_speaker, getattr(args, "tts_ws_url", None))
        self._tts_warm = None
        self._llm_warm = None

    def warm_connections(self):
        if self._llm_warm is None or self._llm_warm.done():
            async def prepare_llm():
                try:
                    await self.llm.warm(self.args.ark_key)
                except Exception as exc:
                    logger.warning("agent     LLM 预连接未完成，实际请求时重试: %s" % exc)
            self._llm_warm = asyncio.create_task(prepare_llm())
        if self.args.tts_mode != "bidirectional":
            return
        if self._tts_warm is not None and not self._tts_warm.done():
            return

        async def prepare():
            try:
                await self.tts.connect()
            except Exception as exc:
                logger.warning("agent     TTS 预连接失败，实际合成时重试: %s" % exc)

        self._tts_warm = asyncio.create_task(prepare())

    async def close(self):
        if self._llm_warm is not None:
            if not self._llm_warm.done():
                self._llm_warm.cancel()
            await asyncio.gather(self._llm_warm, return_exceptions=True)
        if self._tts_warm is not None:
            if not self._tts_warm.done():
                self._tts_warm.cancel()
            await asyncio.gather(self._tts_warm, return_exceptions=True)
        await self.tts.close()
        await self.llm.close()
        if getattr(self.args, "metrics_file", None):
            Path(self.args.metrics_file).write_text(
                json.dumps(self.metrics.get_summary(), ensure_ascii=False, indent=2),
                encoding="utf-8")

    async def speech_stream(self, texts):
        if self.args.tts_mode == "bidirectional":
            async with aclosing(self.tts.stream(texts)) as stream:
                async for pcm in stream:
                    yield pcm
            return
        pending, first = "", True
        async for text in texts:
            pending += text
            sentences, pending = cut_sentences(
                pending,
                min_weak=SENTENCE_MIN_WEAK_CHARS_FIRST if first else SENTENCE_MIN_WEAK_CHARS,
                max_buf=SENTENCE_MAX_BUFFER_FIRST if first else SENTENCE_MAX_BUFFER
            )
            for sentence in sentences:
                first = False
                async with aclosing(tts_stream(sentence, self.args.tts_speaker,
                                               self.args.speech_key,
                                               self.args.tts_sentence_ws_url)) as stream:
                    async for pcm in stream:
                        yield pcm
        if pending:
            async with aclosing(tts_stream(pending, self.args.tts_speaker,
                                           self.args.speech_key,
                                           self.args.tts_sentence_ws_url)) as stream:
                async for pcm in stream:
                    yield pcm

    async def send(self, ws, obj):
        text = json.dumps(obj, ensure_ascii=False, separators=(",", ":"))
        logger.info("agent -> gw  %s" % trunc(text))
        await ws.send(text)

    async def send_error(self, ws, event_id, code, msg):
        await self.send(ws, envelope(T["error"], self.args.app_id,
                                     self.robot_cid, event_id,
                                     errorCode=code, errorMsg=msg))

    async def agent_round(self, ws, req, audio_buf, streaming_asr=None):
        """commit 后取 ASR 最终结果，再跑 LLM → TTS（流式下发）。"""
        a = self.args
        event_id = req.get("eventId", "")
        item_id = req.get("itemId") or ("item-" + uuid.uuid4().hex[:8])
        t0 = time.perf_counter()

        # 诊断：保存上行音频（验证 Unity→agent 音频链路内容）
        if a.save_input:
            save_wav(a.save_input, bytes(audio_buf))
            logger.info("agent     已保存上行音频 → %s (%d bytes)"
                  % (a.save_input, len(audio_buf)))

        # ---- 1) ASR ----
        try:
            if streaming_asr is not None:
                asr_text = await streaming_asr.finish()
            else:
                asr_text = await asr_transcribe(bytes(audio_buf),
                                               a.speech_key, a.asr_resource_id, a.asr_ws_url)
        except Exception as e:
            self.metrics.increment_counter("asr_failure_count")
            logger.error("agent     ASR 失败: %s" % e)
            await self.send_error(ws, event_id, ERR_ASR_FAILED, "asr failed: %s" % e)
            return
        self.metrics.record_duration("asr_duration_ms", (time.perf_counter() - t0) * 1000)
        logger.info("agent     [ASR %.0fms（commit 后）] %r"
              % ((time.perf_counter() - t0) * 1000, asr_text))
        if not asr_text:
            self.metrics.increment_counter("asr_failure_count")
            await self.send_error(ws, event_id, ERR_ASR_EMPTY_RESULT, "empty asr result")
            return
        self.metrics.increment_counter("asr_success_count")
        await self.send(ws, envelope(T["asr_final"], a.app_id, self.robot_cid,
                                    event_id, text=asr_text))

        # LLM 持续接收；单个 TTS worker 保持语音顺序，二者并行。
        t1 = time.perf_counter()
        history_start = len(self.history)
        self.history.append({"role": "user", "content": asr_text})
        reply = ""
        total = bytearray()
        first_audio = None
        first_text = None
        queue = asyncio.Queue(maxsize=128)

        async def texts():
            while True:
                text = await queue.get()
                if text is None:
                    return
                yield text

        async def speak():
            nonlocal first_audio
            stream = self.speech_stream(texts())
            try:
                async for pcm in stream:
                    if first_audio is None:
                        first_audio = time.perf_counter()
                        self.metrics.record_duration("tts_first_chunk_ms",
                                                     (first_audio - (first_text or t1)) * 1000)
                        logger.info("agent     [TTS 首包 %.0fms（自轮次开始）; 首文字后 %.0fms]"
                              % ((time.perf_counter() - t0) * 1000,
                                 (first_audio - (first_text or t1)) * 1000))
                    total.extend(pcm)
                    await self.send(ws, envelope(
                        T["tts_delta"], a.app_id, self.robot_cid, event_id,
                        item_id=item_id, audio=base64.b64encode(pcm).decode("ascii"),
                        audioLen=len(pcm)))
            except Exception as exc:
                raise TtsFailure(str(exc)) from exc
            finally:
                await stream.aclose()

        worker = asyncio.create_task(speak())

        async def enqueue(text):
            if worker.done():
                worker.result()
                raise TtsFailure("TTS 提前结束")
            put = asyncio.create_task(queue.put(text))
            try:
                await asyncio.wait({put, worker}, return_when=asyncio.FIRST_COMPLETED)
                if worker.done():
                    worker.result()
                    if text is not None or not put.done():
                        raise TtsFailure("TTS 提前结束")
                await put
            finally:
                if not put.done():
                    put.cancel()
                await asyncio.gather(put, return_exceptions=True)

        async def llm_events():
            if not self.control_active and is_direct_gesture_request(asr_text):
                yield "text", NO_CONTROL_REPLY
                return
            if self._llm_warm is not None:
                await self._llm_warm
            prompt = a.system_prompt
            if not self.control_active:
                prompt += "\n当前机器人动作由高优先级控制台接管。你可以对话，但不能执行动作，也不要声称已执行动作。"
            stream = self.llm.stream(
                prompt, slice_history(self.history, a.history_turns),
                a.llm_model, a.ark_key, tools=self.skill_tools if self.control_active else None)
            next_item = None
            try:
                while True:
                    next_item = asyncio.create_task(anext(stream))
                    await asyncio.wait({next_item, worker}, return_when=asyncio.FIRST_COMPLETED)
                    if worker.done():
                        worker.result()
                        raise TtsFailure("TTS 提前结束")
                    try:
                        item = next_item.result()
                    except StopAsyncIteration:
                        return
                    yield item
            finally:
                if next_item is not None:
                    if not next_item.done():
                        next_item.cancel()
                    await asyncio.gather(next_item, return_exceptions=True)
                await stream.aclose()

        events = llm_events()
        try:
            tool_calls = []
            async for kind, payload in events:
                if kind == "tool_calls":
                    tool_calls = payload
                    continue
                if first_text is None:
                    first_text = time.perf_counter()
                    self.metrics.record_duration("llm_first_token_ms", (first_text - t1) * 1000)
                reply += payload
                await self.send(ws, envelope(T["llm_delta"], a.app_id, self.robot_cid,
                                            event_id, item_id=item_id, text=payload))
                await enqueue(payload)

            ack = ""
            denied = False
            for tc in tool_calls:
                if tc.get("name") != "robot_skill":
                    continue
                # Authority may change while ASR/LLM/TTS is running.
                if not self.control_active:
                    if not denied:
                        denied = True
                        first_text = first_text or time.perf_counter()
                        reply += NO_CONTROL_REPLY
                        await self.send(ws, envelope(T["llm_delta"], a.app_id, self.robot_cid,
                                                    event_id, item_id=item_id, text=NO_CONTROL_REPLY))
                        await enqueue(NO_CONTROL_REPLY)
                    continue
                try:
                    args = json.loads(tc.get("arguments") or "{}")
                    if not isinstance(args, dict):
                        raise ValueError("工具参数必须为对象")
                    params = self.skills.validate_request(
                        args.get("skillType"), args.get("skillName"), args.get("skillParam"))
                except ValueError:
                    logger.warning("agent     工具参数非法: %r" % tc.get("arguments"))
                    await self.send_error(ws, event_id, ERR_INVALID_SKILL_PARAM, "invalid skill parameters")
                    correction = "动作参数无效，这次动作不会执行。"
                    first_text = first_text or time.perf_counter()
                    reply += correction
                    await self.send(ws, envelope(T["llm_delta"], a.app_id, self.robot_cid,
                                                event_id, item_id=item_id, text=correction))
                    await enqueue(correction)
                    continue
                await self.send(ws, envelope(
                    T["skill"], a.app_id, self.robot_cid, event_id, item_id=item_id,
                    skillType=args.get("skillType", ""), skillName=args.get("skillName", ""),
                    skillParam=params))
                self.metrics.increment_counter("skill_executed_count")
                ack = self.skills.get_acknowledgment(
                    args.get("skillType", ""), args.get("skillName", ""), params)
            if not reply:
                if not tool_calls:
                    raise RuntimeError("empty llm reply")
                reply = ack or "好的。"
                first_text = time.perf_counter()
                await self.send(ws, envelope(T["llm_delta"], a.app_id, self.robot_cid,
                                            event_id, item_id=item_id, text=reply))
                await enqueue(reply)

            await self.send(ws, envelope(T["llm_done_item"], a.app_id, self.robot_cid,
                                        event_id, item_id=item_id))
            await self.send(ws, envelope(T["llm_done"], a.app_id, self.robot_cid, event_id))
            logger.info("agent     [LLM 完 %.0fms] %s"
                  % ((time.perf_counter() - t1) * 1000, trunc(reply, 80)))
            self.metrics.record_duration("llm_total_duration_ms", (time.perf_counter() - t1) * 1000)
            self.metrics.increment_counter("llm_success_count")
            await enqueue(None)
            await worker
            self.metrics.increment_counter("tts_success_count")
            self.metrics.record_duration("tts_total_duration_ms",
                                         (time.perf_counter() - (first_text or t1)) * 1000)
            self.history.append({"role": "assistant", "content": reply})
            if a.save_audio and total:
                save_wav(a.save_audio, total)
            logger.info("agent     本轮完成: 总耗时 %.0fms（音频 %d bytes PCM）"
                  % ((time.perf_counter() - t0) * 1000, len(total)))
        except Exception as exc:
            del self.history[history_start:]
            stage, code = ("tts", ERR_TTS_FAILED) if isinstance(exc, TtsFailure) else ("llm", ERR_LLM_FAILED)
            self.metrics.increment_counter(stage + "_failure_count")
            logger.error("agent     %s 失败: %s" % (stage.upper(), exc))
            await self.send_error(ws, event_id, code, "%s failed: %s" % (stage, exc))
        except asyncio.CancelledError:
            del self.history[history_start:]
            raise
        finally:
            self.metrics.record_duration("round_total_duration_ms", (time.perf_counter() - t0) * 1000)
            await events.aclose()
            if not worker.done():
                worker.cancel()
            await asyncio.gather(worker, return_exceptions=True)
            # 失败也结束已发送的音频轮次，避免 Unity 留在播放/冻结 VAD 状态。
            try:
                await self.send(ws, envelope(T["tts_done_item"], a.app_id, self.robot_cid,
                                            event_id, item_id=item_id))
                await self.send(ws, envelope(T["tts_done"], a.app_id, self.robot_cid, event_id))
            except websockets.exceptions.ConnectionClosed:
                pass


    async def greet(self, ws):
        """连接就绪后的开场播报（独立于对话轮次，网关直接播）。"""
        a = self.args
        if not a.greeting:
            return
        event_id = "evt-greet-" + uuid.uuid4().hex[:8]
        item_id = "item-greet-" + uuid.uuid4().hex[:8]
        try:
            await self.send(ws, envelope(T["llm_delta"], a.app_id,
                                         self.robot_cid, event_id,
                                         item_id=item_id, text=a.greeting))
            await self.send(ws, envelope(T["llm_done_item"], a.app_id,
                                         self.robot_cid, event_id,
                                         item_id=item_id))
            await self.send(ws, envelope(T["llm_done"], a.app_id,
                                         self.robot_cid, event_id))
            async def texts():
                yield a.greeting

            async with aclosing(self.speech_stream(texts())) as stream:
                async for pcm in stream:
                    self._tts_seq += 1
                    await self.send(ws, envelope(
                        T["tts_delta"], a.app_id, self.robot_cid, event_id,
                        item_id=item_id,
                        audio=base64.b64encode(pcm).decode("ascii"),
                        audioLen=len(pcm)))
            logger.info("agent     开场播报已下发: %r" % a.greeting)
        except websockets.exceptions.ConnectionClosed:
            raise
        except Exception as e:
            logger.warning("agent     开场播报失败（不影响对话）: %s" % e)
            await self.send_error(ws, event_id, ERR_TTS_FAILED, "greeting tts failed: %s" % e)
        finally:
            try:
                await self.send(ws, envelope(T["tts_done_item"], a.app_id,
                                             self.robot_cid, event_id, item_id=item_id))
                await self.send(ws, envelope(T["tts_done"], a.app_id,
                                             self.robot_cid, event_id))
            except websockets.exceptions.ConnectionClosed:
                pass

    def _on_gateway_state(self, frame):
        if frame.get("type") == T["sync"]:
            self.robot_cid = frame.get("robotCid", self.robot_cid)
            self.control_active = frame.get("controlActive", True)
            self.audio_active = frame.get("audioActive", True)
        else:
            revision = frame.get("revision", 0)
            if revision < self._authority_revision:
                return
            self._authority_revision = revision
            self.control_active = frame.get("controlOwnerCid") == self.robot_cid
            self.audio_active = frame.get("audioOwnerCid") == self.robot_cid

    async def session(self, ws):
        self._authority_revision = -1
        try:
            async with gateway_messages(ws, on_session_state=self._on_gateway_state) as frames:
                await self._session_frames(ws, frames)
        finally:
            if self._asr is not None:
                await self._asr.close()
                self._asr = None

    async def _session_frames(self, ws, frames):
        a = self.args
        audio_buf = bytearray()
        recording_started = None

        async for raw, f in frames:
            ftype = f.get("type", "")
            if ftype != T["a_append"]:        # append 太吵，不打
                logger.info("agent <- gw  %s" % trunc(raw))

            if ftype == T["sync"]:
                logger.info("agent     会话就绪 state=%s callbackType=%s"
                      % (f.get("state"), f.get("callbackType")))
                if self.audio_active:
                    self.warm_connections()
                    await self.greet(ws)
            elif ftype == T["session_state"]:
                if not self.audio_active and self._asr is not None:
                    await self._asr.close()
                    self._asr = None
                    audio_buf = bytearray()
                logger.info("agent     控制权=%s 语音接收=%s" %
                      (self.control_active, self.audio_active))
            elif ftype == T["a_start"]:
                self.warm_connections()
                if self._asr is not None:
                    await self._asr.close()
                audio_buf = bytearray()
                recording_started = time.perf_counter()
                self._asr = (None if a.asr_after_commit else
                             StreamingAsr(a.speech_key, a.asr_resource_id, a.asr_ws_url))
            elif ftype == T["a_append"]:
                b64 = f.get("audio", "")
                pcm = base64.b64decode(b64) if b64 else b""
                audio_buf += pcm
                if self._asr is not None and pcm:
                    self._asr.feed(pcm)
            elif ftype == T["a_commit"]:
                ms = len(audio_buf) / 32.0
                logger.info("agent     收到 commit：%d bytes（约 %.0f ms 语音）"
                      % (len(audio_buf), ms))
                if recording_started is not None:
                    logger.info("agent     [录音 start→commit %.0fms]"
                          % ((time.perf_counter() - recording_started) * 1000))
                active_asr, self._asr = self._asr, None
                try:
                    await self.agent_round(ws, f, audio_buf, active_asr)
                finally:
                    if active_asr is not None:
                        await active_asr.close()
                    audio_buf = bytearray()
                    recording_started = None
            elif ftype == T["error"]:
                logger.warning("agent     网关错误: code=%s msg=%s"
                      % (f.get("errorCode"), f.get("errorMsg")))
            elif ftype == "agentsdk.skill_response.state":
                logger.info("agent     技能状态: %s → %s %s"
                      % (f.get("skillName"), f.get("state"),
                         f.get("detail", "")))
            elif ftype == "agentsdk.state_request.meta":
                pass
            else:
                logger.info("agent     （忽略 %s）" % ftype)

    async def run(self):
        try:
            await self._run()
        finally:
            await self.close()

    async def _run(self):
        a = self.args
        uri = "ws://%s:%d%s" % (a.host, a.port, a.path)
        while True:
            headers = build_headers(a.app_id, a.app_key, a.app_secret, a.path,
                                    protocol_version=a.protocol_version)
            try:
                logger.info("agent     连接 %s ..." % uri)
                async with websockets.connect(
                        uri,
                        additional_headers=headers,
                        compression=None,       # Unity 手写 WS 服务端不支持压缩扩展
                        user_agent_header=None,
                        open_timeout=5) as ws:
                    logger.info("agent     握手成功（101 Switching Protocols）")
                    await self.session(ws)
            except websockets.exceptions.InvalidStatus as e:
                logger.warning("agent     握手被拒绝: HTTP %s" % e.response.status_code)
                logger.warning("agent     （400=握手头不兼容 401=签名错 404=路径错 503=连接数达到上限）")
            except (websockets.exceptions.ConnectionClosed, OSError,
                    asyncio.TimeoutError) as e:
                logger.warning("agent     连接断开：%s" % e)
            logger.info("agent     3 秒后重连 ...")
            await asyncio.sleep(3)


def parse_args(argv=None):
    preliminary = argparse.ArgumentParser(add_help=False)
    preliminary.add_argument("--env-file")
    options, _ = preliminary.parse_known_args(argv)
    load_env(options.env_file)
    p = argparse.ArgumentParser(description="X02 豆包真实智能体客户端")
    p.add_argument("--env-file", help="Explicit .env path; existing environment variables take priority")
    p.add_argument("--host", default=AgentConfig.host)
    p.add_argument("--port", type=int, default=AgentConfig.port)
    p.add_argument("--path", default=AgentConfig.path)
    p.add_argument("--app-id", default=AgentConfig.app_id)
    p.add_argument("--app-key", default=AgentConfig.app_key)
    p.add_argument("--app-secret", default=AgentConfig.app_secret)
    p.add_argument("--speech-key", default=os.environ.get("DOUBAO_SPEECH_API_KEY", ""),
                   help="语音控制台 API Key（ASR+TTS），或环境变量 DOUBAO_SPEECH_API_KEY")
    p.add_argument("--ark-key", default=os.environ.get("ARK_API_KEY", ""),
                   help="火山方舟 API Key（LLM），或环境变量 ARK_API_KEY")
    p.add_argument("--llm-model", default=os.environ.get("DOUBAO_LLM_MODEL",
                   AgentConfig.llm_model))
    p.add_argument("--tts-speaker", default=os.environ.get("DOUBAO_TTS_SPEAKER",
                   AgentConfig.tts_speaker))
    p.add_argument("--tts-mode", choices=["bidirectional", "sentence"],
                   default=AgentConfig.tts_mode,
                   help="默认双向流式（文本边生成边合成）；sentence 使用逐句合成兼容模式")
    p.add_argument("--asr-resource-id", default=os.environ.get(
        "DOUBAO_ASR_RESOURCE_ID", AgentConfig.asr_resource_id))
    p.add_argument("--asr-after-commit", action="store_true",
                   help="对照模式：收到整段录音后才连接 ASR（默认边录边传）")
    p.add_argument("--system-prompt", default=AgentConfig.system_prompt)
    p.add_argument("--history-turns", type=int, default=AgentConfig.history_turns,
                   help="LLM 记忆的对话轮数")
    p.add_argument("--greeting",
                   default=AgentConfig.greeting,
                   help="连接就绪后的开场播报（置空 '' 禁用）")
    p.add_argument("--save-audio", metavar="PATH", default=None,
                   help="保存最后一轮 TTS 音频到 wav")
    p.add_argument("--save-input", metavar="PATH", default=None,
                   help="保存最近一轮上行语音到 wav（诊断用）")
    for name in ("asr_ws_url", "ark_api_url", "tts_ws_url", "tts_sentence_ws_url"):
        p.add_argument("--" + name.replace("_", "-"),
                       default=os.environ.get(name.upper(), getattr(AgentConfig, name)))
    p.add_argument("--skills-file", help="技能 YAML 路径；默认使用项目 skills.yaml")
    p.add_argument("--log-level", choices=["DEBUG", "INFO", "WARNING", "ERROR"], default="INFO")
    p.add_argument("--log-file", help="保存 Python Agent 日志到 UTF-8 文件")
    p.add_argument("--metrics-file", help="退出时写入耗时统计和计数 JSON")
    p.add_argument("--protocol-version", default=AgentConfig.protocol_version,
                   help="发送版本标记；不代表网关已协商版本")
    return p.parse_args(argv)


def main():
    try:
        args = AgentConfig.from_args(parse_args())
        setup_logger("x2_agent", args.log_level, args.log_file)
        asyncio.run(DoubaoAgent(args).run())
    except (ValueError, OSError) as error:
        raise SystemExit(str(error)) from error
    except KeyboardInterrupt:
        logger.info("Agent 已停止")


if __name__ == "__main__":
    main()
