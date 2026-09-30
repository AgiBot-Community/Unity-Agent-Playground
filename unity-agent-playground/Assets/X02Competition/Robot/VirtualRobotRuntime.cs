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
    ///   - 半双工：提交录音后等待 ASR/LLM/TTS，再恢复上行
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
        [Tooltip("回复连续无进展的超时（秒）；超时后恢复聆听")]
        [Min(1)] public float ResponseTimeoutSeconds = 90f;
        public bool ResponsePending { get; private set; }

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
        public event Action RecordingStarted;
        public event Action RecordingCommitted;
        public event Action TtsUpdated;
        public event Action ResponseTimedOut;

        string _activeEventId;
        string _activeItemId;
        float _stateTimer;
        string _responseEventId;
        readonly HashSet<string> _retiredResponses = new HashSet<string>();
        readonly HashSet<string> _cancelledResponses = new HashSet<string>();
        readonly Queue<string> _retiredResponseOrder = new Queue<string>();
        double _lastResponseActivity;

        // ---------------- 生命周期 ----------------

        /// <summary>会话建立（SessionOpened）时由装配层调用。
        /// 由主线程泵调用；清掉会话建立前的录音，避免上传历史音频。</summary>
        public void Bind(IGatewayPort port)
        {
            Port = port;
            Input?.DiscardPending();
            Vad.Reset();
            ResetResponseState();
        }

        /// <summary>会话断开时调用。</summary>
        public void Unbind()
        {
            Port = null;
            Vad.Reset();
            Input?.DiscardPending();
            Tts?.Stop();
            Router?.Abort();
            ResetResponseState();
        }

        public void ResetSpeechSession()
        {
            Input?.DiscardPending();
            Vad.Reset();
            Tts?.Stop();
            ResetResponseState();
        }

        void Update()
        {
            CheckResponseTimeout();
            // Waiting for cloud ASR/LLM is part of the same turn, before audio exists.
            var ttsActive = Tts != null && Tts.IsStreamingActive;
            Vad.Enabled = !ResponsePending && !ttsActive;

            if (Input != null)
            {
                if (Port == null || (Port is LinkskyGatewayServer gateway && gateway.AudioOwner == null))
                    Input.DiscardPending();
                else
                {
                    // A finished clip can still have its final frame in the queue.
                    while (Input.TryReadFrame(out var frame)) HandleFrame(frame);
                    if (!Input.IsRunning && Vad.InSpeech)
                    {
                        CommitRecording();
                        Vad.Reset();
                    }
                }
            }

            PublishStatesPeriodically();
        }

        void HandleFrame(float[] frame)
        {
            var now = Time.realtimeSinceStartupAsDouble;
            // A single input batch may contain frames after the preceding commit.
            Vad.Enabled = !ResponsePending && (Tts == null || !Tts.IsStreamingActive);
            var evt = Vad.Feed(frame, now);

            if (evt == VadGate.Event.SpeechStarted)
            {
                RetireResponse(_responseEventId);
                // 新一轮对话：若上一轮 TTS 仍在播（理论上门控已冻结，防御性停掉）
                Tts?.Stop();
                Router?.Abort(); // 用户开口：停当前运动/手势
                _activeEventId = Ids.NewEventId();
                _activeItemId = Ids.NewItemId();
                RecordingStarted?.Invoke();
                Port.SendAudioStart(_activeEventId, _activeItemId);
                Port.SendAudioAppend(_activeEventId, _activeItemId, PcmCodec.ToPcm16(frame));
            }
            else if (Vad.InSpeech)
            {
                Port.SendAudioAppend(_activeEventId, _activeItemId, PcmCodec.ToPcm16(frame));
            }
            else if (evt == VadGate.Event.SpeechEnded)
            {
                CommitRecording();
            }
        }

        void CommitRecording()
        {
            BeginResponse(_activeEventId);
            Port.SendAudioCommit(_activeEventId, _activeItemId);
            RecordingCommitted?.Invoke();
        }

        void BeginResponse(string eventId)
        {
            if (_responseEventId != eventId) RetireResponse(_responseEventId);
            ResponsePending = true;
            _responseEventId = eventId;
            _lastResponseActivity = Time.realtimeSinceStartupAsDouble;
            Vad.Enabled = false;
        }

        void ResetResponseState()
        {
            ResponsePending = false;
            _responseEventId = null;
            _retiredResponses.Clear();
            _cancelledResponses.Clear();
            _retiredResponseOrder.Clear();
        }

        bool ObserveResponse(string eventId)
        {
            if (eventId != null && _retiredResponses.Contains(eventId)) return false;
            if (eventId != _responseEventId &&
                (ResponsePending || (Tts != null && Tts.IsStreamingActive)))
            {
                // The gateway may authorize a controller to replace the voice Agent's reply.
                Tts?.Stop();
                Vad.Reset();
                Input?.DiscardPending();
                BeginResponse(eventId);
            }
            if (!ResponsePending) _responseEventId = eventId;
            _lastResponseActivity = Time.realtimeSinceStartupAsDouble;
            return true;
        }

        bool IsCurrentResponse(string eventId) =>
            eventId == _responseEventId && (eventId == null || !_retiredResponses.Contains(eventId));

        void RetireResponse(string eventId, bool cancelled = true)
        {
            if (string.IsNullOrEmpty(eventId)) return;
            if (cancelled) _cancelledResponses.Add(eventId);
            if (!_retiredResponses.Add(eventId)) return;
            _retiredResponseOrder.Enqueue(eventId);
            if (_retiredResponseOrder.Count > 32)
            {
                var oldest = _retiredResponseOrder.Dequeue();
                _retiredResponses.Remove(oldest);
                _cancelledResponses.Remove(oldest);
            }
        }

        void CompleteResponse(string eventId)
        {
            if (!ResponsePending || eventId != _responseEventId) return;
            ResponsePending = false;
            Input?.DiscardPending();
        }

        void CheckResponseTimeout()
        {
            if (!ResponsePending && (Tts == null || !Tts.IsStreamingActive)) return;
            if (Time.realtimeSinceStartupAsDouble - _lastResponseActivity <
                Mathf.Max(1f, ResponseTimeoutSeconds)) return;
            RetireResponse(_responseEventId);
            ResponsePending = false;
            Tts?.Stop();
            Vad.Reset();
            Input?.DiscardPending();
            Debug.LogWarning("[Runtime] 回复等待超时，已恢复聆听: " + _responseEventId);
            ResponseTimedOut?.Invoke();
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
            if (!ObserveResponse(eventId)) return;
            AsrTextReceived?.Invoke(text, isFinal);
        }

        public void OnAgentLlmDelta(string eventId, string itemId, string textDelta)
        {
            if (!ObserveResponse(eventId)) return;
            // A greeting has no preceding recording/commit, but still owns the half-duplex turn.
            if (!ResponsePending && !Vad.InSpeech) BeginResponse(eventId);
            LlmDeltaReceived?.Invoke(textDelta);
        }

        public void OnAgentTtsDelta(string eventId, string itemId, byte[] pcm)
        {
            if (!ObserveResponse(eventId)) return;
            Tts?.Append(eventId, itemId, pcm);
            TtsUpdated?.Invoke();
        }

        public void OnAgentTtsDone(string eventId, string itemId)
        {
            // 单段音频结束：无需特殊处理（播放器自行追赶）
            if (IsCurrentResponse(eventId)) _lastResponseActivity = Time.realtimeSinceStartupAsDouble;
        }

        public void OnAgentRoundDone(string eventId)
        {
            if (!IsCurrentResponse(eventId)) return;
            _lastResponseActivity = Time.realtimeSinceStartupAsDouble;
            Tts?.MarkRoundDone();
            CompleteResponse(eventId);
            RetireResponse(eventId, false);
            TtsUpdated?.Invoke();
        }

        public void OnAgentSkill(string eventId, string itemId, string skillType, string skillName,
            Dictionary<string, object> skillParam)
        {
            if (eventId != null && _cancelledResponses.Contains(eventId)) return;
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
            RetireResponse(_responseEventId);
            ResponsePending = false;
            Input?.DiscardPending();
            Vad.Reset();
            Tts?.Stop();
            Router?.Abort();
            InterruptReceived?.Invoke(interruptType);
        }

        public void OnAgentError(string eventId, int code, string msg)
        {
            if (eventId != null && _retiredResponses.Contains(eventId)) return;
            // A failed/empty ASR round has no TTS done frame. Tool errors may still produce speech.
            if (code >= 3100 && code < 3400 && IsCurrentResponse(eventId))
            {
                CompleteResponse(eventId);
                Tts?.Stop();
                Vad.Reset();
                Input?.DiscardPending();
                RetireResponse(eventId);
            }
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
