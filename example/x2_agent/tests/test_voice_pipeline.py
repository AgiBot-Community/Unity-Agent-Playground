import asyncio
import json
import struct
import unittest
from unittest.mock import patch

import httpx
import websockets

from x2_agent import agent
from x2_agent.llm import LlmClient
from x2_agent.tts import BidiTtsClient, parse_event


class Socket:
    def __init__(self):
        self.frames = []
        self.audio = asyncio.Event()

    async def send(self, raw):
        frame = json.loads(raw)
        self.frames.append(frame)
        if frame['type'] == agent.T['tts_delta']:
            self.audio.set()


class Recognized:
    async def finish(self):
        return '测试'


class PipelineTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        args = agent.parse_args(['--greeting='])
        args.ark_key = args.speech_key = 'test-key'
        self.agent = agent.DoubaoAgent(args)
        self.socket = Socket()

    async def asyncTearDown(self):
        await self.agent.close()

    async def run_round(self):
        await asyncio.wait_for(self.agent.agent_round(
            self.socket, {'eventId': 'test-event', 'itemId': 'test-item'}, b'', Recognized()), 2)

    async def test_blocked_wave_explains_authority_without_calling_llm(self):
        self.agent.control_active = False
        class WaveRequest:
            async def finish(self):
                return "你好，向我挥挥手。"
        async def speech(texts):
            async for _ in texts:
                yield b"\x00\x00" * 1600
        def forbidden_llm(*args, **kwargs):
            raise AssertionError("A blocked direct gesture must not ask the model to promise an action")
        with patch.object(self.agent.llm, "stream", forbidden_llm), patch.object(self.agent, "speech_stream", speech):
            await self.agent.agent_round(self.socket, {"eventId": "blocked"}, b"", WaveRequest())
        self.assertFalse(any(f["type"] == agent.T["skill"] for f in self.socket.frames))
        self.assertEqual(self.agent.history[-1]["content"], agent.NO_CONTROL_REPLY)

    async def test_authority_lost_during_llm_prevents_late_wave_command(self):
        self.agent.robot_cid = "agent"
        async def llm(*args, **kwargs):
            self.agent._on_gateway_state({"type": agent.T["session_state"], "revision": 2,
                                          "controlOwnerCid": "console", "audioOwnerCid": "agent"})
            yield "tool_calls", [{"name": "robot_skill", "arguments":
                                 '{"skillType":"gesture","skillName":"wave_hands"}'}]
        async def speech(texts):
            async for _ in texts:
                yield b"\x00\x00" * 1600
        with patch.object(self.agent.llm, "stream", llm), patch.object(self.agent, "speech_stream", speech):
            await self.run_round()
        self.assertFalse(any(f["type"] == agent.T["skill"] for f in self.socket.frames))
        self.assertIn(agent.NO_CONTROL_REPLY, self.agent.history[-1]["content"])

    async def test_permission_updates_during_busy_round_and_ignores_old_revision(self):
        self.agent.args.asr_after_commit = True
        entered, observed = asyncio.Event(), asyncio.Event()
        async def busy_round(*args):
            entered.set()
            while self.agent.control_active:
                await asyncio.sleep(0)
            observed.set()
        class Socket:
            async def __aiter__(self):
                yield json.dumps({"type": agent.T["sync"], "state": "online", "robotCid": "agent",
                                  "controlActive": True, "audioActive": True})
                yield json.dumps({"type": agent.T["a_start"], "eventId": "event"})
                yield json.dumps({"type": agent.T["a_commit"], "eventId": "event"})
                await entered.wait()
                yield json.dumps({"type": agent.T["session_state"], "revision": 2,
                                  "controlOwnerCid": "console", "audioOwnerCid": "agent"})
                yield json.dumps({"type": agent.T["session_state"], "revision": 1,
                                  "controlOwnerCid": "agent", "audioOwnerCid": "agent"})
                await observed.wait()
        with patch.object(self.agent, "agent_round", busy_round), \
                patch.object(self.agent, "warm_connections", lambda: None):
            await asyncio.wait_for(self.agent.session(Socket()), 2)
        self.assertTrue(observed.is_set())
        self.assertFalse(self.agent.control_active)

    def test_direct_gesture_feedback_does_not_match_negation_or_discussion(self):
        for text in ("嗯，挥挥手吧。", "你好，向我挥手。", "请张开你的双臂"):
            self.assertTrue(agent.is_direct_gesture_request(text), text)
        for text in ("不要挥手", "挥手是什么意思", "先挥手再坐下", "请说挥手两个字"):
            self.assertFalse(agent.is_direct_gesture_request(text), text)

    async def test_audio_before_llm_finishes_and_remaining_text_keeps_flowing(self):
        llm_finished = asyncio.Event()
        observed = []

        async def llm(*args, **kwargs):
            yield 'text', '你好，'
            await self.socket.audio.wait()
            yield 'text', '这是第二句。'
            llm_finished.set()

        async def speech(texts):
            async for text in texts:
                observed.append(text)
                if len(observed) == 1:
                    yield b'\x01\x00' * 3200
                    await llm_finished.wait()  # would deadlock with serial LLM/TTS
                else:
                    yield b'\x02\x00' * 3200

        with patch.object(self.agent.llm, 'stream', llm), patch.object(self.agent, 'speech_stream', speech):
            await self.run_round()
        self.assertEqual(observed, ['你好，', '这是第二句。'])
        self.assertEqual(self.agent.history[-1]['content'], ''.join(observed))
        self.assertEqual(self.socket.frames[-1]['type'], agent.T['tts_done'])

    async def test_tts_failure_cancels_stalled_llm_and_finishes_audio_round(self):
        llm_closed = asyncio.Event()

        async def llm(*args, **kwargs):
            try:
                yield 'text', '你好'
                await asyncio.Event().wait()
            finally:
                llm_closed.set()

        async def speech(texts):
            async for _ in texts:
                raise RuntimeError('synthesis failed')
                yield b''

        with patch.object(self.agent.llm, 'stream', llm), patch.object(self.agent, 'speech_stream', speech):
            with self.assertLogs(agent.logger, level="ERROR") as logged:
                await self.run_round()
        self.assertTrue(any("synthesis failed" in line for line in logged.output))
        self.assertTrue(llm_closed.is_set())
        self.assertEqual(self.agent.history, [])
        self.assertTrue(any(f.get('errorCode') == 3301 for f in self.socket.frames))
        self.assertEqual(self.socket.frames[-1]['type'], agent.T['tts_done'])

    async def test_tool_only_reply_still_dispatches_and_speaks(self):
        spoken = []

        async def llm(*args, **kwargs):
            yield 'tool_calls', [{'name': 'robot_skill', 'arguments':
                                  '{"skillType":"gesture","skillName":"wave_hands"}'}]

        async def speech(texts):
            async for text in texts:
                spoken.append(text)
                yield b'\x00\x01' * 3200

        with patch.object(self.agent.llm, 'stream', llm), patch.object(self.agent, 'speech_stream', speech):
            await self.run_round()
        self.assertTrue(spoken)
        self.assertTrue(any(f.get('skillName') == 'wave_hands' for f in self.socket.frames))
        self.assertEqual(self.agent.history[-1]['content'], ''.join(spoken))

    async def test_invalid_tool_corrects_existing_text_and_never_sends_skill(self):
        spoken = []

        async def llm(*args, **kwargs):
            yield "text", "好的，我这就挥手。"
            yield "tool_calls", [{"name": "robot_skill", "arguments":
                                 '{"skillType":"emotion","skillName":"wave_hands"}'}]

        async def speech(texts):
            async for text in texts:
                spoken.append(text)
                yield b"\x00\x00" * 100

        with patch.object(self.agent.llm, "stream", llm), patch.object(self.agent, "speech_stream", speech):
            await self.run_round()
        self.assertFalse(any(frame["type"] == agent.T["skill"] for frame in self.socket.frames))
        self.assertTrue(any(frame.get("errorCode") == 4092 for frame in self.socket.frames))
        self.assertEqual(spoken[-1], "动作参数无效，这次动作不会执行。")
        self.assertEqual(self.agent.history[-1]["content"], "".join(spoken))

    async def test_partial_greeting_failure_reports_error_and_closes_round(self):
        self.agent.args.greeting = 'hello'
        closed = asyncio.Event()

        async def speech(texts):
            try:
                yield b'\x00\x01' * 1600
                raise RuntimeError('partial greeting failed')
            finally:
                closed.set()

        with patch.object(self.agent, 'speech_stream', speech):
            await self.agent.greet(self.socket)
        self.assertTrue(closed.is_set())
        self.assertTrue(any(f['type'] == agent.T['tts_delta'] for f in self.socket.frames))
        self.assertTrue(any(f.get('errorCode') == 3301 for f in self.socket.frames))
        self.assertEqual([f['type'] for f in self.socket.frames[-2:]],
                         [agent.T['tts_done_item'], agent.T['tts_done']])
        self.assertEqual(len({f['eventId'] for f in self.socket.frames}), 1)

    async def test_cancelled_greeting_closes_generator_and_audio_round(self):
        self.agent.args.greeting = 'hello'
        closed = asyncio.Event()

        async def speech(texts):
            try:
                yield b'\x00\x01' * 1600
                await asyncio.Event().wait()
            finally:
                closed.set()

        with patch.object(self.agent, 'speech_stream', speech):
            task = asyncio.create_task(self.agent.greet(self.socket))
            await asyncio.wait_for(self.socket.audio.wait(), 1)
            task.cancel()
            with self.assertRaises(asyncio.CancelledError):
                await task
        self.assertTrue(closed.is_set())
        self.assertEqual(self.socket.frames[-1]['type'], agent.T['tts_done'])

    async def test_standby_agent_does_not_greet_or_warm_cloud_connections(self):
        from unittest.mock import AsyncMock, Mock
        class Socket:
            async def __aiter__(self):
                yield json.dumps({'type': agent.T['sync'], 'state': 'online', 'robotCid': 'standby',
                                  'audioActive': False, 'controlActive': False})
        with patch.object(self.agent, 'greet', new_callable=AsyncMock) as greet, \
                patch.object(self.agent, 'warm_connections', new_callable=Mock) as warm:
            await self.agent.session(Socket())
            greet.assert_not_called()
            warm.assert_not_called()
            self.assertFalse(self.agent.control_active)


