"""Offline Unity gateway demo client."""
import argparse
import asyncio
import base64
import json
import math
import struct
import time
import uuid
import websockets
from .audio import save_wav, trunc
from .gateway import T, build_headers, envelope

def sine_pcm(ms, freq=440, rate=16000, amp=0.35):
    """生成 16k/16bit/mono 正弦波 PCM（模拟 TTS 音频）。"""
    n = int(rate * ms / 1000)
    return b"".join(
        struct.pack("<h", int(amp * 32767 * math.sin(2 * math.pi * freq * i / rate)))
        for i in range(n))




# ----------------------------------------------------------------------------
# Agent 逻辑
# ----------------------------------------------------------------------------

class AgentDemo:
    def __init__(self, args):
        self.args = args
        self.robot_cid = "cid-agent-demo"

    async def send(self, ws, obj):
        text = json.dumps(obj, ensure_ascii=False, separators=(",", ":"))
        print("agent -> gw  %s" % trunc(text))
        await ws.send(text)

    async def agent_round(self, ws, req):
        """收到 commit 后按官方时序回一轮：asr final → llm → tts。"""
        a = self.args
        event_id = req.get("eventId", "")
        item_id = req.get("itemId") or ("item-" + uuid.uuid4().hex[:8])
        text = a.reply

        # 1) ASR 最终结果
        await self.send(ws, envelope(T["asr_final"], a.app_id, self.robot_cid,
                                     event_id, text=text))
        # 2) LLM 流式
        await self.send(ws, envelope(T["llm_delta"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id, text=text))
        await self.send(ws, envelope(T["llm_done_item"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id))
        await self.send(ws, envelope(T["llm_done"], a.app_id, self.robot_cid, event_id))
        # 3) TTS 音频（正弦波模拟）
        pcm = sine_pcm(a.tts_ms, a.tts_freq)
        await self.send(ws, envelope(T["tts_delta"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id,
                                     audio=base64.b64encode(pcm).decode("ascii"),
                                     audioLen=len(pcm)))
        await self.send(ws, envelope(T["tts_done_item"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id))
        await self.send(ws, envelope(T["tts_done"], a.app_id, self.robot_cid, event_id))
        print("agent     本轮应答完成（TTS %d ms / %d bytes）" % (a.tts_ms, len(pcm)))

    async def greet(self, ws):
        """连接就绪后的开场播报（llm 字幕 + 正弦波模拟 TTS）。"""
        a = self.args
        event_id = "evt-greet-" + uuid.uuid4().hex[:8]
        item_id = "item-greet-" + uuid.uuid4().hex[:8]
        await self.send(ws, envelope(T["llm_delta"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id, text=a.greeting))
        await self.send(ws, envelope(T["llm_done_item"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id))
        await self.send(ws, envelope(T["llm_done"], a.app_id, self.robot_cid,
                                     event_id))
        pcm = sine_pcm(1200, freq=880)   # 1.2s 短提示音模拟播报
        await self.send(ws, envelope(T["tts_delta"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id,
                                     audio=base64.b64encode(pcm).decode("ascii"),
                                     audioLen=len(pcm)))
        await self.send(ws, envelope(T["tts_done_item"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id))
        await self.send(ws, envelope(T["tts_done"], a.app_id, self.robot_cid,
                                     event_id))
        print("agent     开场播报已下发: %r" % a.greeting)

    async def session(self, ws):
        a = self.args
        audio_buf = bytearray()
        sent_extras = False

        async for raw in ws:
            try:
                f = json.loads(raw)
            except ValueError:
                print("agent <- gw  非法 JSON: %s" % trunc(raw))
                continue
            ftype = f.get("type", "")
            print("agent <- gw  %s" % trunc(raw))

            if ftype == T["sync"]:
                print("agent     会话就绪 state=%s callbackType=%s agentMeta=%s" % (
                    f.get("state"), f.get("callbackType"), f.get("agentMeta")))
                if not sent_extras:
                    sent_extras = True
                    if a.greeting:
                        await self.greet(ws)
                    if a.skill:
                        st, sn = a.skill.split("/", 1)
                        await self.send(ws, envelope(
                            T["skill"], a.app_id, self.robot_cid,
                            "evt-" + uuid.uuid4().hex[:8],
                            skillType=st, skillName=sn, skillParam={}))
                    if a.interrupt:
                        await self.send(ws, envelope(
                            T["interrupt"], a.app_id, self.robot_cid,
                            "evt-" + uuid.uuid4().hex[:8],
                            interruptType=a.interrupt))
            elif ftype == T["a_start"]:
                audio_buf = bytearray()
                print("agent     开始收音 eventId=%s itemId=%s" % (
                    f.get("eventId"), f.get("itemId", "")))
            elif ftype == T["a_append"]:
                b64 = f.get("audio", "")
                audio_buf += base64.b64decode(b64) if b64 else b""
            elif ftype == T["a_commit"]:
                ms = len(audio_buf) / 32.0  # 16k*2byte → 32 bytes/ms
                print("agent     收到 commit：本轮上行 %d bytes（约 %.0f ms）" % (
                    len(audio_buf), ms))
                if a.save_audio and audio_buf:
                    save_wav(a.save_audio, audio_buf)
                    print("agent     已保存上行音频 → %s" % a.save_audio)
                await self.agent_round(ws, f)
            elif ftype == T["state"]:
                print("agent     机器人状态: %s = %s" % (
                    f.get("stateName"), f.get("stateValue")))
            elif ftype == T["error"]:
                print("agent     错误: code=%s msg=%s" % (
                    f.get("errorCode"), f.get("errorMsg")))
            else:
                print("agent     （忽略 %s）" % ftype)

    async def run(self):
        a = self.args
        uri = "ws://%s:%d%s" % (a.host, a.port, a.path)
        while True:
            headers = build_headers(a.app_id, a.app_key, a.app_secret, a.path,
                                    bad_sig=a.bad_sig)
            try:
                print("agent     连接 %s ..." % uri)
                async with websockets.connect(
                        uri,
                        additional_headers=headers,
                        compression=None,       # 关键：禁用 permessage-deflate，Unity Mono HttpListener 不支持该扩展（否则 400）
                        user_agent_header=None,  # 对齐 .NET ClientWebSocket，减少握手头差异
                        open_timeout=5) as ws:
                    print("agent     握手成功（101 Switching Protocols）")
                    await self.session(ws)
            except websockets.exceptions.InvalidStatus as e:
                body = ""
                try:
                    raw = getattr(e.response, "body", None)
                    if raw:
                        body = raw.decode("utf-8", "replace").strip()
                except Exception:
                    pass
                print("agent     握手被拒绝: HTTP %s%s"
                      % (e.response.status_code, (" | " + body) if body else ""))
                print("agent     （400=握手头不兼容 401=签名错 404=路径错 503=已有会话）")
                if a.bad_sig:
                    print("agent     --bad-sig 模式：预期被 401 拒绝，联调通过")
                    return
                print("agent     3 秒后重连 ...")
                await asyncio.sleep(3)
            except (websockets.exceptions.ConnectionClosed, OSError,
                    asyncio.TimeoutError) as e:
                print("agent     连接断开：%s" % e)
                if a.bad_sig:
                    return
                print("agent     3 秒后重连（模拟 SDK 行为）...")
                await asyncio.sleep(3)


def main():
    p = argparse.ArgumentParser(description="X02 竞赛 Mock 网关联调客户端")
    p.add_argument("--host", default="localhost")
    p.add_argument("--port", type=int, default=9002)
    p.add_argument("--path", default="/api/V1/open-portal/app/wss/agent-sdk")
    p.add_argument("--app-id", default="demo-app")
    p.add_argument("--app-key", default="demo-key")
    p.add_argument("--app-secret", default="demo-secret")
    p.add_argument("--reply", default="你好，我是灵犀，很高兴认识你。",
                   help="收到机器人语音后回复的 ASR/LLM 文本")
    p.add_argument("--tts-ms", type=int, default=1200, help="TTS 正弦波时长（毫秒）")
    p.add_argument("--tts-freq", type=int, default=440, help="TTS 正弦波频率（Hz）")
    p.add_argument("--save-audio", metavar="PATH",
                   help="把机器人上行语音保存为 wav，如 out.wav")
    p.add_argument("--skill", metavar="TYPE/NAME", default=None,
                   help="sync 后主动下发技能帧，如 gesture/wave_hands")
    p.add_argument("--interrupt", metavar="TYPE", default=None,
                   help="sync 后主动下发打断帧，如 chat")
    p.add_argument("--greeting", default="你好，我是灵犀，有什么可以帮您？",
                   help="连接就绪后的开场播报文本（置空 '' 禁用）")
    p.add_argument("--bad-sig", action="store_true", help="故意发错误签名（验证 401）")
    args = p.parse_args()

    print("=" * 62)
    print("X02 竞赛 Mock 网关联调客户端")
    print("目标: ws://%s:%d%s" % (args.host, args.port, args.path))
    print("凭证: %s / %s  (StrictAuth 见 Unity Inspector)" % (args.app_id, args.app_key))
    print("=" * 62)
    try:
        asyncio.run(AgentDemo(args).run())
    except KeyboardInterrupt:
        print("\nagent     手动退出")


if __name__ == "__main__":
    main()
