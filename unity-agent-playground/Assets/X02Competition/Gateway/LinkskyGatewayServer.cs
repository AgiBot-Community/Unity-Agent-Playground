using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using X02Competition.Protocol;

namespace X02Competition.Gateway
{
    /// <summary>
    /// LinkskyGateway 模拟服务端：TcpListener + 手写 RFC6455（RawWebSocket.cs）。
    /// （原 HttpListener 方案在 Unity Mono 不可用：IsWebSocketRequest/AcceptWebSocketAsync 均为空存根。）
    /// 多客户端：默认 8 路，控制台固定最高优先级，日志广播、音频单路选举。
    /// 路径必须与 SDK 连接串一致（参与 HMAC 签名）：/api/V1/open-portal/app/wss/agent-sdk
    /// </summary>
    public sealed class LinkskyGatewayServer : IGatewayPort
    {
        public string AgentId = "agent-001";
        /// <summary>WS 路径（= SDK url 的 path 部分，参与签名校验，必须一致）。</summary>
        public string Path = "/api/V1/open-portal/app/wss/agent-sdk";
        /// <summary>严格鉴权：true 校验 HMAC 签名+时间戳+凭证；false 宽松放行（仅解析 CallbackType）。</summary>
        public bool StrictAuth;
        public string AppId;
        public string AppKey;
        public string AppSecret;
        /// <summary>时间戳最大偏移（秒），默认 5 分钟。</summary>
        public double MaxClockSkewSec = 300;
        public int MaxSessions = 8;
        public const int ControllerPriority = 1000;
        public const int DefaultAgentPriority = 50;
        public event Action RoutingChanged;
        long _revision;
        GatewaySession _recordingSession;
        string _recordingEvent, _recordingItem;
        bool _voiceResponseBlocked;
        string _allowedAudioEvent;

        /// <summary>监听线程回调：会话建立（此时应立即绑定 Runtime 并 SendRobotOnline）。</summary>
        public event Action<GatewaySession> SessionOpened;
        /// <summary>会话关闭（断线 / 出错 / 服务停止）。</summary>
        public event Action<GatewaySession> SessionClosed;
        public event Action<string> Log;

        TcpListener _listener;
        Thread _acceptThread;
        volatile bool _running;
        readonly object _lock = new object();
        readonly List<GatewaySession> _sessions = new List<GatewaySession>();
        readonly byte[] _handshakeBuf = new byte[8192];   // 仅 accept 线程使用

        public bool IsRunning => _running;

        public IReadOnlyList<GatewaySession> Sessions
        {
            get { lock (_lock) return _sessions.ToArray(); }
        }

        GatewaySession Owner(bool audio)
        {
            GatewaySession best = null;
            foreach (var session in _sessions)
            {
                if (!session.IsOpen || session.Role == "observer" ||
                    (audio ? !session.AudioEnabled : !session.ControlEnabled)) continue;
                // Earlier connections win ties, avoiding reconnect-driven oscillation.
                if (best == null || session.Priority > best.Priority) best = session;
            }
            return best;
        }

        public GatewaySession ControlOwner { get { lock (_lock) return Owner(false); } }
        public GatewaySession AudioOwner { get { lock (_lock) return Owner(true); } }
        public GatewaySession ManagementOwner
        {
            get { lock (_lock) return _sessions.Find(s => s.IsOpen && s.Role == "controller"); }
        }

        public bool CanDispatch(GatewaySession session, InboundFrame frame)
        {
            lock (_lock)
            {
                if (!session.IsOpen || session.Role == "observer") return false;
                var control = Owner(false);
                if (frame.Type == LinkskyTypes.XlmResponseSkill || frame.Type == LinkskyTypes.XlmResponseInterrupt)
                {
                    if (!ReferenceEquals(session, control)) return false;
                    if (frame.Type == LinkskyTypes.XlmResponseInterrupt) _voiceResponseBlocked = true;
                    return true;
                }
                if (frame.Type == LinkskyTypes.Error) return true;
                if (session.Role == "controller" && ReferenceEquals(session, control)) return true;
                return ReferenceEquals(session, Owner(true)) && !_voiceResponseBlocked &&
                    (_allowedAudioEvent == null || frame.EventId == _allowedAudioEvent);
            }
        }

        public bool SetPriority(GatewaySession sender, string targetCid, int priority, out string error)
        {
            lock (_lock)
            {
                if (sender.Role != "controller" || !ReferenceEquals(sender, ManagementOwner))
                { error = "only the active controller can change priorities"; return false; }
                var target = _sessions.Find(s => s.IsOpen && s.RobotCid == targetCid);
                if (target == null) { error = "session no longer exists"; return false; }
                if (target.Role == "controller") { error = "controller priority is fixed"; return false; }
                if (priority < 0 || priority >= ControllerPriority)
                { error = "priority must be an integer from 0 to 999"; return false; }
                target.Priority = priority;
            }
            error = null;
            PublishRouting();
            return true;
        }

