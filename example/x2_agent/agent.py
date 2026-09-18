"""Unity voice-agent session orchestration and Doubao CLI."""
import argparse
import asyncio
import base64
import json
import os
import time
import uuid
from contextlib import aclosing
import websockets
from .asr import StreamingAsr, asr_transcribe
from .audio import save_wav, trunc
from .config import load_env
from .gateway import T, build_headers, envelope
from .llm import LlmClient
from .tts import BidiTtsClient
from .sentence_tts import tts_stream

# 机器人技能工具定义（function calling），与 Unity SkillCatalog 一一对应。
SKILL_TOOLS = [{
    "type": "function",
    "function": {
        "name": "robot_skill",
        "description": (
            "让机器人执行一个动作或表情技能。用户表达动作/表情/移动意图时调用；"
            "调用后正常用自然语言回应即可。"
        ),
        "parameters": {
            "type": "object",
            "properties": {
                "skillType": {"type": "string",
                             "enum": ["gesture", "movement", "emotion"]},
                "skillName": {"type": "string", "enum": [
                    # gesture（3 个基本动作）
                    "wave_hands", "bow", "open_arms",
                    # movement
                    "walk", "turn", "stop",
                    # emotion（5 个经典表情）
                    "happy", "sad", "surprised", "angry", "love",
                    "neutral"]},
                "skillParam": {
                    "type": "object",
                    "description": "walk: distanceM(米,默认1)；turn: angleDeg(度,右转为正)；emotion: durationMs(默认3000)",
                    "properties": {
                        "distanceM": {"type": "number"},
                        "angleDeg": {"type": "number"},
                        "durationMs": {"type": "number"},
                    },
                },
            },
            "required": ["skillType", "skillName"],
        },
    },
}]

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


def cut_sentences(pending: str, min_weak: int = 8, max_buf: int = 40):
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


# 技能即时口播表：纯工具调用时立刻回话（与动作并行），不等二轮 LLM
SKILL_ACKS = {
    ("gesture", "wave_hands"): "好呀，我这就挥挥手～",
    ("gesture", "bow"): "给您鞠个躬～",
    ("gesture", "open_arms"): "欢迎欢迎！",
    ("movement", "stop"): "好的，我停下了。",
    ("emotion", "happy"): "我现在好开心呀！",
    ("emotion", "sad"): "呜，有点难过……",
    ("emotion", "surprised"): "哇！真的吗？",
    ("emotion", "angry"): "哼，我生气啦！",
    ("emotion", "love"): "爱心送给您～",
    ("emotion", "neutral"): "好的。",
}
SKILL_ACK_GENERIC = "好的，我这就来！"


def pick_ack(skill_type, skill_name, param):
    """按技能挑即时口播；walk/turn 带上参数。"""
    if skill_name == "walk":
        try:
            d = float((param or {}).get("distanceM", 1))
            return "好，我往前走%s米。" % ("%g" % d)
        except (TypeError, ValueError):
            pass
    elif skill_name == "turn":
        try:
            deg = float((param or {}).get("angleDeg", 90))
            return "好，我向%s转。" % ("右" if deg >= 0 else "左")
        except (TypeError, ValueError):
            pass
    return SKILL_ACKS.get((skill_type, skill_name), SKILL_ACK_GENERIC)

class TtsFailure(RuntimeError):
    pass


