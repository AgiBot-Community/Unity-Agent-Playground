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
            await self.run_round()
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
            with patch('x2_agent.tts.URL', url):
                client = BidiTtsClient('test', 'test')
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
                with patch('x2_agent.llm.URL', url):
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
