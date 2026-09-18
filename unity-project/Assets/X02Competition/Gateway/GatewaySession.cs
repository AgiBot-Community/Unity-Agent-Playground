using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using X02Competition.Protocol;

namespace X02Competition.Gateway
{
    /// <summary>
    /// 单个 Agent 连接的会话：对外是 IGatewayPort（出站），对内把入站帧投递给 IRobotRuntime。
    /// 线程模型：
    ///   - 接收线程：WS 收帧 → FrameCodec 解析 → 入站动作压入 _toMain（由主线程泵执行）
    ///   - 发送线程：串行化出站帧（保证音频帧有序），lock-free 队列 + 信号量唤醒
    /// </summary>
    public sealed class GatewaySession : IGatewayPort
    {
        readonly LinkskyGatewayServer _server;
        readonly ServerWsConnection _ws;
        readonly ConcurrentQueue<Action> _toMain = new ConcurrentQueue<Action>();
        readonly ConcurrentQueue<string> _toSend = new ConcurrentQueue<string>();
        readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);

        Thread _recvThread;
        Thread _sendThread;
        // 构造即置位：SessionOpened（装配层 SendRobotOnline）发生在 StartThreads 之前，
        // 否则首帧会被 EnqueueFrame 的 _open 门静默丢弃
        volatile bool _open = true;
        volatile bool _closedRaised;
        readonly object _runtimeLock = new object();

        public string RobotCid { get; }
        public string CallbackType { get; }
        public bool IsOpen => _open;

        /// <summary>由装配层（Launcher / 测试）在 SessionOpened 时立即绑定。线程安全。</summary>
        public IRobotRuntime Runtime
        {
            get { lock (_runtimeLock) return _runtime; }
            set { lock (_runtimeLock) _runtime = value; }
        }
        IRobotRuntime _runtime;

        /// <summary>会话关闭（含异常断开）时触发，在接收线程回调。</summary>
        public event Action<GatewaySession> Closed;
        /// <summary>全帧收发日志（含方向前缀），调试与争议仲裁用。</summary>
        public event Action<string> FrameLogged;

        internal GatewaySession(LinkskyGatewayServer server, ServerWsConnection ws, string callbackType)
        {
            _server = server;
            _ws = ws;
            RobotCid = Ids.NewRobotCid();
            CallbackType = callbackType;
        }

        internal void StartThreads()
        {
            // 会话建立通知（主线程回调；SessionOpened 已先绑定 Runtime，时序安全）
            var agentId = _server.AgentId;
            var callbackType = CallbackType;
            EnqueueToMain(() =>
            {
                var rt = Runtime;
                if (rt != null) rt.OnAgentConnected(agentId, callbackType);
            });
            _sendThread = new Thread(SendLoop) { IsBackground = true, Name = "gw-send-" + RobotCid };
            _recvThread = new Thread(RecvLoop) { IsBackground = true, Name = "gw-recv-" + RobotCid };
            _sendThread.Start();
            _recvThread.Start();
        }

        // ---------------- 入站（接收线程 → 主线程队列） ----------------

