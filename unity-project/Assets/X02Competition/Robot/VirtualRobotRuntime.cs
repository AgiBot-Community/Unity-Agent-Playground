using System;
using System.Collections.Generic;
using UnityEngine;
using X02Competition.Gateway;
using X02Competition.Protocol;

namespace X02Competition.Robot
{
    /// <summary>
    /// L2 虚拟机器人总控（IRobotRuntime 实现，挂在 X2 根物体上）。
    /// 职责：装配音频输入/VAD/TTS 播放/技能执行，实现协议 §5 的时序与边界语义：
    ///   - VAD start/append/commit → 网关三段式上行
    ///   - 半双工：TTS 播放中冻结上行（门控在 VadGate.Enabled）
    ///   - interrupt / 新一轮 start（由门控间接保证不会并发）→ 停播停动作
    /// </summary>
    public class VirtualRobotRuntime : MonoBehaviour, IRobotRuntime
    {
        [Header("装配（由 CompetitionLauncher 注入，也可手动挂）")]
        public IAudioInputSource Input;
        public TtsStreamPlayer Tts;
        [Tooltip("技能路由（推荐：手势/运动/表情/Trigger 统一分发）")]
        public SkillRouter Router;
        [Tooltip("兼容路径：无 Router 时直接走 Animator Trigger")]
        public SkillAnimator Skills;

        [Header("VAD 参数（与真机抓包校准）")]
        public VadGate Vad = new VadGate();

        [Header("状态推送")]
        [Tooltip("周期推送间隔（秒），0 = 关闭")]
        public float StatePublishInterval = 5f;

        /// <summary>当前绑定的网关端口（会话建立时由装配层设置；断开置空）。</summary>
        public IGatewayPort Port { get; private set; }

        // ---- UI 事件（SubtitlePresenter / DebugHud 订阅） ----
        public event Action<string, bool> AsrTextReceived;      // (text, isFinal)
        public event Action<string> LlmDeltaReceived;
        public event Action<string, string> SkillRequested;     // (skillType, skillName)
        public event Action<string, string> SkillStateChanged;  // (skillName, state)
        public event Action<string> InterruptReceived;          // interruptType
        public event Action<int, string> AgentErrorReceived;
        public event Action Connected;
        public event Action Disconnected;

        string _activeEventId;
        string _activeItemId;
        float _stateTimer;

        // ---------------- 生命周期 ----------------

        /// <summary>会话建立（SessionOpened）时由装配层调用。
        /// 注意：本方法可能在网关监听线程调用 —— 只允许线程安全操作（赋值/纯 C# 重置）；
        /// TTS 清场等 Unity API 在 OnAgentConnected（主线程泵）执行。</summary>
        public void Bind(IGatewayPort port)
        {
            Port = port;
            Vad.Reset();
        }

        /// <summary>会话断开时调用。</summary>
        public void Unbind()
        {
            Port = null;
            Vad.Reset();
            Tts?.Stop();
        }

        void Update()
        {
            // 半双工门控：TTS 播放中冻结麦克风上行
            var ttsActive = Tts != null && Tts.IsStreamingActive;
            Vad.Enabled = !ttsActive;

            if (Input != null && Input.IsRunning && Port != null)
            {
                while (Input.TryReadFrame(out var frame))
                {
                    HandleFrame(frame);
                }
            }

            PublishStatesPeriodically();
        }

        void HandleFrame(float[] frame)
        {
            var now = Time.realtimeSinceStartupAsDouble;
            var evt = Vad.Feed(frame, now);

            if (evt == VadGate.Event.SpeechStarted)
            {
                // 新一轮对话：若上一轮 TTS 仍在播（理论上门控已冻结，防御性停掉）
                Tts?.Stop();
                Router?.Abort(); // 用户开口：停当前运动/手势
                _activeEventId = Ids.NewEventId();
                _activeItemId = Ids.NewItemId();
                Port.SendAudioStart(_activeEventId, _activeItemId);
                Port.SendAudioAppend(_activeEventId, _activeItemId, PcmCodec.ToPcm16(frame));
            }
            else if (Vad.InSpeech)
            {
                Port.SendAudioAppend(_activeEventId, _activeItemId, PcmCodec.ToPcm16(frame));
            }
            else if (evt == VadGate.Event.SpeechEnded)
            {
                Port.SendAudioCommit(_activeEventId, _activeItemId);
            }
        }

        void PublishStatesPeriodically()
        {
            if (StatePublishInterval <= 0 || Port == null) return;
            _stateTimer += Time.deltaTime;
            if (_stateTimer < StatePublishInterval) return;
            _stateTimer = 0;
            Port.SendState("power", "ok");
            Port.SendState("network", "ok");
        }

        // ---------------- IRobotRuntime（网关主线程泵回调） ----------------

        public void OnAgentConnected(string agentId, string callbackType)
        {
            Tts?.Stop(); // 新会话清场（主线程，Bind 不能碰 Unity API）
            Debug.Log("[Runtime] Agent 已连接: " + agentId + " (" + callbackType + ")");
            Connected?.Invoke();
        }

        public void OnAgentAsrText(string eventId, bool isFinal, string text)
        {
            AsrTextReceived?.Invoke(text, isFinal);
        }

        public void OnAgentLlmDelta(string eventId, string itemId, string textDelta)
        {
            LlmDeltaReceived?.Invoke(textDelta);
        }

        public void OnAgentTtsDelta(string eventId, string itemId, byte[] pcm)
        {
            Tts?.Append(eventId, itemId, pcm);
        }

        public void OnAgentTtsDone(string eventId, string itemId)
        {
            // 单段音频结束：无需特殊处理（播放器自行追赶）
        }

        public void OnAgentRoundDone(string eventId)
        {
            Tts?.MarkRoundDone();
        }

        public void OnAgentSkill(string eventId, string itemId, string skillType, string skillName,
            Dictionary<string, object> skillParam)
        {
            SkillRequested?.Invoke(skillType, skillName);
            if (Router != null)
            {
                Router.Execute(skillType, skillName, skillParam, state =>
                {
                    SkillStateChanged?.Invoke(skillName, state);
                    Port?.SendSkillState(skillName, state, "");
                });
            }
            else
            {
                Skills?.Play(skillType, skillName);
            }
        }

        public void OnAgentInterrupt(string eventId, string interruptType, string tips)
        {
            // 官方语义：新应答前打断旧的 —— 停当前播报/动作
            Tts?.Stop();
            Router?.Abort();
            InterruptReceived?.Invoke(interruptType);
        }

        public void OnAgentError(string eventId, int code, string msg)
        {
            Debug.LogWarning("[Runtime] Agent 错误: code=" + code + " msg=" + msg);
            AgentErrorReceived?.Invoke(code, msg);
        }

        public void OnAgentDisconnected()
        {
            Unbind();
            Debug.Log("[Runtime] Agent 已断开");
            Disconnected?.Invoke();
        }
    }

    /// <summary>float[-1,1] ↔ 16bit PCM 小端转换。</summary>
    public static class PcmCodec
    {
        public static byte[] ToPcm16(float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                var v = Mathf.Clamp(samples[i], -1f, 1f);
                var s = (short)(v * 32767f);
                bytes[i * 2] = (byte)(s & 0xff);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
            }
            return bytes;
        }
    }
}