class TransportTests(unittest.IsolatedAsyncioTestCase):
    async def test_bidirectional_audio_before_text_finishes_and_connection_reuse(self):
        connections, sessions, received = [], [], []

        def frame(event, sid, payload, audio=False):
            sid = sid.encode()
            body = payload if audio else json.dumps(payload).encode()
            header = bytes([0x11, 0xb4 if audio else 0x94, 0 if audio else 0x10, 0])
            return header + struct.pack('>II', event, len(sid)) + sid + struct.pack('>I', len(body)) + body

        async def server(ws):
            connections.append(1)
            await ws.recv()
            await ws.send(frame(50, 'connection', {}))
            async for raw in ws:
                _, event, sid, payload = parse_event(raw)
                if event == 100:
                    sessions.append(sid)
                    await ws.send(frame(150, sid, {}))
                elif event == 200:
                    received.append(payload['req_params']['text'])
                    await ws.send(frame(352, sid, b'\x01\x00' * 3200, audio=True))
                elif event == 102:
                    await ws.send(frame(152, sid, {}))

        async with websockets.serve(server, '127.0.0.1', 0) as listener:
            url = 'ws://127.0.0.1:%d' % listener.sockets[0].getsockname()[1]
            with patch.dict("os.environ", {"TTS_WS_URL": url}):
                client = BidiTtsClient('test', 'test', endpoint=url)
                try:
                    for _ in range(2):
                        audio = asyncio.Event()
                        async def texts():
                            yield 'first'
                            await asyncio.wait_for(audio.wait(), 1)
                            yield 'second'
                        count = 0
                        async for pcm in client.stream(texts()):
                            count += 1
                            audio.set()
                        self.assertEqual(count, 2)
                    self.assertEqual(len(connections), 1)
                    self.assertEqual(len(set(sessions)), 2)
                    self.assertEqual(received, ['first', 'second', 'first', 'second'])
                finally:
                    await client.close()

    async def test_llm_keeps_real_http_connection_between_turns(self):
        connections, requests = [], []

        async def handle(reader, writer):
            connections.append(1)
            try:
                while True:
                    header = await reader.readuntil(b'\r\n\r\n')
                    length = next(int(line.split(b':')[1]) for line in header.split(b'\r\n')
                                  if line.lower().startswith(b'content-length:'))
                    requests.append(json.loads(await reader.readexactly(length)))
                    body = b'data: {"choices":[{"delta":{"content":"hello"}}]}\n\ndata: [DONE]\n\n'
                    writer.write(b'HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: ' +
                                 str(len(body)).encode() + b'\r\n\r\n' + body)
                    await writer.drain()
            except asyncio.IncompleteReadError:
                pass
            finally:
                writer.close()
                await writer.wait_closed()

        client = LlmClient()
        try:
            async with await asyncio.start_server(handle, '127.0.0.1', 0) as server:
                url = 'http://127.0.0.1:%d/chat' % server.sockets[0].getsockname()[1]
                client.endpoint = url
                with patch.dict("os.environ", {"ARK_API_URL": url}):
                    for _ in range(2):
                        self.assertEqual([x async for x in client.stream('s', [], 'm', 'test')],
                                         [('text', 'hello')])
                    await client.close()
            self.assertEqual(len(connections), 1)
            self.assertEqual(len(requests), 2)
            self.assertTrue(all(r['thinking']['type'] == 'disabled' for r in requests))
        finally:
            await client.close()

    async def test_unrelated_http_400_is_not_retried_with_thinking_enabled(self):
        calls = []

        def respond(request):
            calls.append(request)
            return httpx.Response(400, json={'error': 'invalid model'})

        client = LlmClient()
        await client.http.aclose()
        client.http = httpx.AsyncClient(transport=httpx.MockTransport(respond))
        try:
            with self.assertRaisesRegex(RuntimeError, 'invalid model'):
                _ = [x async for x in client.stream('s', [], 'm', 'test')]
            self.assertEqual(len(calls), 1)
        finally:
            await client.close()

    def test_tts_error_frame_preserves_error_message(self):
        import struct
        message = b'{"message":"not authorized"}'
        raw = b'\x11\xf0\x10\x00' + struct.pack('>II', 45000000, len(message)) + message
        with self.assertRaisesRegex(RuntimeError, 'not authorized'):
            parse_event(raw)


if __name__ == '__main__':
    unittest.main()
