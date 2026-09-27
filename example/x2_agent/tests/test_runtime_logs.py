import asyncio
import contextlib
import io
import json
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import websockets

from x2_agent.agent import DoubaoAgent
from x2_agent.demo import AgentDemo
from x2_agent.gateway import T, gateway_messages, print_runtime_log


class RuntimeLogTests(unittest.IsolatedAsyncioTestCase):
    def test_levels_stack_and_loss_metadata_are_visible(self):
        for level in ('info', 'warning', 'error'):
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertTrue(print_runtime_log({
                    'type': T['runtime_log'], 'level': level, 'message': 'test message',
                    'stackTrace': 'Stack: method:42', 'timestampMs': 123, 'sequence': 7,
                    'droppedCount': 4, 'truncated': True,
                }))
            self.assertIn(level.upper(), output.getvalue())
            self.assertIn('Stack: method:42', output.getvalue())
            self.assertIn('4', output.getvalue())
        self.assertFalse(print_runtime_log({'type': T['a_start']}))

    async def test_both_clients_receive_logs_during_busy_greeting(self):
        # The server waits for log consumption before greeting completion.
        # A receiver blocked by greet() would deadlock this test.
        for client_type in (DoubaoAgent, AgentDemo):
            with self.subTest(client=client_type.__name__):
                started, received = asyncio.Event(), asyncio.Event()
                args = SimpleNamespace(speech_key='', tts_speaker='', greeting='hello',
                                       skill=None, interrupt=None, app_id='test')
                client = client_type(args)
                async def greet(ws):
                    started.set()
                    await received.wait()
                async def server(ws):
                    await ws.send(json.dumps({'type': T['sync'], 'state': 'online'}))
                    await started.wait()
                    await ws.send(json.dumps({'type': T['runtime_log'],
                                              'message': 'while greeting', 'level': 'warning'}))
                    await received.wait()
                def handle(frame):
                    if frame.get('type') == T['runtime_log']:
                        received.set()
                        return True
                    return False
                try:
                    with patch.object(client, 'greet', greet), patch(
                            'x2_agent.gateway.print_runtime_log', handle):
                        if isinstance(client, DoubaoAgent):
                            client.warm_connections = lambda: None
                        async with websockets.serve(server, '127.0.0.1', 0) as listener:
                            uri = 'ws://127.0.0.1:%d' % listener.sockets[0].getsockname()[1]
                            async with websockets.connect(uri) as ws:
                                await asyncio.wait_for(client.session(ws), 2)
                    self.assertTrue(received.is_set())
                finally:
                    if isinstance(client, DoubaoAgent):
                        await client.close()

    async def test_reader_preserves_audio_order_and_cancels_on_early_exit(self):
        class Socket:
            closed = False
            async def __aiter__(self):
                try:
                    for kind in (T['a_start'], T['runtime_log'], T['a_append'], T['a_commit']):
                        yield json.dumps({'type': kind, 'message': 'diagnostic'})
                    await asyncio.Event().wait()
                finally:
                    self.closed = True
        ws = Socket()
        with contextlib.redirect_stdout(io.StringIO()):
            async with gateway_messages(ws) as messages:
                types = []
                async for _, frame in messages:
                    types.append(frame['type'])
                    if len(types) == 3:
                        break
        self.assertEqual(types, [T['a_start'], T['a_append'], T['a_commit']])
        self.assertTrue(ws.closed)
