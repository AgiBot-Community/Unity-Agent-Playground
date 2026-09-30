using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace X02Competition.Protocol
{
    /// <summary>
    /// Agent→网关方向帧的解析结果（弱类型承载，字段按需取用）。
    /// 字段名与官方 SDK base.py/_build_envelope 及各 response 方法逐一对齐。
    /// </summary>
    public sealed class InboundFrame
    {
        public string RawJson;
        public string Type;
        public string AgentId;
        public string RobotCid;
        public string EventId;
        public string ItemId;
        public string AgentMode;

        // ASR / LLM / VLM / Greet 文本
        public string Text;

        // xlm_response.interrupt
        public string InterruptType;
        public string InterruptTips;

        // xlm_response.skill
        public string SkillType;
        public string SkillName;
        public Dictionary<string, object> SkillParam;

        // tts_response.item.delta
        public byte[] Audio;
        public int AudioLen;

        // agentsdk.error
        public int ErrorCode;
        public string ErrorMsg;
        public string TargetRobotCid;
        public int Priority = -1;
        public bool? ControlEnabled;
    }

    /// <summary>
    /// 协议帧编解码器。网关侧唯一 JSON 出入口。
    /// 出站（网关→Agent）用 Build*；入站（Agent→网关）用 ParseInbound。
    /// </summary>
    public static class FrameCodec
    {
        // ---------------------------------------------------------------
        // 出站帧构造（网关 → Agent）
        // ---------------------------------------------------------------

        static JObject Envelope(string type, string agentId, string robotCid, string eventId)
        {
            return new JObject
            {
                ["type"] = type,
                ["agentId"] = agentId ?? "",
                ["robotCid"] = robotCid ?? "",
                ["cid"] = robotCid ?? "",
                ["eventId"] = eventId ?? "",
            };
        }

        /// <summary>机器人上下线同步（agentsdk.robot_state.sync）。agentMeta 为 AgentMeta 字典。</summary>
        public static string BuildRobotStateSync(string agentId, string robotCid, string state,
            string callbackType, IReadOnlyDictionary<string, object> agentMeta,
            string role = null, int priority = 0, bool controlActive = true, bool audioActive = true,
            bool controlEnabled = true, bool canManage = false)
        {
            var o = new JObject
            {
                ["type"] = LinkskyTypes.RobotStateSync,
                ["agentId"] = agentId ?? "",
                ["state"] = state,
                ["callbackType"] = callbackType ?? CallbackTypes.Audio2Tts,
                ["robotCid"] = robotCid ?? "",
                ["cid"] = robotCid ?? "",
                ["clientRole"] = role ?? "agent",
                ["priority"] = priority,
                ["controlActive"] = controlActive,
                ["audioActive"] = audioActive,
                ["controlEnabled"] = controlEnabled,
                ["canManage"] = canManage,
            };
            if (agentMeta != null)
            {
                var meta = new JObject();
                foreach (var kv in agentMeta) meta[kv.Key] = JToken.FromObject(kv.Value ?? "");
                o["agentMeta"] = meta;
            }
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>音频流开始（VAD flag=0）。仅 audio2tts 回调类型携带 itemId（对齐 SDK handle_audio_request_start）。</summary>
        public static string BuildAudioRequestStart(string agentId, string robotCid, string eventId,
            string itemId, bool withItemId)
        {
            var o = Envelope(LinkskyTypes.AudioRequestStart, agentId, robotCid, eventId);
            if (withItemId) o["itemId"] = itemId ?? "";
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>音频流中间帧（VAD flag=1），audio 为原始 PCM，base64 内嵌。</summary>
        public static string BuildAudioRequestAppend(string agentId, string robotCid, string eventId,
            string itemId, byte[] audio)
        {
            var o = Envelope(LinkskyTypes.AudioRequestAppend, agentId, robotCid, eventId);
            o["itemId"] = itemId ?? "";
            o["audio"] = audio != null ? Convert.ToBase64String(audio) : "";
            o["audioLen"] = audio != null ? audio.Length : 0;
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>音频流结束（VAD flag=2）。</summary>
        public static string BuildAudioRequestCommit(string agentId, string robotCid, string eventId,
            string itemId)
        {
            var o = Envelope(LinkskyTypes.AudioRequestCommit, agentId, robotCid, eventId);
            o["itemId"] = itemId ?? "";
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>状态推送（agentsdk.state_request.meta）。stateValue 发字符串（SDK 侧对 dict 会再 dumps，直接发字符串最稳）。</summary>
        public static string BuildStateRequestMeta(string agentId, string robotCid, string eventId,
            string stateName, string stateValue)
        {
            var o = Envelope(LinkskyTypes.StateRequestMeta, agentId, robotCid, eventId);
            o["stateName"] = stateName ?? "";
            o["stateValue"] = stateValue ?? "";
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>技能状态回报帧（网关 → Agent，仿真扩展）。</summary>
        public static string BuildSkillResponseState(string agentId, string robotCid, string eventId,
            string skillName, string state, string detail)
        {
            var o = Envelope(LinkskyTypes.SkillResponseState, agentId, robotCid, eventId);
            o["skillName"] = skillName ?? "";
            o["state"] = state ?? "";
            o["detail"] = detail ?? "";
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string BuildRuntimeLog(string agentId, string robotCid, string level,
            string logType, string message, string stackTrace, long timestampMs, long sequence,
            int threadId, long droppedCount, bool truncated, long sessionDroppedCount = 0)
        {
            var o = Envelope(LinkskyTypes.RuntimeLog, agentId, robotCid, Ids.NewEventId());
            o["source"] = "unity";
            o["level"] = level;
            o["logType"] = logType;
            o["message"] = message ?? "";
            o["stackTrace"] = stackTrace ?? "";
            o["timestampMs"] = timestampMs;
            o["sequence"] = sequence;
            o["threadId"] = threadId;
            o["droppedCount"] = droppedCount;
            o["sessionDroppedCount"] = sessionDroppedCount;
            o["truncated"] = truncated;
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string BuildSessionState(string agentId, string cid, long revision,
            List<Dictionary<string, object>> sessions, string controlCid, string audioCid, string managementCid)
        {
            var o = Envelope(LinkskyTypes.SessionState, agentId, cid, Ids.NewEventId());
            o["revision"] = revision;
            o["sessions"] = JArray.FromObject(sessions);
            o["controlOwnerCid"] = controlCid;
            o["audioOwnerCid"] = audioCid;
            o["managementOwnerCid"] = managementCid;
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string BuildError(string agentId, string cid, string eventId, int code, string message)
        {
            var o = Envelope(LinkskyTypes.Error, agentId, cid, eventId);
            o["errorCode"] = code;
            o["errorMsg"] = message;
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        // ---------------------------------------------------------------
        // 入站帧解析（Agent → 网关）
        // ---------------------------------------------------------------

        /// <summary>解析入站 JSON 文本帧。非法 JSON 或非对象返回 null。</summary>
        public static InboundFrame ParseInbound(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            JObject o;
            try
            {
                o = JObject.Parse(json);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // JSON 解析失败 - 返回 null 让调用者处理
                // 注意：此方法被网关接收循环调用，异常日志在调用方记录
                return null;
            }
            catch (Exception)
            {
                // 意外异常 - 同样返回 null
                return null;
            }
            if (o == null) return null;

            var f = new InboundFrame
            {
                RawJson = json,
                Type = Str(o, "type"),
                AgentId = Str(o, "agentId"),
                RobotCid = Str(o, "robotCid"),
                EventId = Str(o, "eventId"),
                ItemId = Str(o, "itemId"),
                AgentMode = Str(o, "agentMode"),
                Text = Str(o, "text"),
                InterruptType = Str(o, "interruptType"),
                InterruptTips = Str(o, "interruptTips"),
                SkillType = Str(o, "skillType"),
                SkillName = Str(o, "skillName"),
                TargetRobotCid = Str(o, "targetRobotCid"),
            };
            if (o.TryGetValue("priority", out var priority) && priority.Type == JTokenType.Integer &&
                long.TryParse(priority.ToString(), out var number) && number >= 0 && number <= 999)
                f.Priority = (int)number;
            if (o.TryGetValue("enabled", out var enabled) && enabled.Type == JTokenType.Boolean)
                f.ControlEnabled = (bool)enabled;

            if (o.TryGetValue("skillParam", out var sp) && sp is JObject spObj)
            {
                f.SkillParam = spObj.ToObject<Dictionary<string, object>>();
            }

            if (o.TryGetValue("audio", out var audioTok))
            {
                var s = audioTok?.Type == JTokenType.String ? (string)audioTok : null;
                if (!string.IsNullOrEmpty(s))
                {
                    try { f.Audio = Convert.FromBase64String(s); } catch { f.Audio = null; }
                }
            }
            f.AudioLen = Int(o, "audioLen");
            f.ErrorCode = Int(o, "errorCode");
            f.ErrorMsg = Str(o, "errorMsg");
            return f;
        }

        static string Str(JObject o, string key)
        {
            return o.TryGetValue(key, out var t) && t.Type == JTokenType.String ? (string)t : null;
        }

        static int Int(JObject o, string key)
        {
            if (o.TryGetValue(key, out var t))
            {
                if (t.Type == JTokenType.Integer) return (int)t;
                if (t.Type == JTokenType.String && int.TryParse((string)t, out var v)) return v;
            }
            return 0;
        }
    }
}
