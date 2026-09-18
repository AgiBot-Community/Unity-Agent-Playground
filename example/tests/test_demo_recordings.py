"""Upstream recordings and skills remain usable after the example move."""
import base64
import json
from pathlib import Path
import re
from types import SimpleNamespace
import unittest
import wave

from x2_agent import agent, demo


class RecordingTests(unittest.TestCase):
    def test_bundled_recordings_load_from_package_location(self):
        for path in (demo.GREETING_WAV, demo.REPLY_WAV):
            with self.subTest(path=path), wave.open(path) as recording:
                self.assertEqual(
                    (recording.getframerate(), recording.getnchannels(), recording.getsampwidth()),
                    (16000, 1, 2),
                )
                expected = recording.readframes(recording.getnframes())
                self.assertGreater(len(expected), 6400)
                self.assertEqual(demo.load_pcm(path, 100, 440), expected)

    def test_missing_or_invalid_recording_falls_back_to_tone(self):
        for path in (str(Path(__file__).with_suffix('.missing.wav')), __file__):
            with self.subTest(path=path):
                self.assertEqual(demo.load_pcm(path, 100, 440), demo.sine_pcm(100, 440))

    def test_agent_skills_match_unity_source_catalog(self):
        catalog = (Path(__file__).resolve().parents[2] / 'unity-project/Assets/'
                   'X02Competition/Robot/Skills/SkillCatalog.cs').read_text(encoding='utf-8')
        expected = set(re.findall(r'SkillName = "([^"]+)"', catalog))
        actual = agent.SKILL_TOOLS[0]['function']['parameters']['properties']['skillName']['enum']
        self.assertEqual(set(actual), expected)


class PlaybackTests(unittest.IsolatedAsyncioTestCase):
    async def test_greeting_and_reply_stream_complete_recordings_in_order(self):
        client = demo.AgentDemo(SimpleNamespace(
            app_id='demo-app', reply='reply caption', greeting='greeting caption',
            tts_ms=100, tts_freq=440,
        ))
        class Socket:
            def __init__(self):
                self.frames = []

            async def send(self, text):
                self.frames.append(json.loads(text))

        for greeting, path in ((True, demo.GREETING_WAV), (False, demo.REPLY_WAV)):
            with self.subTest(greeting=greeting):
                socket = Socket()
                if greeting:
                    await client.greet(socket)
                else:
                    await client.agent_round(socket, {'eventId': 'recording-test', 'itemId': 'item'})
                audio = [f for f in socket.frames if f['type'] == demo.T['tts_delta']]
                self.assertTrue(audio)
                chunks = [base64.b64decode(f['audio']) for f in audio]
                self.assertTrue(all(len(chunk) == 6400 for chunk in chunks[:-1]))
                self.assertLessEqual(len(chunks[-1]), 6400)
                self.assertEqual([f['audioLen'] for f in audio], [len(chunk) for chunk in chunks])
                with wave.open(path) as recording:
                    self.assertEqual(b''.join(chunks), recording.readframes(recording.getnframes()))
                self.assertEqual([f['type'] for f in socket.frames[-2:]],
                                 [demo.T['tts_done_item'], demo.T['tts_done']])
                llm_done = next(i for i, f in enumerate(socket.frames) if f['type'] == demo.T['llm_done'])
                first_audio = next(i for i, f in enumerate(socket.frames) if f['type'] == demo.T['tts_delta'])
                self.assertLess(llm_done, first_audio)
