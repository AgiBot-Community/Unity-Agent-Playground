using System.Collections.Generic;

namespace X02Competition.Protocol
{
    /// <summary>
    /// LinkSoul AgentSDK v1.4.0 线上协议消息类型常量。
    /// 与官方 SDK enums.AgentEventType 一一对应（仅事件字符串，不引入枚举绑定）。
    /// 分组：GatewayToAgent = 网关(Unity/真机)→Agent 方向；AgentToGateway = 反方向。
    /// </summary>
    public static class LinkskyTypes
    {
        // ---- 网关 → Agent（Mock 网关构造发送） ----
        public const string RobotStateSync = "agentsdk.robot_state.sync";
        public const string AudioRequestStart = "agentsdk.audio_request.start";
        public const string AudioRequestAppend = "agentsdk.audio_request.append";
        public const string AudioRequestCommit = "agentsdk.audio_request.commit";
        public const string AsrRequestText = "agentsdk.asr_request.text";
        public const string StateRequestMeta = "agentsdk.state_request.meta";
        public const string VideoH264RequestAppend = "agentsdk.video_h264_request.append";
        public const string VideoImageRequestAppend = "agentsdk.video_image_request.append";
        public const string GreetRequestSignal = "agentsdk.greet_request.signal";
        public const string GreetRequestFace = "agentsdk.greet_request.face";
        public const string GreetRequestVideo = "agentsdk.greet_request.video";

        /// <summary>技能执行状态回报（v1 仿真扩展：Agent 可感知动作何时完成）。</summary>
        public const string SkillResponseState = "agentsdk.skill_response.state";

        // ---- Agent → 网关（Mock 网关解析处理） ----
        public const string AsrResponseMiddle = "agentsdk.asr_response.middle";
        public const string AsrResponseFinal = "agentsdk.asr_response.final";
        public const string LlmResponseItemDelta = "agentsdk.llm_response.item.delta";
        public const string LlmResponseItemDone = "agentsdk.llm_response.item.done";
        public const string LlmResponseDone = "agentsdk.llm_response.done";
        public const string VlmResponseItemDelta = "agentsdk.vlm_response.item.delta";
        public const string VlmResponseItemDone = "agentsdk.vlm_response.item.done";
        public const string VlmResponseDone = "agentsdk.vlm_response.done";
        public const string TtsResponseItemDelta = "agentsdk.tts_response.item.delta";
        public const string TtsResponseItemDone = "agentsdk.tts_response.item.done";
        public const string TtsResponseDone = "agentsdk.tts_response.done";
        public const string XlmResponseSkill = "agentsdk.xlm_response.skill";
        public const string XlmResponseInterrupt = "agentsdk.xlm_response.interrupt";
        public const string XlmResponseControl = "agentsdk.xlm_response.control";
        public const string GreetResponseVlmDelta = "agentsdk.greet_response.vlm.delta";
        public const string GreetResponseVlmDone = "agentsdk.greet_response.vlm.done";
        public const string GreetResponseTtsDelta = "agentsdk.greet_response.tts.delta";
        public const string GreetResponseTtsDone = "agentsdk.greet_response.tts.done";
        public const string Error = "agentsdk.error";
    }

    /// <summary>回调类型（X-Callback-Types 头取值）。赛事主路径：audio2tts。</summary>
    public static class CallbackTypes
    {
        public const string Audio2Llm = "audio2llm";
        public const string Audio2Tts = "audio2tts";
        public const string Asr2Llm = "asr2llm";
        public const string Asr2Tts = "asr2tts";
        public const string AsrVideo2Tts = "asrVideo2tts";
        public const string AsrVideo2Vlm = "asrVideo2vlm";
        public const string AudioVideo2Tts = "audioVideo2tts";
        public const string AudioVideo2Vlm = "audioVideo2vlm";
        public const string PushListen = "pushListen";

        /// <summary>解析 X-Callback-Types 头（JSON 数组字符串），返回首个有效项；解析失败回退 audio2tts。</summary>
        public static string ParseFirst(string headerValue)
        {
            if (string.IsNullOrEmpty(headerValue)) return Audio2Tts;
            try
            {
                var arr = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(headerValue);
                if (arr != null && arr.Count > 0 && !string.IsNullOrEmpty(arr[0])) return arr[0];
            }
            catch { /* 宽松处理 */ }
            return Audio2Tts;
        }
    }
}