        void RecvLoop()
        {
            var buffer = new byte[256 * 1024];
            var ms = new System.IO.MemoryStream();
            try
            {
                while (_open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        var segment = new ArraySegment<byte>(buffer);
                        result = _ws.ReceiveAsync(segment, CancellationToken.None).GetAwaiter().GetResult();
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Log("gw  <- agent  CLOSE(" + result.CloseStatus + ")");
                            Close(WebSocketCloseStatus.NormalClosure, "bye");
                            return;
                        }
                        // 字节先累积，EndOfMessage 后统一解码：多字节 UTF-8 字符跨分片安全
                        if (result.Count > 0) ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    Log("gw  <- agent  " + Trunc(text));
                    HandleMessage(text);
                }
            }
            catch (Exception)
            {
                Close(WebSocketCloseStatus.InternalServerError, "recv error");
            }
        }

        void HandleMessage(string text)
        {
            var frame = FrameCodec.ParseInbound(text);
            if (frame == null)
            {
                Log("gw  warn     invalid inbound json");
                return;
            }
            // 解析在接收线程，业务回调在主线程（Unity API 安全）
            EnqueueToMain(() => Dispatch(frame));
        }

        void Dispatch(InboundFrame f)
        {
            IRobotRuntime rt;
            lock (_runtimeLock) rt = _runtime;
            if (rt == null)
            {
                // 装配层未绑定 Runtime 却收到入站帧 —— 高危静默丢帧，必须显式告警
                Log("gw  WARN     frame dropped, session.Runtime not bound: " + f.Type);
                return;
            }

            switch (f.Type)
            {
                case LinkskyTypes.AsrResponseMiddle:
                    rt.OnAgentAsrText(f.EventId, false, f.Text);
                    break;
                case LinkskyTypes.AsrResponseFinal:
                    rt.OnAgentAsrText(f.EventId, true, f.Text);
                    break;
                case LinkskyTypes.LlmResponseItemDelta:
                    rt.OnAgentLlmDelta(f.EventId, f.ItemId, f.Text);
                    break;
                case LinkskyTypes.TtsResponseItemDelta:
                    rt.OnAgentTtsDelta(f.EventId, f.ItemId, f.Audio);
                    break;
                case LinkskyTypes.TtsResponseItemDone:
                    rt.OnAgentTtsDone(f.EventId, f.ItemId);
                    break;
                case LinkskyTypes.TtsResponseDone:
                    rt.OnAgentRoundDone(f.EventId);
                    break;
                case LinkskyTypes.XlmResponseSkill:
                    rt.OnAgentSkill(f.EventId, f.ItemId, f.SkillType, f.SkillName, f.SkillParam);
                    break;
                case LinkskyTypes.XlmResponseInterrupt:
                    rt.OnAgentInterrupt(f.EventId, f.InterruptType, f.InterruptTips);
                    break;
                case LinkskyTypes.Error:
                    rt.OnAgentError(f.EventId, f.ErrorCode, f.ErrorMsg);
                    break;
                // v1 范围外：llm item done / vlm / greet / control —— 忽略（协议文档 §7）
                default:
                    Log("gw  ignore   " + f.Type);
                    break;
            }
        }

        // ---------------- 出站（IGatewayPort，任意线程 → 发送队列） ----------------

        public void SendRobotOnline(Dictionary<string, object> agentMeta, string callbackType)
        {
            EnqueueFrame(FrameCodec.BuildRobotStateSync(_server.AgentId, RobotCid, "online",
                callbackType, agentMeta));
        }

        public void SendRobotOffline()
        {
            EnqueueFrame(FrameCodec.BuildRobotStateSync(_server.AgentId, RobotCid, "offline",
                CallbackType, null));
        }

        public void SendAudioStart(string eventId, string itemId)
        {
            EnqueueFrame(FrameCodec.BuildAudioRequestStart(_server.AgentId, RobotCid, eventId,
                itemId, _server.IncludeItemIdFor(CallbackType)));
        }

        public void SendAudioAppend(string eventId, string itemId, byte[] pcm)
        {
            EnqueueFrame(FrameCodec.BuildAudioRequestAppend(_server.AgentId, RobotCid, eventId,
                itemId, pcm));
        }

        public void SendAudioCommit(string eventId, string itemId)
        {
            EnqueueFrame(FrameCodec.BuildAudioRequestCommit(_server.AgentId, RobotCid, eventId,
                itemId));
        }

        public void SendState(string stateName, string stateValue)
        {
            EnqueueFrame(FrameCodec.BuildStateRequestMeta(_server.AgentId, RobotCid,
                Ids.NewEventId(), stateName, stateValue));
        }

        public void SendSkillState(string skillName, string state, string detail)
        {
            EnqueueFrame(FrameCodec.BuildSkillResponseState(_server.AgentId, RobotCid,
                Ids.NewEventId(), skillName, state, detail));
        }

        void EnqueueFrame(string json)
        {
            if (!_open) return;
            _toSend.Enqueue(json);
            _sendSignal.Set();
        }

        void SendLoop()
        {
            try
            {
                while (_open)
                {
                    _sendSignal.WaitOne(1000);
                    while (_toSend.TryDequeue(out var json))
                    {
                        if (!_open) return;
                        var bytes = Encoding.UTF8.GetBytes(json);
                        Log("gw  -> agent  " + Trunc(json));
                        _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                            true, CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception)
            {
                Close(WebSocketCloseStatus.InternalServerError, "send error");
            }
        }

        // ---------------- 生命周期 ----------------

        /// <summary>主线程泵：执行入站业务回调。返回执行条数。</summary>
        public int ExecutePendingActions(int max = 256)
        {
            var n = 0;
            while (n < max && _toMain.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Log("gw  main-cb error: " + e.Message); }
                n++;
            }
            return n;
        }

        void EnqueueToMain(Action a) => _toMain.Enqueue(a);

        /// <summary>主动关闭会话（服务停止 / 单客户端策略抢占）。</summary>
        public void Close(WebSocketCloseStatus status, string reason)
        {
            if (!_open) return;
            _open = false;
            try { _ws.CloseAsync(status, reason, CancellationToken.None).Wait(500); } catch { }
            try { _ws.Dispose(); } catch { }

            EnqueueToMain(() =>
            {
                var rt = Runtime;
                if (rt != null) rt.OnAgentDisconnected();
            });

            if (_closedRaised) return;
            _closedRaised = true;
            Log("gw  session closed");
            Closed?.Invoke(this);
        }

        static string Trunc(string s)
        {
            const int max = 300;
            return s != null && s.Length > max ? s.Substring(0, max) + "...(" + s.Length + ")" : s;
        }

        void Log(string line) => FrameLogged?.Invoke(line + "  [" + RobotCid + "]");
    }
}
