using System.Collections.Generic;

namespace X02Competition.Gateway
{
    /// <summary>
    /// 网关看到的“机器人”（入站：Agent 说了什么）。
    /// 所有方法由 GatewaySession 投递到主线程队列后调用（Unity API 安全）。
    /// 这是 L1↔L2 的冻结契约，两侧并行开发的唯一接口。
    /// </summary>
    public interface IRobotRuntime
    {
        /// <summary>鉴权通过、robot_state.sync(online) 之前回调（会话建立）。</summary>
        void OnAgentConnected(string agentId, string callbackType);

        void OnAgentAsrText(string eventId, bool isFinal, string text);
        void OnAgentLlmDelta(string eventId, string itemId, string textDelta);
        void OnAgentTtsDelta(string eventId, string itemId, byte[] pcm);
        /// <summary>tts_response.item.done（一段 TTS 音频结束）。</summary>
        void OnAgentTtsDone(string eventId, string itemId);
        /// <summary>tts_response.done（本轮 TTS 全部结束）。</summary>
        void OnAgentRoundDone(string eventId);
        void OnAgentSkill(string eventId, string itemId, string skillType, string skillName,
            Dictionary<string, object> skillParam);
        void OnAgentInterrupt(string eventId, string interruptType, string tips);
        void OnAgentError(string eventId, int code, string msg);
        void OnAgentDisconnected();
    }

    /// <summary>
    /// 机器人看到的“网关”（出站：用户说了什么 / 机器人状态）。
    /// 实现方：GatewaySession。线程安全，可在任意线程调用。
    /// </summary>
    public interface IGatewayPort
    {
        /// <summary>发送 robot_state.sync(online)。含 AgentMeta。重连后由装配层再次调用。</summary>
        void SendRobotOnline(Dictionary<string, object> agentMeta, string callbackType);
        void SendRobotOffline();
        /// <summary>音频流开始（VAD flag=0），eventId/itemId 由机器人侧生成。</summary>
        void SendAudioStart(string eventId, string itemId);
        /// <summary>音频中间帧（flag=1），pcm 为 16k/16bit/mono。</summary>
        void SendAudioAppend(string eventId, string itemId, byte[] pcm);
        /// <summary>音频流结束（flag=2）。</summary>
        void SendAudioCommit(string eventId, string itemId);
        /// <summary>状态推送（state_request.meta 子集）。</summary>
        void SendState(string stateName, string stateValue);
        /// <summary>技能执行状态回报（skill_response.state）。state: running/done/failed。</summary>
        void SendSkillState(string skillName, string state, string detail);
    }
}