        public bool SetControlEnabled(GatewaySession sender, bool? enabled, out string error)
        {
            lock (_lock)
            {
                if (!sender.IsOpen || sender.Role != "controller" || !_sessions.Contains(sender))
                { error = "only a controller can change its own takeover setting"; return false; }
                if (!enabled.HasValue)
                { error = "enabled must be a JSON boolean"; return false; }
                sender.ControlEnabled = enabled.Value;
            }
            error = null;
            PublishRouting();
            return true;
        }

        public string RejectionReason(GatewaySession sender, InboundFrame frame)
        {
            lock (_lock)
            {
                if (sender.Role == "observer") return "observer sessions are read-only";
                if (frame.Type == LinkskyTypes.XlmResponseSkill || frame.Type == LinkskyTypes.XlmResponseInterrupt)
                {
                    var owner = Owner(false);
                    return "action control belongs to " + (owner == null ? "no session" :
                        owner.ClientName + " (priority " + owner.Priority + ")") +
                        "; release console takeover to let the voice Agent execute skills";
                }
                if (_voiceResponseBlocked) return "previous voice response was interrupted; wait for a new recording";
                if (ReferenceEquals(sender, Owner(true)) && _allowedAudioEvent != null &&
                    frame.EventId != _allowedAudioEvent) return "stale voice response event";
                return "session is not the active voice or control owner";
            }
        }

        void PublishRouting()
        {
            lock (_lock)
            {
                _revision++;
                var control = Owner(false);
                var audio = Owner(true);
                var rows = new List<Dictionary<string, object>>();
                foreach (var session in _sessions)
                    if (session.IsOpen) rows.Add(new Dictionary<string, object>
                    {
                        ["robotCid"] = session.RobotCid, ["name"] = session.ClientName,
                        ["role"] = session.Role, ["priority"] = session.Priority,
                        ["audioEnabled"] = session.AudioEnabled,
                        ["controlEnabled"] = session.ControlEnabled,
                        ["controlActive"] = ReferenceEquals(session, control),
                        ["audioActive"] = ReferenceEquals(session, audio),
                    });
                foreach (var session in _sessions)
                    if (session.IsOpen) session.SendSessionState(_revision, rows,
                        control?.RobotCid ?? "", audio?.RobotCid ?? "", ManagementOwner?.RobotCid ?? "");
            }
            RoutingChanged?.Invoke();
        }

        /// <summary>Main-thread voice handoff: complete old input and suppress its unfinished reply.</summary>
        public void ResetAudioRouting()
        {
            var previous = _recordingSession;
            _recordingSession = null;
            if (previous != null && previous.IsOpen)
                previous.SendAudioCommit(_recordingEvent, _recordingItem);
            lock (_lock)
            {
                _voiceResponseBlocked = false;
                _allowedAudioEvent = null;
            }
        }

        public void SendAudioStart(string eventId, string itemId)
        {
            _recordingSession = AudioOwner;
            _recordingEvent = eventId;
            _recordingItem = itemId;
            lock (_lock)
            {
                _voiceResponseBlocked = false;
                _allowedAudioEvent = eventId;
            }
            _recordingSession?.SendAudioStart(eventId, itemId);
        }
        public void SendAudioAppend(string eventId, string itemId, byte[] pcm)
        {
            if (ReferenceEquals(_recordingSession, AudioOwner))
                _recordingSession?.SendAudioAppend(eventId, itemId, pcm);
        }
        public void SendAudioCommit(string eventId, string itemId)
        {
            if (ReferenceEquals(_recordingSession, AudioOwner))
                _recordingSession?.SendAudioCommit(eventId, itemId);
            _recordingSession = null;
        }
        public void SendState(string name, string value)
        { foreach (var session in Sessions) session.SendState(name, value); }
        public void SendSkillState(string name, string state, string detail)
        { foreach (var session in Sessions) session.SendSkillState(name, state, detail); }
        public void SendRobotOnline(Dictionary<string, object> meta, string callbackType)
        { foreach (var session in Sessions) session.SendRobotOnline(meta, session.CallbackType); }
        public void SendRobotOffline()
        { foreach (var session in Sessions) session.SendRobotOffline(); }

