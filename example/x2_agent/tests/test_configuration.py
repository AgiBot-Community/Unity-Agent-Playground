import asyncio
import json
import logging
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import yaml

from x2_agent import agent, asr, error_codes
from x2_agent.agent_config import AgentConfig
from x2_agent.logging_config import setup_logger
from x2_agent.skill_loader import SkillLoader


class ConfigurationTests(unittest.IsolatedAsyncioTestCase):
    async def test_env_file_endpoints_reach_clients_and_cli_takes_precedence(self):
        with tempfile.TemporaryDirectory() as folder:
            config = Path(folder) / "settings.env"
            config.write_text(
                "ASR_WS_URL=ws://127.0.0.1:19001/asr\n"
                "ARK_API_URL=http://127.0.0.1:19002/llm\n"
                "TTS_WS_URL=ws://127.0.0.1:19003/tts\n"
                "TTS_SENTENCE_WS_URL=ws://127.0.0.1:19004/sentence\n",
                encoding="utf-8")
            with patch.dict(os.environ, {}, clear=True):
                args = agent.parse_args([
                    "--env-file", str(config), "--speech-key", "test",
                    "--ark-key", "test", "--ark-api-url", "http://127.0.0.1:19005/cli",
                ])
                client = agent.DoubaoAgent(AgentConfig.from_args(args))
                try:
                    self.assertEqual(client.llm.endpoint, "http://127.0.0.1:19005/cli")
                    self.assertEqual(client.tts.endpoint, "ws://127.0.0.1:19003/tts")
                    self.assertEqual(client.args.tts_sentence_ws_url, "ws://127.0.0.1:19004/sentence")
                    with patch.object(asr.websockets, "connect", side_effect=OSError("probe")) as connect:
                        with self.assertRaisesRegex(OSError, "probe"):
                            await asr.asr_transcribe(b"\x00\x00", "test", "resource", args.asr_ws_url)
                        self.assertEqual(connect.call_args.args[0], "ws://127.0.0.1:19001/asr")
                finally:
                    await client.close()

    async def test_custom_skill_schema_ack_and_metrics_are_used_by_agent(self):
        config = {
            "version": "1.0",
            "skill_categories": {"gesture": {"skills": [{"name": "wave_hands", "parameters": []}]}},
            "acknowledgments": {"gesture/wave_hands": "custom acknowledgment"},
        }
        with tempfile.TemporaryDirectory() as folder:
            skills = Path(folder) / "skills.yaml"
            skills.write_text(yaml.safe_dump(config), encoding="utf-8")
            metrics = Path(folder) / "metrics.json"
            args = agent.parse_args(["--skills-file", str(skills), "--metrics-file", str(metrics)])
            args.speech_key = args.ark_key = "test"
            client = agent.DoubaoAgent(args)
            frames, spoken = [], []

            class Socket:
                async def send(self, raw):
                    frames.append(json.loads(raw))

            class Asr:
                async def finish(self):
                    return "wave"

            async def llm(*args, **kwargs):
                names = kwargs["tools"][0]["function"]["parameters"]["properties"]["skillName"]["enum"]
                self.assertEqual(names, ["wave_hands"])
                yield "tool_calls", [{"name": "robot_skill", "arguments":
                                     '{"skillType":"gesture","skillName":"wave_hands"}'}]

            async def speech(texts):
                async for text in texts:
                    spoken.append(text)
                    yield b"\x00\x00" * 100

            try:
                with patch.object(client.llm, "stream", llm), patch.object(client, "speech_stream", speech):
                    await asyncio.wait_for(client.agent_round(Socket(), {"eventId": "custom"}, b"", Asr()), 3)
                self.assertEqual(spoken, ["custom acknowledgment"])
                self.assertTrue(any(frame.get("skillName") == "wave_hands" for frame in frames))
            finally:
                await client.close()
            summary = json.loads(metrics.read_text(encoding="utf-8"))
            self.assertEqual(summary["counters"]["skill_executed_count"], 1)
            self.assertEqual(summary["counters"]["tts_success_count"], 1)
            self.assertEqual(summary["round_total_duration_ms"]["count"], 1)

    def test_invalid_configuration_is_rejected_and_secrets_are_not_in_repr(self):
        for overrides in ({"port": 0}, {"history_turns": -1}, {"asr_ws_url": "https://wrong"},
                          {"port": True}, {"tts_mode": "unknown"}):
            with self.subTest(overrides=overrides), self.assertRaises(ValueError):
                AgentConfig("speech-secret", "ark-secret", **overrides)
        config = AgentConfig("speech-secret", "ark-secret", app_secret="gateway-secret")
        for secret in ("speech-secret", "ark-secret", "gateway-secret"):
            self.assertNotIn(secret, repr(config))
            self.assertNotIn(secret, json.dumps(config.to_dict()))

    def test_logger_reconfiguration_writes_new_file_without_duplicate_output(self):
        name = "x2_agent.tests.configuration"
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "agent.log"
            logger = setup_logger(name)
            logger = setup_logger(name, "WARNING", str(path))
            try:
                logger.info("filtered")
                logger.warning("visible")
                for handler in logger.handlers:
                    handler.flush()
                text = path.read_text(encoding="utf-8")
                self.assertNotIn("filtered", text)
                self.assertEqual(text.count("visible"), 1)
                self.assertFalse(logger.propagate)
            finally:
                for handler in list(logger.handlers):
                    logger.removeHandler(handler)
                    handler.close()

    def test_error_codes_preserve_existing_wire_values(self):
        self.assertEqual(error_codes.ERR_LLM_FAILED, 3201)
        self.assertEqual(error_codes.ERR_TTS_FAILED, 3301)
        self.assertEqual(error_codes.TtsError("failed").code, 3301)
        self.assertFalse(error_codes.is_recoverable_error(error_codes.TtsError("invalid connection config")))
        self.assertTrue(error_codes.is_recoverable_error(error_codes.TtsError("timeout", TimeoutError())))


class SkillConfigurationTests(unittest.TestCase):
    def test_rejected_config_is_not_cached(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "skills.yaml"
            path.write_text("version: '1.0'\nskill_categories: []\n", encoding="utf-8")
            loader = SkillLoader(str(path))
            for _ in range(2):
                with self.assertRaises(ValueError):
                    loader.load()

    def test_skill_pairs_and_non_finite_parameters_are_rejected(self):
        loader = SkillLoader()
        for category, name, params in (
            ("emotion", "walk", {}), ("movement", "walk", {"distanceM": "NaN"}),
            ("movement", "turn", {"angleDeg": float("inf")}),
            ("movement", "walk", {"distanceM": True}), ("movement", "walk", []),
        ):
            with self.subTest(name=name, params=params), self.assertRaises(ValueError):
                loader.validate_request(category, name, params)
        self.assertEqual(loader.validate_request("movement", "walk", {"distanceM": 15}), {"distanceM": 5})
        self.assertEqual(loader.validate_request("emotion", "happy", {"durationMs": 0}), {"durationMs": 0})
        self.assertEqual(loader.get_acknowledgment("movement", "walk"), "好，我往前走1米。")

    def test_yaml_parameter_limits_reach_tool_schema(self):
        schema = SkillLoader().get_tool_definition()[0]["function"]["parameters"]
        parameters = schema["properties"]["skillParam"]["properties"]
        self.assertEqual(parameters["distanceM"]["minimum"], 0.2)
        self.assertEqual(parameters["distanceM"]["maximum"], 5)
        self.assertNotIn("minimum", parameters["durationMs"])
