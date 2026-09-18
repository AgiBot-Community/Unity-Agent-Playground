using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using X02Competition.Gateway;
using X02Competition.Protocol;

namespace X02Competition.Tests.PlayMode
{
    /// <summary>
    /// 网关联调用例（真实 WS 服务端 + FakeAgent 客户端，覆盖设计文档 §3 用例）：
    ///   T1 连接 → robot_state.sync(online)
    ///   T2 签名错误 → HTTP 401 拒绝
    ///   T3 全回路 → 三段式上行 + Agent 应答帧分发到 IRobotRuntime（含顺序断言）
    ///   T7 断线重连 → 会话释放 + 重连后重发 sync
    /// </summary>
    public class GatewayLoopTests
    {
        const string BasePath = "/api/V1/open-portal/app/wss/agent-sdk";
        const string AppId = "test-app";
        const string AppKey = "test-key";
        const string AppSecret = "test-secret";

        LinkskyGatewayServer _server;
        StubRuntime _runtime;

        [SetUp]
        public void SetUp()
        {
            _server = new LinkskyGatewayServer
            {
                Path = BasePath,
                StrictAuth = false,
                AppId = AppId,
                AppKey = AppKey,
                AppSecret = AppSecret,
            };
            _runtime = new StubRuntime();
            _server.SessionOpened += s =>
            {
                s.Runtime = _runtime;
                s.SendRobotOnline(new Dictionary<string, object>
                {
                    ["def.wakeupWord"] = "灵犀",
                    ["def.robotName"] = "X2",
                }, s.CallbackType);
            };
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Stop();
            _server = null;
        }

        // ------------------------------------------------------------------
        // T1：连接 → robot_state.sync(online) → Runtime.OnAgentConnected
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T1_Connect_ReceivesRobotStateSyncOnline()
        {
            yield return null;
            _server.Start(9401);
            var agent = new FakeAgent(9401, BasePath, AppId, AppKey, AppSecret, validSignature: true);
            Assert.IsTrue(agent.Connect(3000), "FakeAgent 应能连接");

            var sync = WaitForFrame(agent, LinkskyTypes.RobotStateSync, 5000, PumpSessions);
            Assert.IsNotNull(sync, "应收到 robot_state.sync");
            Assert.AreEqual("online", (string)sync["state"]);
            Assert.AreEqual("audio2tts", (string)sync["callbackType"]);
            Assert.AreEqual("灵犀", (string)sync["agentMeta"]["def.wakeupWord"]);
            Assert.AreEqual("X2", (string)sync["agentMeta"]["def.robotName"]);

            Assert.IsTrue(WaitUntil(() => _runtime.Has("OnAgentConnected"), 5000, PumpSessions),
                "主线程泵执行后 Runtime 应收到 OnAgentConnected");
            agent.Close();
        }

        // ------------------------------------------------------------------
        // T2：严格鉴权 + 错误签名 → HTTP 401，连接被拒
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T2_StrictAuth_BadSignature_Rejected()
        {
            yield return null;
            _server.StrictAuth = true;
            _server.Start(9402);
            var agent = new FakeAgent(9402, BasePath, AppId, AppKey, AppSecret, validSignature: false);
            Assert.IsFalse(agent.Connect(3000), "签名错误应被 401 拒绝");
            Assert.AreEqual(0, _server.Sessions.Count, "不应建立会话");
            agent.Close();
        }

        [UnityTest]
        public IEnumerator T2b_StrictAuth_ValidSignature_Accepted()
        {
            yield return null;
            _server.StrictAuth = true;
            _server.Start(9403);
            var agent = new FakeAgent(9403, BasePath, AppId, AppKey, AppSecret, validSignature: true);
            Assert.IsTrue(agent.Connect(3000), "正确签名应通过");
            Assert.IsNotNull(WaitForFrame(agent, LinkskyTypes.RobotStateSync, 5000, PumpSessions));
            agent.Close();
        }