        public void Start(int port)
        {
            if (_running) throw new InvalidOperationException("server already running");
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _running = true;
            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "gw-accept"
            };
            _acceptThread.Start();
            Log?.Invoke("gw  listening on ws://localhost:" + port + Path);
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try { _listener.Stop(); } catch { }
            GatewaySession[] snapshot;
            lock (_lock)
            {
                snapshot = _sessions.ToArray();
                _sessions.Clear();
            }
            foreach (var s in snapshot) s.Close(WebSocketCloseStatus.NormalClosure, "server stop");
        }

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    if (!_running) break;
                    continue;
                }

                try
                {
                    HandleConnection(client);
                }
                catch (Exception e)
                {
                    // 完整异常信息（类型+堆栈），否则后台线程异常只能看到一句 Message 难以定位
                    Log?.Invoke("gw  accept error: " + e.GetType().Name + ": " + e.Message +
                                "\n" + e.StackTrace);
                    try { client.Close(); } catch { }
                }
            }
        }

        void HandleConnection(TcpClient client)
        {
            var io = client.GetStream();
            io.ReadTimeout = 10000;   // 握手阶段 10s 超时
            var req = RawWs.ReadRequest(io, _handshakeBuf, out var leftover);

            var path = req.Path;
            if (!string.Equals(path.TrimEnd('/'), Path.TrimEnd('/'), StringComparison.Ordinal))
            {
                RawWs.WritePlainResponse(io, 404, "Not Found", "not found");
                client.Close();
                return;
            }
            if (!RawWs.IsWebSocketUpgrade(req))
            {
                RawWs.WritePlainResponse(io, 400, "Bad Request", "websocket required");
                client.Close();
                return;
            }

            lock (_lock)
            {
                if (_sessions.Count >= MaxSessions)
                {
                    Log?.Invoke("gw  reject: max sessions reached");
                    RawWs.WritePlainResponse(io, 503, "Service Unavailable", "busy");
                    client.Close();
                    return;
                }
            }

            var appId = Header(req, AuthVerifier.HeaderAppId);
            var appKey = Header(req, AuthVerifier.HeaderAppKey);
            var ts = Header(req, AuthVerifier.HeaderTimestamp);
            var nonce = Header(req, AuthVerifier.HeaderNonce);
            var sig = Header(req, AuthVerifier.HeaderSignature);
            var callbackTypesRaw = Header(req, AuthVerifier.HeaderCallbackTypes);
            var callbackType = CallbackTypes.ParseFirst(callbackTypesRaw);
            var role = (Header(req, "X-Client-Role") ?? "agent").ToLowerInvariant();
            var audioHeader = Header(req, "X-Audio-Enabled");
            var controlHeader = Header(req, "X-Control-Enabled");
            if ((role != "controller" && role != "agent" && role != "observer") ||
                (audioHeader != null && audioHeader != "true" && audioHeader != "false") ||
                (controlHeader != null && controlHeader != "true" && controlHeader != "false"))
            {
                RawWs.WritePlainResponse(io, 400, "Bad Request", "invalid client role or audio/control flag");
                client.Close();
                return;
            }
            var audioEnabled = role != "observer" && (audioHeader == "true" || (audioHeader == null && role == "agent"));
            var controlEnabled = role != "observer" && controlHeader != "false";
            var clientName = Header(req, "X-Client-Name") ?? appId ?? role;
            if (clientName.Length > 64) clientName = clientName.Substring(0, 64);

            if (StrictAuth && !AuthVerifier.Verify(path, appId, appKey, ts, nonce, sig,
                    AppSecret, AppId, AppKey, MaxClockSkewSec))
            {
                Log?.Invoke("gw  auth failed for app=" + appId);
                RawWs.WritePlainResponse(io, 401, "Unauthorized", "unauthorized");
                client.Close();
                return;
            }

            // —— 101 升级为 WebSocket ——
            io.ReadTimeout = Timeout.Infinite;
            RawWs.WriteSwitchingProtocols(io, req.Headers["Sec-WebSocket-Key"]);
            var conn = new ServerWsConnection(client, io, _handshakeBuf, leftover);

            var session = new GatewaySession(this, conn, callbackType, role, audioEnabled, clientName, controlEnabled);
            session.FrameLogged += OnSessionLog;
            session.Closed += OnSessionClosed;
            lock (_lock) _sessions.Add(session);
            Log?.Invoke("gw  session opened, callbackType=" + callbackType);
            try
            {
                SessionOpened?.Invoke(session); // 装配层在此绑定 Runtime + SendRobotOnline
                session.StartThreads();
                PublishRouting();
            }
            catch (Exception e)
            {
                // 装配层异常：绝不让会话泄漏（否则单客户端模式永久 503）
                Log?.Invoke("gw  session open failed: " + e.GetType().Name + ": " + e.Message +
                            "\n" + e.StackTrace);
                session.Close(WebSocketCloseStatus.InternalServerError, "session open failed");
            }
        }

        void OnSessionClosed(GatewaySession s)
        {
            lock (_lock) _sessions.Remove(s);
            Log?.Invoke("gw  session removed, active=" + _sessions.Count);
            SessionClosed?.Invoke(s);
            PublishRouting();
        }

        void OnSessionLog(string line) => Log?.Invoke(line);

        static string Header(RawHttpRequest req, string name)
        {
            string v;
            return req.Headers.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>audio_request.start 是否携带 itemId：仅 audio2tts（对齐 SDK handle_audio_request_start 分支）。</summary>
        internal bool IncludeItemIdFor(string callbackType)
        {
            return callbackType == CallbackTypes.Audio2Tts;
        }
    }
}
