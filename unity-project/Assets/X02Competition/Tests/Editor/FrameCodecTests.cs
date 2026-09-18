using System.Collections.Generic;
using NUnit.Framework;
using X02Competition.Protocol;

namespace X02Competition.Tests.Editor
{
    /// <summary>
    /// 协议帧编解码测试：字段名与官方 SDK v1.4.0 源码逐一对齐（golden 断言）。
    /// 依据：LinkSoul协议提取-v1.4.0.md §2~§4。
    /// </summary>
    public class FrameCodecTests
    {
        // ---------------- 出站（网关→Agent） ----------------

        [Test]
        public void BuildRobotStateSync_ContainsAllFields()
        {
            var meta = new Dictionary<string, object>
            {
                ["def.wakeupWord"] = "灵犀",
                ["def.robotName"] = "X2",
            };
            var json = FrameCodec.BuildRobotStateSync("agent-001", "cid-1", "online", "audio2tts", meta);
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.AreEqual("agentsdk.robot_state.sync", (string)o["type"]);
            Assert.AreEqual("agent-001", (string)o["agentId"]);
            Assert.AreEqual("online", (string)o["state"]);
            Assert.AreEqual("audio2tts", (string)o["callbackType"]);
            Assert.AreEqual("灵犀", (string)o["agentMeta"]["def.wakeupWord"]);
            Assert.AreEqual("X2", (string)o["agentMeta"]["def.robotName"]);
        }

        [Test]
        public void BuildRobotStateSync_Offline_HasNoAgentMeta()
        {
            var json = FrameCodec.BuildRobotStateSync("a", "c", "offline", "audio2tts", null);
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            Assert.AreEqual("offline", (string)o["state"]);
            Assert.IsNull(o["agentMeta"]);
        }

        [Test]
        public void BuildAudioRequestStart_WithItemId_OnlyForAudio2Tts()
        {
            var with = FrameCodec.BuildAudioRequestStart("a", "c", "evt-1", "item-1", true);
            var without = FrameCodec.BuildAudioRequestStart("a", "c", "evt-1", "item-1", false);

            var o1 = Newtonsoft.Json.Linq.JObject.Parse(with);
            Assert.AreEqual("agentsdk.audio_request.start", (string)o1["type"]);
            Assert.AreEqual("evt-1", (string)o1["eventId"]);
            Assert.AreEqual("item-1", (string)o1["itemId"]);
            Assert.AreEqual("c", (string)o1["robotCid"]);

            var o2 = Newtonsoft.Json.Linq.JObject.Parse(without);
            Assert.IsNull(o2["itemId"]); // 对齐 SDK：非 audio2tts 的 start 不带 itemId
        }

        [Test]
        public void BuildAudioRequestAppend_AudioIsBase64_AndRoundtrips()
        {
            var pcm = new byte[] { 0x01, 0x02, 0xFF, 0xFE, 0x7F, 0x80 };
            var json = FrameCodec.BuildAudioRequestAppend("a", "c", "evt-1", "item-1", pcm);
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.AreEqual("agentsdk.audio_request.append", (string)o["type"]);
            Assert.AreEqual(6, (int)o["audioLen"]);
            var decoded = System.Convert.FromBase64String((string)o["audio"]);
            Assert.AreEqual(pcm, decoded);
        }

        [Test]
        public void BuildAudioRequestCommit_HasEventIdAndItemId()
        {
            var json = FrameCodec.BuildAudioRequestCommit("a", "c", "evt-9", "item-9");
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            Assert.AreEqual("agentsdk.audio_request.commit", (string)o["type"]);
            Assert.AreEqual("evt-9", (string)o["eventId"]);
            Assert.AreEqual("item-9", (string)o["itemId"]);
        }

        [Test]
        public void BuildStateRequestMeta_StateValueIsString()
        {
            var json = FrameCodec.BuildStateRequestMeta("a", "c", "evt-s", "power", "ok");
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            Assert.AreEqual("agentsdk.state_request.meta", (string)o["type"]);
            Assert.AreEqual("power", (string)o["stateName"]);
            Assert.AreEqual("ok", (string)o["stateValue"]);
        }

        // ---------------- 入站（Agent→网关） ----------------

        /// <summary>官方 SDK base._build_envelope + _response_* 的真实出站形态。</summary>
        static string AgentEnvelope(string type, string extraJson = null)
        {
            var env = "{\"type\":\"" + type + "\",\"agentId\":\"agent-001\"," +
                "\"agentMode\":\"passive\",\"robotCid\":\"cid-x\",\"cid\":\"cid-x\"," +
                "\"eventId\":\"evt-x\"" +
                (extraJson != null ? "," + extraJson : "") + "}";
            return env;
        }

