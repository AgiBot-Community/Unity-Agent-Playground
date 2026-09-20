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
    /// 单客户端策略（默认 MaxSessions=1）：已有会话未释放时新连接 503 拒绝。
    /// 路径必须与 SDK 连接串一致（参与 HMAC 签名）：/api/V1/open-portal/app/wss/agent-sdk
    /// </summary>
    public sealed class LinkskyGatewayServer
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
        /// <summary>最大并发会话数，赛事模式 1。</summary>
        public int MaxSessions = 1;

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

            var session = new GatewaySession(this, conn, callbackType);
            session.FrameLogged += OnSessionLog;
            session.Closed += OnSessionClosed;
            lock (_lock) _sessions.Add(session);
            Log?.Invoke("gw  session opened, callbackType=" + callbackType);
            try
            {
                SessionOpened?.Invoke(session); // 装配层在此绑定 Runtime + SendRobotOnline
                session.StartThreads();
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