        // ------------------------------------------------------------------
        // T3：全回路。网关三段式上行 → Agent 按官方时序应答 → Runtime 回调有序到达
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T3_FullLoop_AudioUpstream_And_AgentResponses_DispatchedInOrder()
        {
            yield return null;
            _server.Start(9404);
            var agent = new FakeAgent(9404, BasePath, AppId, AppKey, AppSecret, validSignature: true);
            Assert.IsTrue(agent.Connect(3000));
            Assert.IsNotNull(WaitForFrame(agent, LinkskyTypes.RobotStateSync, 5000, PumpSessions),
                "先等 sync");

            GatewaySession session = null;
            Assert.IsTrue(WaitUntil(() =>
            {
                var list = _server.Sessions;
                if (list.Count > 0) { session = list[0]; return true; }
                return false;
            }, 5000, PumpSessions), "会话应已建立");

            // ---- 上行：机器人侧（模拟 VAD）发三段式 ----
            var port = (IGatewayPort)session;
            var pcm = new byte[3200]; // 100ms 静音数据（内容不重要，验证帧序与载荷）
            port.SendAudioStart("evt-t3", "item-t3");
            port.SendAudioAppend("evt-t3", "item-t3", pcm);
            port.SendAudioAppend("evt-t3", "item-t3", pcm);
            port.SendAudioCommit("evt-t3", "item-t3");

            var start = WaitForFrame(agent, LinkskyTypes.AudioRequestStart, 5000, PumpSessions);
            Assert.IsNotNull(start, "应收到 audio_request.start");
            Assert.AreEqual("evt-t3", (string)start["eventId"]);
            Assert.AreEqual("item-t3", (string)start["itemId"]); // audio2tts 携带 itemId

            var append = WaitForFrame(agent, LinkskyTypes.AudioRequestAppend, 5000, PumpSessions);
            Assert.IsNotNull(append, "应收到 audio_request.append");
            var audioB64 = (string)append["audio"];
            Assert.AreEqual(pcm.Length, (int)append["audioLen"]);
            Assert.AreEqual(pcm.Length, Convert.FromBase64String(audioB64).Length);

            var commit = WaitForFrame(agent, LinkskyTypes.AudioRequestCommit, 5000, PumpSessions);
            Assert.IsNotNull(commit, "应收到 audio_request.commit");

            // ---- 下行：Agent 按官方时序应答（interrupt → llm → tts） ----
            agent.Send(AgentFrame(LinkskyTypes.AsrResponseFinal, "evt-t3", "\"text\":\"你好呀\""));
            agent.Send(AgentFrame(LinkskyTypes.XlmResponseInterrupt, "evt-t3",
                "\"interruptType\":\"chat\""));
            agent.Send(AgentFrame(LinkskyTypes.LlmResponseItemDelta, "evt-t3",
                "\"itemId\":\"item-t3\",\"text\":\"你好\""));
            agent.Send(AgentFrame(LinkskyTypes.LlmResponseItemDone, "evt-t3",
                "\"itemId\":\"item-t3\""));
            agent.Send(AgentFrame(LinkskyTypes.LlmResponseDone, "evt-t3"));
            agent.Send(AgentFrame(LinkskyTypes.TtsResponseItemDelta, "evt-t3",
                "\"itemId\":\"item-t3\",\"audio\":\"" + Convert.ToBase64String(pcm) +
                "\",\"audioLen\":" + pcm.Length));
            agent.Send(AgentFrame(LinkskyTypes.TtsResponseItemDone, "evt-t3", "\"itemId\":\"item-t3\""));
            agent.Send(AgentFrame(LinkskyTypes.TtsResponseDone, "evt-t3"));

            Assert.IsTrue(WaitUntil(() => _runtime.Has("OnAgentRoundDone"), 5000, PumpSessions),
                "Runtime 应收到本轮所有回调");

            // 顺序断言（官方对话时序）
            _runtime.AssertOrder("OnAgentAsrText", "OnAgentInterrupt", "OnAgentLlmDelta",
                "OnAgentTtsDelta", "OnAgentTtsDone", "OnAgentRoundDone");

            // TTS PCM 载荷完整性
            var tts = _runtime.GetCall("OnAgentTtsDelta");
            Assert.AreEqual(pcm.Length, ((byte[])tts.Args[2]).Length);

            agent.Close();
        }

        // ------------------------------------------------------------------
        // T7：断线 → 会话释放 → 重连 → 重发 robot_state.sync
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T7_Reconnect_ResendsRobotStateSync()
        {
            yield return null;
            _server.Start(9405);
            var agent1 = new FakeAgent(9405, BasePath, AppId, AppKey, AppSecret, validSignature: true);
            Assert.IsTrue(agent1.Connect(3000));
            Assert.IsNotNull(WaitForFrame(agent1, LinkskyTypes.RobotStateSync, 5000, PumpSessions));
            var cid1 = _server.Sessions[0].RobotCid;

            // 断开（SDK 侧 3s 后重连；测试直接模拟重连）
            agent1.Close();
            Assert.IsTrue(WaitUntil(() => _server.Sessions.Count == 0, 5000, PumpSessions),
                "会话应已释放（单客户端槽位归还）");
            Assert.IsTrue(WaitUntil(() => _runtime.Has("OnAgentDisconnected"), 5000, PumpSessions),
                "Runtime 应收到 OnAgentDisconnected");

            var agent2 = new FakeAgent(9405, BasePath, AppId, AppKey, AppSecret, validSignature: true);
            Assert.IsTrue(agent2.Connect(5000), "重连应成功（槽位已释放）");
            var sync2 = WaitForFrame(agent2, LinkskyTypes.RobotStateSync, 5000, PumpSessions);
            Assert.IsNotNull(sync2, "重连后必须重发 robot_state.sync（协议 §5）");
            Assert.AreEqual("online", (string)sync2["state"]);

            // 新会话 = 新 robotCid
            Assert.IsTrue(WaitUntil(() =>
            {
                var list = _server.Sessions;
                return list.Count > 0 && list[0].RobotCid != cid1;
            }, 5000, PumpSessions), "重连会话应为新 robotCid");

            agent2.Close();
        }