        [Test]
        public void ParseInbound_AsrFinal()
        {
            var f = FrameCodec.ParseInbound(AgentEnvelope(LinkskyTypes.AsrResponseFinal,
                "\"text\":\"今天天气怎么样\""));
            Assert.AreEqual(LinkskyTypes.AsrResponseFinal, f.Type);
            Assert.AreEqual("agent-001", f.AgentId);
            Assert.AreEqual("cid-x", f.RobotCid);
            Assert.AreEqual("evt-x", f.EventId);
            Assert.AreEqual("今天天气怎么样", f.Text);
            Assert.AreEqual("passive", f.AgentMode);
        }

        [Test]
        public void ParseInbound_LlmDelta()
        {
            var f = FrameCodec.ParseInbound(AgentEnvelope(LinkskyTypes.LlmResponseItemDelta,
                "\"itemId\":\"item-1\",\"text\":\"你好\""));
            Assert.AreEqual("item-1", f.ItemId);
            Assert.AreEqual("你好", f.Text);
        }

        [Test]
        public void ParseInbound_TtsDelta_DecodesBase64Audio()
        {
            var pcm = new byte[] { 0x10, 0x20, 0x30, 0x40 };
            var b64 = System.Convert.ToBase64String(pcm);
            var f = FrameCodec.ParseInbound(AgentEnvelope(LinkskyTypes.TtsResponseItemDelta,
                "\"itemId\":\"item-1\",\"audio\":\"" + b64 + "\",\"audioLen\":4"));
            Assert.AreEqual("item-1", f.ItemId);
            Assert.AreEqual(pcm, f.Audio);
            Assert.AreEqual(4, f.AudioLen);
        }

        [Test]
        public void ParseInbound_Skill_WithParam()
        {
            var json = AgentEnvelope(LinkskyTypes.XlmResponseSkill,
                "\"itemId\":\"item-2\",\"skillType\":\"movement\"," +
                "\"skillName\":\"wave_hands\",\"skillParam\":{\"movement\":\"wave_hands\"}");
            var f = FrameCodec.ParseInbound(json);
            Assert.AreEqual("movement", f.SkillType);
            Assert.AreEqual("wave_hands", f.SkillName);
            Assert.IsNotNull(f.SkillParam);
            Assert.AreEqual("wave_hands", f.SkillParam["movement"] as string);
        }

        [Test]
        public void ParseInbound_Interrupt()
        {
            var f = FrameCodec.ParseInbound(AgentEnvelope(LinkskyTypes.XlmResponseInterrupt,
                "\"interruptType\":\"chat\",\"interruptTips\":null"));
            Assert.AreEqual("chat", f.InterruptType);
            Assert.IsNull(f.InterruptTips);
        }

        [Test]
        public void ParseInbound_Error()
        {
            var f = FrameCodec.ParseInbound(AgentEnvelope(LinkskyTypes.Error,
                "\"errorCode\":5001,\"errorMsg\":\"ASR 服务不可用\""));
            Assert.AreEqual(5001, f.ErrorCode);
            Assert.AreEqual("ASR 服务不可用", f.ErrorMsg);
        }

        [Test]
        public void ParseInbound_InvalidJson_ReturnsNull()
        {
            Assert.IsNull(FrameCodec.ParseInbound("not json"));
            Assert.IsNull(FrameCodec.ParseInbound(""));
            Assert.IsNull(FrameCodec.ParseInbound(null));
        }

        // ---------------- CallbackTypes 解析 ----------------

        [Test]
        public void CallbackTypes_ParseFirst_PrefersFirstEntry()
        {
            Assert.AreEqual("asr2llm", CallbackTypes.ParseFirst("[\"asr2llm\",\"audio2tts\"]"));
            Assert.AreEqual("audio2tts", CallbackTypes.ParseFirst("[\"audio2tts\"]"));
        }

        [Test]
        public void CallbackTypes_ParseFirst_FallsBackOnGarbage()
        {
            Assert.AreEqual("audio2tts", CallbackTypes.ParseFirst(null));
            Assert.AreEqual("audio2tts", CallbackTypes.ParseFirst("garbage"));
            Assert.AreEqual("audio2tts", CallbackTypes.ParseFirst("[]"));
        }

        // ---------------- Ids ----------------

        [Test]
        public void Ids_AreUniqueAndPrefixed()
        {
            Assert.IsTrue(Ids.NewEventId().StartsWith("evt-"));
            Assert.IsTrue(Ids.NewItemId().StartsWith("item-"));
            Assert.IsTrue(Ids.NewRobotCid().StartsWith("cid-"));
            Assert.AreNotEqual(Ids.NewEventId(), Ids.NewEventId());
        }
    }
}