class DoubaoAgent:
    def __init__(self, args):
        self.args = args
        self.robot_cid = "cid-agent-demo"
        self.history = []          # [{role, content}, ...]
        self._tts_seq = 0
        self._asr = None
        self.llm = LlmClient()
        self.tts = BidiTtsClient(args.speech_key, args.tts_speaker)
        self._tts_warm = None
        self._llm_warm = None

    def warm_connections(self):
        if self._llm_warm is None or self._llm_warm.done():
            async def prepare_llm():
                try:
                    await self.llm.warm(self.args.ark_key)
                except Exception as exc:
                    print("agent     LLM 预连接未完成，实际请求时重试: %s" % exc)
            self._llm_warm = asyncio.create_task(prepare_llm())
        if self.args.tts_mode != "bidirectional":
            return
        if self._tts_warm is not None and not self._tts_warm.done():
            return

        async def prepare():
            try:
                await self.tts.connect()
            except Exception as exc:
                print("agent     TTS 预连接失败，实际合成时重试: %s" % exc)

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

    async def speech_stream(self, texts):
        if self.args.tts_mode == "bidirectional":
            async with aclosing(self.tts.stream(texts)) as stream:
                async for pcm in stream:
                    yield pcm
            return
        pending, first = "", True
        async for text in texts:
            pending += text
            sentences, pending = cut_sentences(pending, min_weak=4 if first else 8,
                                               max_buf=24 if first else 40)
            for sentence in sentences:
                first = False
                async with aclosing(tts_stream(sentence, self.args.tts_speaker,
                                               self.args.speech_key)) as stream:
                    async for pcm in stream:
                        yield pcm
        if pending:
            async with aclosing(tts_stream(pending, self.args.tts_speaker,
                                           self.args.speech_key)) as stream:
                async for pcm in stream:
                    yield pcm

    async def send(self, ws, obj):
        text = json.dumps(obj, ensure_ascii=False, separators=(",", ":"))
        print("agent -> gw  %s" % trunc(text))
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
        t0 = time.time()

        # 诊断：保存上行音频（验证 Unity→agent 音频链路内容）
        if a.save_input:
            save_wav(a.save_input, bytes(audio_buf))
            print("agent     已保存上行音频 → %s (%d bytes)"
                  % (a.save_input, len(audio_buf)))

        # ---- 1) ASR ----
        try:
            if streaming_asr is not None:
                asr_text = await streaming_asr.finish()
            else:
                asr_text = await asr_transcribe(bytes(audio_buf),
                                               a.speech_key, a.asr_resource_id)
        except Exception as e:
            print("agent     ASR 失败: %s" % e)
            await self.send_error(ws, event_id, 3101, "asr failed: %s" % e)
            return
        print("agent     [ASR %.0fms（commit 后）] %r"
              % ((time.time() - t0) * 1000, asr_text))
        if not asr_text:
            await self.send_error(ws, event_id, 3102, "empty asr result")
            return
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
                        print("agent     [TTS 首包 %.0fms（自轮次开始）; 首文字后 %.0fms]"
                              % ((time.time() - t0) * 1000,
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
            if self._llm_warm is not None:
                await self._llm_warm
            stream = self.llm.stream(
                a.system_prompt, slice_history(self.history, a.history_turns),
                a.llm_model, a.ark_key, tools=SKILL_TOOLS)
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
                reply += payload
                await self.send(ws, envelope(T["llm_delta"], a.app_id, self.robot_cid,
                                            event_id, item_id=item_id, text=payload))
                await enqueue(payload)

            ack = ""
            for tc in tool_calls:
                if tc.get("name") != "robot_skill":
                    continue
                try:
                    args = json.loads(tc.get("arguments") or "{}")
                except ValueError:
                    print("agent     工具参数非法: %r" % tc.get("arguments"))
                    continue
                await self.send(ws, envelope(
                    T["skill"], a.app_id, self.robot_cid, event_id, item_id=item_id,
                    skillType=args.get("skillType", ""), skillName=args.get("skillName", ""),
                    skillParam=args.get("skillParam") or {}))
                ack = pick_ack(args.get("skillType", ""), args.get("skillName", ""),
                               args.get("skillParam"))
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
            print("agent     [LLM 完 %.0fms] %s"
                  % ((time.perf_counter() - t1) * 1000, trunc(reply, 80)))
            await enqueue(None)
            await worker
            self.history.append({"role": "assistant", "content": reply})
            if a.save_audio and total:
                save_wav(a.save_audio, total)
            print("agent     本轮完成: 总耗时 %.0fms（音频 %d bytes PCM）"
                  % ((time.time() - t0) * 1000, len(total)))
        except Exception as exc:
            del self.history[history_start:]
            stage, code = ("tts", 3301) if isinstance(exc, TtsFailure) else ("llm", 3201)
            print("agent     %s 失败: %s" % (stage.upper(), exc))
            await self.send_error(ws, event_id, code, "%s failed: %s" % (stage, exc))
        except asyncio.CancelledError:
            del self.history[history_start:]
            raise
        finally:
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

            async for pcm in self.speech_stream(texts()):
                self._tts_seq += 1
                await self.send(ws, envelope(
                    T["tts_delta"], a.app_id, self.robot_cid, event_id,
                    item_id=item_id,
                    audio=base64.b64encode(pcm).decode("ascii"),
                    audioLen=len(pcm)))
            await self.send(ws, envelope(T["tts_done_item"], a.app_id,
                                         self.robot_cid, event_id,
                                         item_id=item_id))
            await self.send(ws, envelope(T["tts_done"], a.app_id,
                                         self.robot_cid, event_id))
            print("agent     开场播报已下发: %r" % a.greeting)
        except Exception as e:
            print("agent     开场播报失败（不影响对话）: %s" % e)

    async def session(self, ws):
        try:
            await self._session_frames(ws)
        finally:
            if self._asr is not None:
                await self._asr.close()
                self._asr = None

    async def _session_frames(self, ws):
        a = self.args
        audio_buf = bytearray()
        recording_started = None

        async for raw in ws:
            try:
                f = json.loads(raw)
            except ValueError:
                print("agent <- gw  非法 JSON: %s" % trunc(raw))
                continue
            ftype = f.get("type", "")
            if ftype != T["a_append"]:        # append 太吵，不打
                print("agent <- gw  %s" % trunc(raw))

            if ftype == T["sync"]:
                print("agent     会话就绪 state=%s callbackType=%s"
                      % (f.get("state"), f.get("callbackType")))
                self.warm_connections()
                await self.greet(ws)
            elif ftype == T["a_start"]:
                self.warm_connections()
                if self._asr is not None:
                    await self._asr.close()
                audio_buf = bytearray()
                recording_started = time.perf_counter()
                self._asr = (None if a.asr_after_commit else
                             StreamingAsr(a.speech_key, a.asr_resource_id))
            elif ftype == T["a_append"]:
                b64 = f.get("audio", "")
                pcm = base64.b64decode(b64) if b64 else b""
                audio_buf += pcm
                if self._asr is not None and pcm:
                    self._asr.feed(pcm)
            elif ftype == T["a_commit"]:
                ms = len(audio_buf) / 32.0
                print("agent     收到 commit：%d bytes（约 %.0f ms 语音）"
                      % (len(audio_buf), ms))
                if recording_started is not None:
                    print("agent     [录音 start→commit %.0fms]"
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
                print("agent     网关错误: code=%s msg=%s"
                      % (f.get("errorCode"), f.get("errorMsg")))
            elif ftype == "agentsdk.skill_response.state":
                print("agent     技能状态: %s → %s %s"
                      % (f.get("skillName"), f.get("state"),
                         f.get("detail", "")))
            elif ftype == "agentsdk.state_request.meta":
                pass
            else:
                print("agent     （忽略 %s）" % ftype)

    async def run(self):
        try:
            await self._run()
        finally:
            await self.close()

    async def _run(self):
        a = self.args
        uri = "ws://%s:%d%s" % (a.host, a.port, a.path)
        while True:
            headers = build_headers(a.app_id, a.app_key, a.app_secret, a.path)
            try:
                print("agent     连接 %s ..." % uri)
                async with websockets.connect(
                        uri,
                        additional_headers=headers,
                        compression=None,       # Unity 手写 WS 服务端不支持压缩扩展
                        user_agent_header=None,
                        open_timeout=5) as ws:
                    print("agent     握手成功（101 Switching Protocols）")
                    await self.session(ws)
            except websockets.exceptions.InvalidStatus as e:
                print("agent     握手被拒绝: HTTP %s" % e.response.status_code)
                print("agent     （400=握手头不兼容 401=签名错 404=路径错 503=已有会话）")
            except (websockets.exceptions.ConnectionClosed, OSError,
                    asyncio.TimeoutError) as e:
                print("agent     连接断开：%s" % e)
            print("agent     3 秒后重连 ...")
            await asyncio.sleep(3)


def parse_args(argv=None):
    preliminary = argparse.ArgumentParser(add_help=False)
    preliminary.add_argument("--env-file")
    options, _ = preliminary.parse_known_args(argv)
    load_env(options.env_file)
    p = argparse.ArgumentParser(description="X02 豆包真实智能体客户端")
    p.add_argument("--env-file", help="Explicit .env path; existing environment variables take priority")
    p.add_argument("--host", default="localhost")
    p.add_argument("--port", type=int, default=9002)
    p.add_argument("--path", default="/api/V1/open-portal/app/wss/agent-sdk")
    p.add_argument("--app-id", default="demo-app")
    p.add_argument("--app-key", default="demo-key")
    p.add_argument("--app-secret", default="demo-secret")
    p.add_argument("--speech-key", default=os.environ.get("DOUBAO_SPEECH_API_KEY", ""),
                   help="语音控制台 API Key（ASR+TTS），或环境变量 DOUBAO_SPEECH_API_KEY")
    p.add_argument("--ark-key", default=os.environ.get("ARK_API_KEY", ""),
                   help="火山方舟 API Key（LLM），或环境变量 ARK_API_KEY")
    p.add_argument("--llm-model", default=os.environ.get("DOUBAO_LLM_MODEL",
                   "doubao-seed-2-0-mini-260428"))
    p.add_argument("--tts-speaker", default=os.environ.get("DOUBAO_TTS_SPEAKER",
                   "zh_female_wanqudashu_moon_bigtts"))
    p.add_argument("--tts-mode", choices=["bidirectional", "sentence"],
                   default="bidirectional",
                   help="默认双向流式（文本边生成边合成）；sentence 使用逐句合成兼容模式")
    p.add_argument("--asr-resource-id", default=os.environ.get(
        "DOUBAO_ASR_RESOURCE_ID", "volc.bigasr.sauc.duration"))
    p.add_argument("--asr-after-commit", action="store_true",
                   help="对照模式：收到整段录音后才连接 ASR（默认边录边传）")
    p.add_argument("--system-prompt",
                   default="你是人形机器人X2的语音助手，名叫灵犀。"
                           "回答口语化、简洁（一般不超过两句话），不要用列表和markdown。"
                           "你可以通过 robot_skill 工具做动作和表情：动作有挥手"
                           "（wave_hands）、鞠躬（bow）、张开双臂（open_arms）；"
                           "表情有开心（happy）、"
                           "难过（sad）、惊讶（surprised）、生气（angry）、爱心"
                           "（love）。用户表达这类意图时调用工具，同时必须给一句"
                           "简短的口头回应（如'好呀，我这就挥手'），不能只调用工具"
                           "不说话。还可以走（walk）、转（turn）、停（stop）。")
    p.add_argument("--history-turns", type=int, default=5,
                   help="LLM 记忆的对话轮数")
    p.add_argument("--greeting",
                   default="你好，我是灵犀，有什么可以帮您？",
                   help="连接就绪后的开场播报（置空 '' 禁用）")
    p.add_argument("--save-audio", metavar="PATH", default=None,
                   help="保存最后一轮 TTS 音频到 wav")
    p.add_argument("--save-input", metavar="PATH", default=None,
                   help="保存最近一轮上行语音到 wav（诊断用）")
    return p.parse_args(argv)


def main():
    args = parse_args()

    if not args.speech_key:
        raise SystemExit("缺少语音 API Key：填 .env 的 DOUBAO_SPEECH_API_KEY 或 --speech-key")
    if not args.ark_key:
        raise SystemExit("缺少方舟 API Key：填 .env 的 ARK_API_KEY 或 --ark-key")

    asyncio.run(DoubaoAgent(args).run())


if __name__ == "__main__":
    main()
