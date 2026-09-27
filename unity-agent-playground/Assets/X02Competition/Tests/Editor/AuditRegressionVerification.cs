#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using X02Competition.Bootstrap;
using X02Competition.Gateway;
using X02Competition.Protocol;
using X02Competition.Robot;

// Unity -batchmode -projectPath ... -executeMethod AuditRegressionVerification.Run
// Enters real Play Mode; uses loopback sockets, AudioSource/AudioClip and Unity's log callbacks.
[InitializeOnLoad]
public static class AuditRegressionVerification
{
    const string Pending = "X02.AuditRegression.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int _checks;

    static AuditRegressionVerification()
    {
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += VerifyWhenPlaying;
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.update -= VerifyWhenPlaying;
        EditorApplication.update += VerifyWhenPlaying;
        EditorApplication.EnterPlaymode();
    }

    static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    static void Tick(object target, string method = "Update") =>
        target.GetType().GetMethod(method, Private).Invoke(target, null);

    static void VerifyWhenPlaying()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.update -= VerifyWhenPlaying;
        SessionState.SetBool(Pending, false);
        var exitCode = 0;
        var root = new GameObject("Audit regression");
        try
        {
            VerifyAudio(root);
            VerifyMovement(root);
            VerifyMessageBoundaries();
            VerifyMessageLimit();
            VerifySlowPeerClose();
            VerifyLogForwarding(root);
            MultiSessionVerification.Verify();
            Debug.Log("AUDIT_REGRESSION_PASSED: " + _checks +
                " checks; audio tails, bounded input, movement cancellation, WS fragmentation/size/close, " +
                "Unity log levels/stacks/threading/backpressure/reconnect/no-feedback.");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            exitCode = 1;
        }
        finally
        {
            LocomotionRegistry.Agent = null;
            UnityEngine.Object.DestroyImmediate(root);
            EditorApplication.Exit(exitCode);
        }
    }

    static void VerifyAudio(GameObject root)
    {
        var tts = root.AddComponent<TtsStreamPlayer>();
        tts.Append("short", "item", new byte[3200]);
        tts.MarkRoundDone();
        Tick(tts);
        Require(tts.Source.isPlaying, "Completed 100ms speech must play.");
        tts.Source.timeSamples = 1600;
        Tick(tts);
        Require(!tts.IsStreamingActive && tts.Source.clip == null, "Short speech must release VAD.");

        tts.Append("tail", "item", new byte[6400]);
        Tick(tts);
        tts.Source.timeSamples = 2800;
        Tick(tts);
        Require(!tts.Source.isPlaying && tts.IsStreamingActive, "Underrun must pause without releasing VAD.");
        tts.MarkRoundDone();
        Tick(tts);
        Require(tts.Source.isPlaying, "Completed short tail must resume.");
        tts.Source.timeSamples = 3200;
        Tick(tts);
        Require(!tts.IsStreamingActive, "Completed tail must finish.");

        var queue = new AudioFrameQueue(10);
        for (var i = 0; i < 600; i++) queue.Enqueue(new[] { (float)i });
        var count = 0;
        while (queue.TryRead(out var frame))
        {
            Require(frame[0] == 590 + count, "Bounded input must retain newest frames in order.");
            count++;
        }
        Require(count == 10, "Input queue must stay bounded.");

        var inputClip = AudioClip.Create("125ms", 2000, 1, 16000, false);
        inputClip.SetData(Enumerable.Repeat(0.25f, 2000).ToArray(), 0);
        var clip = root.AddComponent<ClipInputSource>();
        clip.Clip = inputClip;
        clip.Begin();
        typeof(ClipInputSource).GetField("_clockSec", Private).SetValue(clip, 0.2);
        Tick(clip);
        Require(!clip.IsRunning, "Partial clip must end.");
        Require(clip.TryReadFrame(out var first) && first.Length == 1600, "First clip frame missing.");
        Require(clip.TryReadFrame(out var tail) && tail[399] == 0.25f && tail[400] == 0,
            "Final partial frame must be padded and remain readable after completion.");
        clip.Loop = true;
        clip.Replay();
        for (var i = 0; i < 3; i++)
        {
            typeof(ClipInputSource).GetField("_clockSec", Private).SetValue(clip, 0.2);
            Tick(clip);
            Require(clip.TryReadFrame(out _) && clip.TryReadFrame(out _) && clip.IsRunning,
                "Partial clip must loop.");
        }
        clip.End();
        UnityEngine.Object.DestroyImmediate(inputClip);

        var input = new BufferedInput();
        var port = new RecordingPort();
        var runtime = root.AddComponent<VirtualRobotRuntime>();
        runtime.Input = input;
        runtime.Tts = tts;
        input.Frames.Enqueue(new float[1600]);
        Tick(runtime);
        Require(input.Frames.Count == 0, "Disconnected runtime must discard audio.");
        input.Frames.Enqueue(new float[1600]);
        runtime.Bind(port);
        Require(input.Frames.Count == 0, "Bind must discard pre-session audio.");
        input.Frames.Enqueue(Enumerable.Repeat(0.1f, 1600).ToArray());
        input.IsRunning = false;
        Tick(runtime);
        Require(string.Join(",", port.Events) == "start,append,commit",
            "A finished input must deliver its final frame and close the VAD round.");
        runtime.Unbind();
    }

    static void VerifyMovement(GameObject root)
    {
        LocomotionRegistry.Agent = new FakeLocomotion(root.transform);
        var loco = root.AddComponent<LocomotionCommander>();
        var results = new List<string>();
        loco.Walk(1, ok => results.Add("walk:" + ok));
        loco.Stop(ok => results.Add("stop:" + ok));
        Tick(loco, "FixedUpdate");
        Tick(loco, "FixedUpdate");
        Require(string.Join(",", results) == "walk:False,stop:True",
            "Replaced movement needs exactly one failed terminal callback.");
        loco.Walk(1, ok => results.Add("walk2:" + ok));
        loco.Turn(90, ok => results.Add("turn:" + ok));
        Require(results.Last() == "walk2:False", "Turn replacement must complete old movement.");
        loco.Stop();
        Require(results.Last() == "turn:False", "Abort without callback must complete old movement.");
        Tick(loco, "FixedUpdate");
    }

    sealed class RawPair : IDisposable
    {
        public readonly TcpClient Client;
        public readonly TcpClient Server;
        public readonly ServerWsConnection Ws;
        public RawPair()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Client = new TcpClient();
            Client.ReceiveBufferSize = 1024;
            Client.Connect((IPEndPoint)listener.LocalEndpoint);
            Server = listener.AcceptTcpClient();
            listener.Stop();
            Server.GetStream().ReadTimeout = 3000;
            Ws = new ServerWsConnection(Server, Server.GetStream(), Array.Empty<byte>(), 0);
        }
        public void Dispose() { Ws.Dispose(); Client.Close(); Server.Close(); }
    }

    static void SendFrame(NetworkStream stream, int opcode, bool final, byte[] payload)
    {
        var head = new List<byte> { (byte)((final ? 128 : 0) | opcode) };
        if (payload.Length < 126) head.Add((byte)(128 | payload.Length));
        else if (payload.Length <= 65535)
        {
            head.Add(254);
            head.Add((byte)(payload.Length >> 8));
            head.Add((byte)payload.Length);
        }
        else
        {
            head.Add(255);
            for (var i = 7; i >= 0; i--) head.Add((byte)((long)payload.Length >> (8 * i)));
        }
        head.AddRange(new byte[4]); // a deterministic zero mask for these test frames
        stream.Write(head.ToArray(), 0, head.Count);
        stream.Write(payload, 0, payload.Length);
    }

    static void VerifyMessageBoundaries()
    {
        using (var pair = new RawPair())
        {
            var stream = pair.Client.GetStream();
            SendFrame(stream, 1, false, Encoding.UTF8.GetBytes("A"));
            SendFrame(stream, 9, true, new byte[] { 1 }); // interleaved ping
            SendFrame(stream, 0, true, Array.Empty<byte>());
            SendFrame(stream, 1, true, Encoding.UTF8.GetBytes("B"));
            var buf = new byte[64];
            var a = pair.Ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
            Require(a.Count == 1 && !a.EndOfMessage && buf[0] == 65, "First fragment changed.");
            var end = pair.Ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
            Require(end.Count == 0 && end.EndOfMessage, "Empty continuation must finish message.");
            var b = pair.Ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
            Require(b.Count == 1 && b.EndOfMessage && buf[0] == 66, "Next message must stay separate.");
            SendFrame(stream, 1, true, Array.Empty<byte>());
            var empty = pair.Ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
            Require(empty.Count == 0 && empty.EndOfMessage, "Empty standalone message missing.");
        }
    }

    static void VerifyMessageLimit()
    {
        using (var pair = new RawPair())
        {
            var writing = Task.Run(() =>
            {
                var half = new byte[ServerWsConnection.MaxMessageBytes / 2];
                SendFrame(pair.Client.GetStream(), 1, false, half);
                SendFrame(pair.Client.GetStream(), 0, false, half);
                SendFrame(pair.Client.GetStream(), 0, true, new byte[1]);
            });
            var buf = new byte[64 * 1024];
            long received = 0;
            WebSocketReceiveResult result;
            do
            {
                result = pair.Ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                received += result.Count;
            } while (result.MessageType != WebSocketMessageType.Close);
            Require(received == ServerWsConnection.MaxMessageBytes, "Message limit rejected legal prefix.");
            Require(result.CloseStatus == WebSocketCloseStatus.MessageTooBig,
                "Fragmented message must reject the first byte beyond the aggregate limit.");
            Require(writing.Wait(3000), "Fragment writer hung.");
        }
    }

    static void VerifySlowPeerClose()
    {
        using (var pair = new RawPair())
        {
            pair.Server.SendBufferSize = 1024;
            var started = new ManualResetEventSlim();
            var writing = Task.Run(() =>
            {
                started.Set();
                try
                {
                    pair.Ws.SendAsync(new ArraySegment<byte>(new byte[8 * 1024 * 1024]),
                        WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            });
            Require(started.Wait(1000), "Slow-peer sender did not start.");
            var sendLock = typeof(ServerWsConnection).GetField("_sendLock", Private).GetValue(pair.Ws);
            Require(SpinWait.SpinUntil(() =>
            {
                if (!Monitor.TryEnter(sendLock)) return true;
                Monitor.Exit(sendLock);
                return false;
            }, 1000), "Sender did not acquire the socket lock.");
            Require(pair.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None)
                .Wait(500), "Slow peer prevented bounded close.");
            Require(writing.Wait(2000), "Close failed to unblock sender.");
            started.Dispose();
        }
    }

    public sealed class AgentSocket : IDisposable
    {
        readonly TcpClient _client = new TcpClient();
        readonly NetworkStream _stream;
        public AgentSocket(int port, string role = "agent", bool audio = true, string name = "test-client",
            bool? control = null)
        {
            _client.Connect(IPAddress.Loopback, port);
            _stream = _client.GetStream();
            _stream.ReadTimeout = 3000;
            var request = Encoding.ASCII.GetBytes(
                "GET /api/V1/open-portal/app/wss/agent-sdk HTTP/1.1\r\nHost: localhost\r\n" +
                "Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\n" +
                "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
                "X-Client-Role: " + role + "\r\nX-Audio-Enabled: " + (audio ? "true" : "false") +
                "\r\nX-Client-Name: " + name +
                (control.HasValue ? "\r\nX-Control-Enabled: " + (control.Value ? "true" : "false") : "") +
                "\r\n\r\n");
            _stream.Write(request, 0, request.Length);
            var header = new StringBuilder();
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                var b = _stream.ReadByte();
                if (b < 0) throw new IOException("Handshake ended early");
                header.Append((char)b);
            }
            Require(header.ToString().StartsWith("HTTP/1.1 101"), "Gateway handshake failed.");
        }
        public bool HasData => _stream.DataAvailable;
        public void Send(JObject frame) => SendFrame(_stream, 1, true, Encoding.UTF8.GetBytes(frame.ToString()));
        public JObject Read(bool includeSessionState = false)
        {
            var op = _stream.ReadByte();
            var len = _stream.ReadByte();
            if (op < 0 || len < 0) throw new IOException("WebSocket ended early");
            Require((op & 15) == 1, "Expected JSON text frame.");
            long length = len & 127;
            if (length == 126) length = (_stream.ReadByte() << 8) | _stream.ReadByte();
            else if (length == 127)
            {
                length = 0;
                for (var i = 0; i < 8; i++) length = (length << 8) | (uint)_stream.ReadByte();
            }
            if (length > 1024 * 1024) throw new IOException("Unexpected test message size");
            var bytes = new byte[(int)length];
            var pos = 0;
            while (pos < bytes.Length)
            {
                var n = _stream.Read(bytes, pos, bytes.Length - pos);
                if (n == 0) throw new IOException("Incomplete frame");
                pos += n;
            }
            var frame = JObject.Parse(Encoding.UTF8.GetString(bytes));
            if (!includeSessionState && (string)frame["type"] == LinkskyTypes.SessionState)
                return Read();
            return frame;
        }
        public void Dispose() => _client.Close();
    }

    static List<JObject> Drain(UnityLogForwarder forwarder, AgentSocket socket)
    {
        var frames = new List<JObject>();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var n = forwarder.FlushPending();
            if (n == 0) return frames;
            for (var i = 0; i < n; i++)
            {
                var frame = socket.Read();
                Require((string)frame["type"] == LinkskyTypes.RuntimeLog, "Log frame type mismatch.");
                frames.Add(frame);
            }
        }
        throw new Exception("Log forwarding fed back into Unity logging.");
    }

    static void VerifyLogForwarding(GameObject root)
    {
        var reset = typeof(UnityLogForwarder).GetMethod("InitializeCapture", BindingFlags.Static | BindingFlags.NonPublic);
        var capture = typeof(UnityLogForwarder).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        reset.Invoke(null, null);
        reset.Invoke(null, null); // must not duplicate subscriptions
        var forwarder = root.AddComponent<UnityLogForwarder>();
        var server = new LinkskyGatewayServer();
        forwarder.Server = server;
        server.Log += line => Debug.Log(line);
        GatewaySession current = null;
        var opened = new AutoResetEvent(false);
        server.SessionOpened += session =>
        {
            current = session;
            session.SendRobotOnline(null, session.CallbackType);
            opened.Set();
        };
        server.Start(0);
        var listener = (TcpListener)typeof(LinkskyGatewayServer).GetField("_listener", Private).GetValue(server);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Debug.Log("audit-before-connect");
            using (var socket = new AgentSocket(port))
            {
                Require(opened.WaitOne(1000), "Session open callback missing.");
                Require((string)socket.Read()["type"] == LinkskyTypes.RobotStateSync, "Online frame missing.");
                Require(Drain(forwarder, socket).Any(f => (string)f["message"] == "audit-before-connect"),
                    "Offline/startup log was not forwarded.");
                Debug.Log("audit-info");
                Debug.LogWarning("audit-warning");
                Debug.LogError("audit-error");
                Debug.Assert(false, "audit-assert");
                try { throw new InvalidOperationException("audit-exception"); }
                catch (Exception error) { Debug.LogException(error); }
                var thread = new Thread(() => Debug.Log("audit-thread"));
                thread.Start();
                Require(thread.Join(1000), "Threaded logging stalled.");
                var logs = Drain(forwarder, socket);
                foreach (var pair in new[] { ("audit-info", "info"), ("audit-warning", "warning"), ("audit-error", "error") })
                {
                    var match = logs.Where(f => (string)f["message"] == pair.Item1).ToArray();
                    Require(match.Length == 1 && (string)match[0]["level"] == pair.Item2,
                        "Unity log severity lost or subscription duplicated: " + pair.Item1);
                }
                Require(logs.Any(f => (string)f["logType"] == "Assert" && (string)f["level"] == "error"),
                    "Unity assertions not forwarded.");
                Require(logs.Any(f => (string)f["logType"] == "Exception" &&
                    ((string)f["stackTrace"]).Contains(nameof(VerifyLogForwarding))), "Exception stack missing.");
                Require(logs.Any(f => (string)f["message"] == "audit-thread" &&
                    (int)f["threadId"] != Thread.CurrentThread.ManagedThreadId), "Worker-thread log missing.");
                Require(logs.All(f => (string)f["source"] == "unity" && (long)f["timestampMs"] > 0 &&
                    (string)f["robotCid"] == current.RobotCid), "Log envelope incomplete.");
                Require(logs.Select(f => (long)f["sequence"]).SequenceEqual(
                    logs.Select(f => (long)f["sequence"]).OrderBy(x => x)), "Log order changed.");

                // Exercise the same callback without printing 300 lines into the batch editor log.
                for (var i = 0; i < 300; i++) capture.Invoke(null, new object[] { "overflow-" + i, "", LogType.Log });
                capture.Invoke(null, new object[] { new string('x', 9000), new string('s', 17000), LogType.Warning });
                Require(UnityLogForwarder.PendingCount == 256, "Offline log buffer is unbounded.");
                var overflow = Drain(forwarder, socket);
                Require(overflow.Any(f => (long)f["droppedCount"] >= 45), "Dropped log count missing.");
                var truncated = overflow.Single(f => (bool)f["truncated"]);
                Require(((string)truncated["message"]).Length == 8192 &&
                    ((string)truncated["stackTrace"]).Length == 16384, "Log size limits failed.");

                // Block only the diagnostic sender; producers must stop at 128 queued records.
                var ws = (ServerWsConnection)typeof(GatewaySession).GetField("_ws", Private).GetValue(current);
                var sendLock = typeof(ServerWsConnection).GetField("_sendLock", Private).GetValue(ws);
                var accepted = 0;
                lock (sendLock)
                {
                    for (var i = 0; i < 300; i++)
                        if (current.TrySendRuntimeLog("info", "Log", "backpressure", "", 1, i, 1, 0, false)) accepted++;
                    Require(accepted <= 129, "Session log queue is unbounded.");
                }
                for (var i = 0; i < accepted; i++) Require((string)socket.Read()["message"] == "backpressure",
                    "Bounded log queue lost an accepted entry.");
                Require(Drain(forwarder, socket).Count == 0, "Sending logs recursively generated more logs.");
            }
            Require(SpinWait.SpinUntil(() => server.Sessions.Count == 0, 2000), "Disconnected session leaked.");
            Debug.LogWarning("audit-between-connections");
            using (var socket = new AgentSocket(port))
            {
                Require(opened.WaitOne(1000), "Reconnect failed.");
                socket.Read();
                Require(Drain(forwarder, socket).Any(f => (string)f["message"] == "audit-between-connections"),
                    "Disconnected diagnostics were lost before reconnect.");
            }
        }
        finally
        {
            server.Stop();
            forwarder.Server = null;
            opened.Dispose();
        }
    }

    sealed class BufferedInput : IAudioInputSource
    {
        public readonly Queue<float[]> Frames = new Queue<float[]>();
        public bool IsRunning { get; set; } = true;
        public void Begin() => IsRunning = true;
        public void End() => IsRunning = false;
        public void DiscardPending() => Frames.Clear();
        public bool TryReadFrame(out float[] frame)
        {
            frame = Frames.Count > 0 ? Frames.Dequeue() : null;
            return frame != null;
        }
    }

    sealed class RecordingPort : IGatewayPort
    {
        public readonly List<string> Events = new List<string>();
        public void SendAudioStart(string e, string i) => Events.Add("start");
        public void SendAudioAppend(string e, string i, byte[] pcm) => Events.Add("append");
        public void SendAudioCommit(string e, string i) => Events.Add("commit");
        public void SendRobotOnline(Dictionary<string, object> meta, string type) { }
        public void SendRobotOffline() { }
        public void SendState(string name, string value) { }
        public void SendSkillState(string name, string state, string detail) { }
    }

    sealed class FakeLocomotion : ILocomotionAgent
    {
        public FakeLocomotion(Transform root) { Root = root; }
        public float Vr { get; set; }
        public float Vd { get; set; }
        public float Wr { get; set; }
        public Transform Root { get; }
        public bool IsSupportPhase() => true;
        public bool SettleAtSupportPhase() => true;
        public bool IsSettled() => true;
        public void SetExternalCommand(bool active) { }
    }
}
#endif