        // ------------------------------------------------------------------
        // 单客户端策略：已有会话未释放时新连接被 503 拒绝
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T7b_SecondClient_RejectedWhileSessionActive()
        {
            yield return null;
            _server.Start(9406);
            var agent1 = new FakeAgent(9406, BasePath, AppId, AppKey, AppSecret, validSignature: true);
            Assert.IsTrue(agent1.Connect(3000));
            Assert.IsNotNull(WaitForFrame(agent1, LinkskyTypes.RobotStateSync, 5000, PumpSessions));

            var agent2 = new FakeAgent(9406, BasePath, AppId, AppKey, AppSecret, validSignature: true);
            Assert.IsFalse(agent2.Connect(3000), "第二客户端应被 503 拒绝（单客户端策略）");
            agent1.Close();
        }

        // ==================================================================
        // 辅助
        // ==================================================================

        void PumpSessions()
        {
            var list = _server.Sessions;
            for (int i = 0; i < list.Count; i++) list[i].ExecutePendingActions();
        }

        Newtonsoft.Json.Linq.JObject WaitForFrame(FakeAgent agent, string type, int timeoutMs,
            Action pump)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                pump?.Invoke();
                var f = agent.TakeFrame(type);
                if (f != null) return f;
                Thread.Sleep(20);
            }
            return null;
        }

        bool WaitUntil(Func<bool> cond, int timeoutMs, Action pump)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                pump?.Invoke();
                if (cond()) return true;
                Thread.Sleep(20);
            }
            return cond();
        }

        static string AgentFrame(string type, string eventId, string extraJson = null)
        {
            return "{\"type\":\"" + type + "\",\"agentId\":\"" + AppId + "\"," +
                "\"agentMode\":\"passive\",\"robotCid\":\"cid-x\",\"cid\":\"cid-x\"," +
                "\"eventId\":\"" + eventId + "\"" +
                (extraJson != null ? "," + extraJson : "") + "}";
        }

        // ---------------- FakeAgent：官方 SDK 客户端行为的最小复刻 ----------------

        class FakeAgent
        {
            readonly int _port;
            readonly string _path;
            readonly string _appId, _appKey, _appSecret;
            readonly bool _validSignature;

            ClientWebSocket _ws;
            Thread _recvThread;
            volatile bool _running;
            readonly ConcurrentQueue<string> _raw = new ConcurrentQueue<string>();
            readonly List<Newtonsoft.Json.Linq.JObject> _frames =
                new List<Newtonsoft.Json.Linq.JObject>();
            readonly object _lock = new object();

            public FakeAgent(int port, string path, string appId, string appKey, string appSecret,
                bool validSignature)
            {
                _port = port;
                _path = path;
                _appId = appId;
                _appKey = appKey;
                _appSecret = appSecret;
                _validSignature = validSignature;
            }

            public bool Connect(int timeoutMs)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                    var nonce = "nonce-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    var sig = _validSignature
                        ? AuthVerifier.ComputeSignature(_appSecret, "GET", _path, ts, nonce)
                        : "deadbeef";
                    _ws.Options.SetRequestHeader(AuthVerifier.HeaderAppId, _appId);
                    _ws.Options.SetRequestHeader(AuthVerifier.HeaderAppKey, _appKey);
                    _ws.Options.SetRequestHeader(AuthVerifier.HeaderTimestamp, ts);
                    _ws.Options.SetRequestHeader(AuthVerifier.HeaderNonce, nonce);
                    _ws.Options.SetRequestHeader(AuthVerifier.HeaderSignature, sig);
                    _ws.Options.SetRequestHeader(AuthVerifier.HeaderCallbackTypes, "[\"audio2tts\"]");

                    var uri = new Uri("ws://localhost:" + _port + _path);
                    var task = _ws.ConnectAsync(uri, CancellationToken.None);
                    if (!task.Wait(timeoutMs)) return false;
                    if (_ws.State != WebSocketState.Open) return false;

                    _running = true;
                    _recvThread = new Thread(RecvLoop) { IsBackground = true };
                    _recvThread.Start();
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            void RecvLoop()
            {
                var buffer = new byte[256 * 1024];
                var sb = new StringBuilder();
                try
                {
                    while (_running)
                    {
                        sb.Clear();
                        WebSocketReceiveResult r;
                        do
                        {
                            r = _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)
                                .GetAwaiter().GetResult();
                            if (r.MessageType == WebSocketMessageType.Close) return;
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                        } while (!r.EndOfMessage);
                        var text = sb.ToString();
                        _raw.Enqueue(text);
                        lock (_lock)
                        {
                            try { _frames.Add(Newtonsoft.Json.Linq.JObject.Parse(text)); }
                            catch { }
                        }
                    }
                }
                catch (Exception) { /* 断线 */ }
            }

            public void Send(string json)
            {
                if (_ws == null || _ws.State != WebSocketState.Open) return;
                var bytes = Encoding.UTF8.GetBytes(json);
                _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    CancellationToken.None).Wait(2000);
            }

            public Newtonsoft.Json.Linq.JObject TakeFrame(string type)
            {
                lock (_lock)
                {
                    for (int i = 0; i < _frames.Count; i++)
                    {
                        if ((string)_frames[i]["type"] == type)
                        {
                            var f = _frames[i];
                            _frames.RemoveAt(i);
                            return f;
                        }
                    }
                }
                return null;
            }

            public void Close()
            {
                _running = false;
                try
                {
                    if (_ws != null && _ws.State == WebSocketState.Open)
                        _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "test done",
                            CancellationToken.None).Wait(1000);
                }
                catch { }
                try { _ws?.Dispose(); } catch { }
            }
        }

        // ---------------- StubRuntime：录制 IRobotRuntime 调用 ----------------

        class StubRuntime : IRobotRuntime
        {
            public class Call
            {
                public string Method;
                public object[] Args;
            }

            readonly List<Call> _calls = new List<Call>();
            readonly object _lock = new object();

            public void Record(string method, params object[] args)
            {
                lock (_lock) _calls.Add(new Call { Method = method, Args = args });
            }

            public bool Has(string method)
            {
                lock (_lock) return _calls.Exists(c => c.Method == method);
            }

            public Call GetCall(string method)
            {
                lock (_lock) return _calls.Find(c => c.Method == method);
            }

            /// <summary>断言方法按给定先后顺序出现（允许中间夹其他调用）。</summary>
            public void AssertOrder(params string[] methods)
            {
                lock (_lock)
                {
                    int idx = -1;
                    foreach (var m in methods)
                    {
                        var next = _calls.FindIndex(idx + 1, c => c.Method == m);
                        Assert.IsTrue(next > idx, "调用顺序错误：期望 " + m + " 出现在 " + idx +
                            " 之后。实际序列: " + Sequence());
                        idx = next;
                    }
                }
            }

            string Sequence()
            {
                lock (_lock) return string.Join(" -> ", _calls.ConvertAll(c => c.Method).ToArray());
            }

            // IRobotRuntime
            public void OnAgentConnected(string agentId, string callbackType) =>
                Record("OnAgentConnected", agentId, callbackType);
            public void OnAgentAsrText(string eventId, bool isFinal, string text) =>
                Record("OnAgentAsrText", eventId, isFinal, text);
            public void OnAgentLlmDelta(string eventId, string itemId, string textDelta) =>
                Record("OnAgentLlmDelta", eventId, itemId, textDelta);
            public void OnAgentTtsDelta(string eventId, string itemId, byte[] pcm) =>
                Record("OnAgentTtsDelta", eventId, itemId, pcm);
            public void OnAgentTtsDone(string eventId, string itemId) =>
                Record("OnAgentTtsDone", eventId, itemId);
            public void OnAgentRoundDone(string eventId) => Record("OnAgentRoundDone", eventId);
            public void OnAgentSkill(string eventId, string itemId, string skillType,
                string skillName, Dictionary<string, object> skillParam) =>
                Record("OnAgentSkill", eventId, itemId, skillType, skillName, skillParam);
            public void OnAgentInterrupt(string eventId, string interruptType, string tips) =>
                Record("OnAgentInterrupt", eventId, interruptType, tips);
            public void OnAgentError(string eventId, int code, string msg) =>
                Record("OnAgentError", eventId, code, msg);
            public void OnAgentDisconnected() => Record("OnAgentDisconnected");
        }
    }
}
