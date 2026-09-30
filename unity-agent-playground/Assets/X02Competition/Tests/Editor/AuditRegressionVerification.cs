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
            VerifyVoiceRounds();
            VerifyMovement(root);
            VerifySkillParameters(root);
            VerifyMessageBoundaries();
            VerifyMessageLimit();
            VerifySlowPeerClose();
            VerifySessionDisposal();
            VerifyCallbackDisposal();
            VerifyStopDuringHandshake();
            VerifyFaultyLogObserver();
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

    static void VerifyVoiceRounds()
    {
        var root = new GameObject("Voice round regression");
        try
        {
            var input = new BufferedInput();
            var port = new RecordingPort();
            var runtime = root.AddComponent<VirtualRobotRuntime>();
            var tts = root.AddComponent<TtsStreamPlayer>();
            runtime.Input = input;
            runtime.Tts = tts;
            runtime.StatePublishInterval = 0;
            runtime.Bind(port);
            var loud = Enumerable.Repeat(0.1f, 1600).ToArray();
            runtime.OnAgentLlmDelta("greeting", "reply", "hello");
            input.Frames.Enqueue(loud);
            Tick(runtime);
            Require(port.Events.Count == 0, "Greeting text-to-audio gap admitted a recording.");
            runtime.OnAgentRoundDone("greeting");
            for (var round = 0; round < 12; round++)
            {
                input.IsRunning = false;
                input.Frames.Enqueue(loud);
                var before = port.Events.Count;
                Tick(runtime);
                Require(port.Events.Count == before + 3, "A new voice round must start, append and commit.");
                var eventId = port.LastEventId;
                input.IsRunning = true;
                // Cloud ASR/LLM has not answered yet. Noise must not supersede this committed event.
                for (var i = 0; i < 5; i++) input.Frames.Enqueue(loud);
                Tick(runtime);
                Require(port.Events.Count == before + 3,
                    "Waiting for cloud response admitted another recording and invalidated the current event.");
                runtime.OnAgentAsrText(eventId, true, "round " + round);
                runtime.OnAgentLlmDelta(eventId, "reply", "reply " + round);
                if (round % 3 == 0)
                {
                    runtime.OnAgentError(eventId, 3102, "empty asr result");
                    continue;
                }
                runtime.OnAgentTtsDelta(eventId, "reply", new byte[3200]);
                runtime.OnAgentRoundDone(eventId);
                Tick(tts);
                input.Frames.Enqueue(loud);
                Tick(runtime);
                Require(port.Events.Count == before + 3, "TTS tail admitted a new recording.");
                tts.Source.timeSamples = 1600;
                Tick(tts);
                Require(!tts.IsStreamingActive, "TTS round did not release playback.");
            }
            input.IsRunning = false;
            input.Frames.Enqueue(loud);
            Tick(runtime);
            var timedOutEvent = port.LastEventId;
            runtime.OnAgentTtsDelta(timedOutEvent, "unfinished", new byte[3200]);
            var timeouts = 0;
            runtime.ResponseTimedOut += () => timeouts++;
            typeof(VirtualRobotRuntime).GetField("_lastResponseActivity", Private).SetValue(runtime,
                Time.realtimeSinceStartupAsDouble - runtime.ResponseTimeoutSeconds - 1);
            Tick(runtime);
            Require(timeouts == 1 && !runtime.ResponsePending && !tts.IsStreamingActive,
                "Missing done/response must time out and release input.");
            var asrEvents = 0;
            runtime.AsrTextReceived += (_, __) => asrEvents++;
            runtime.OnAgentAsrText(timedOutEvent, true, "late");
            runtime.OnAgentTtsDelta(timedOutEvent, "late", new byte[3200]);
            runtime.OnAgentRoundDone(timedOutEvent);
            Require(asrEvents == 0 && !tts.IsStreamingActive,
                "Late data resurrected a timed-out response.");
            input.Frames.Enqueue(loud);
            Tick(runtime);
            Require(runtime.ResponsePending && port.LastEventId != timedOutEvent,
                "Timeout did not allow a fresh recording.");
            var interruptedEvent = port.LastEventId;
            runtime.OnAgentInterrupt("manual", "chat", "");
            runtime.OnAgentTtsDelta(interruptedEvent, "late", new byte[3200]);
            Require(!runtime.ResponsePending && !tts.IsStreamingActive,
                "Explicit interruption did not release the waiting turn or reject late data.");
            input.Frames.Enqueue(loud);
            Tick(runtime);
            var failedEvent = port.LastEventId;
            runtime.OnAgentTtsDelta(failedEvent, "partial", new byte[3200]);
            runtime.OnAgentError(failedEvent, 3301, "synthesis failed after first chunk");
            Require(!runtime.ResponsePending && !tts.IsStreamingActive,
                "An error after partial TTS must immediately release input without relying on done.");
            runtime.OnAgentTtsDelta(failedEvent, "late", new byte[3200]);
            Require(!tts.IsStreamingActive, "Failed TTS was resurrected by a late chunk.");

            input.Frames.Enqueue(loud);
            Tick(runtime);
            var replacedEvent = port.LastEventId;
            runtime.OnAgentLlmDelta("controller-reply", "override", "controller reply");
            runtime.OnAgentTtsDelta("controller-reply", "override", new byte[3200]);
            runtime.OnAgentRoundDone("controller-reply");
            Tick(tts);
            tts.Source.timeSamples = 1600;
            Tick(tts);
            runtime.OnAgentAsrText(replacedEvent, true, "stale voice reply");
            runtime.OnAgentTtsDelta(replacedEvent, "late", new byte[3200]);
            runtime.OnAgentTtsDelta("controller-reply", "late", new byte[3200]);
            Require(!runtime.ResponsePending && !tts.IsStreamingActive && asrEvents == 0,
                "Authorized replacement must complete its own turn and reject both old and completed audio.");
            var skills = 0;
            runtime.SkillRequested += (_, __) => skills++;
            runtime.OnAgentSkill("controller-reply", "skill", "emotion", "neutral", null);
            runtime.OnAgentSkill(replacedEvent, "skill", "emotion", "neutral", null);
            Require(skills == 1, "Audio completion must retain same-turn skill compatibility; cancellation must not.");
            runtime.Unbind();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void VerifySkillParameters(GameObject root)
    {
        var router = root.AddComponent<SkillRouter>();
        router.Loco = root.GetComponent<LocomotionCommander>();
        var states = new List<string>();
        foreach (var value in new object[] { "NaN", "Infinity", "invalid", true, null })
        {
            states.Clear();
            router.Execute("movement", "walk",
                new Dictionary<string, object> { ["distanceM"] = value }, states.Add);
            Require(string.Join(",", states) == "failed",
                "Invalid movement must fail without reporting running or executing a default move.");
            Require(!router.Loco.IsBusy, "Invalid movement started locomotion.");
        }
        router.Emotions = root.AddComponent<EmotionController>();
        var hold = typeof(EmotionController).GetField("_holdUntil", Private);
        foreach (var duration in new[] { 0f, -1f, 500f, 15000f })
        {
            states.Clear();
            var started = Time.time;
            router.Execute("emotion", "happy",
                new Dictionary<string, object> { ["durationMs"] = duration }, states.Add);
            Require(string.Join(",", states) == "running,done", "Valid emotion rejected.");
            var until = (float)hold.GetValue(router.Emotions);
            Require(duration <= 0 ? until == float.MaxValue :
                Mathf.Abs(until - started - duration / 1000f) < 0.05f,
                "Emotion duration changed instead of preserving the established contract.");
        }
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
                var half = new byte[NetworkConstants.MaxMessageSize / 2];
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
            Require(received == NetworkConstants.MaxMessageSize, "Message limit rejected legal prefix.");
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
            // Keep ordinary write timeout beyond the observation window; only Close may unblock it.
            pair.Server.GetStream().WriteTimeout = 5000;
            var payload = new byte[8 * 1024 * 1024];
            var started = new ManualResetEventSlim();
            var writing = Task.Run(() =>
            {
                started.Set();
                try
                {
                    // Some loopback stacks buffer a complete message despite the small SO_SNDBUF.
                    // Keep sending until backpressured, rather than assuming one write must block.
                    while (true)
                        pair.Ws.SendAsync(new ArraySegment<byte>(payload),
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
            }, 2000), "Sender did not acquire the socket lock.");
            Require(pair.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None)
                .Wait(500), "Slow peer prevented bounded close.");
            Require(writing.Wait(2000), "Close failed to unblock sender.");
            started.Dispose();
        }
    }

    static int StartServer(LinkskyGatewayServer server)
    {
        server.Start(0);
        var listener = (TcpListener)typeof(LinkskyGatewayServer).GetField("_listener", Private).GetValue(server);
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    static bool SessionThreadsStopped(GatewaySession session)
    {
        return new[] { "_sendThread", "_recvThread" }.All(name =>
        {
            var thread = (Thread)typeof(GatewaySession).GetField(name, Private).GetValue(session);
            return thread == null || !thread.IsAlive;
        });
    }

    static void VerifySessionDisposal()
    {
        for (var round = 0; round < 12; round++)
        {
            using (var server = new LinkskyGatewayServer())
            using (var opened = new ManualResetEventSlim())
            using (var start = new ManualResetEventSlim())
            {
                GatewaySession session = null;
                var closed = 0;
                server.SessionOpened += current => { session = current; opened.Set(); };
                server.SessionClosed += current =>
                {
                    Interlocked.Increment(ref closed);
                    current.Dispose(); // Close notification must allow reentrant disposal.
                };
                using (var socket = new AgentSocket(StartServer(server)))
                {
                    Require(opened.Wait(2000), "Disposal test did not establish a session.");
                    var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                    {
                        start.Wait();
                        for (var i = 0; i < 100; i++)
                        {
                            session.SendState("test", "value");
                            session.TrySendRuntimeLog("info", "Log", "dispose-race", "", 1, i, 1, 0, false);
                            if (i == 20) session.Dispose();
                        }
                    })).ToArray();
                    start.Set();
                    Require(Task.WaitAll(tasks, 5000), "Concurrent producers/disposers stalled.");
                    Require(SpinWait.SpinUntil(() => closed == 1 && SessionThreadsStopped(session), 2000),
                        "Close must fire once and both workers must exit.");
                    Require(!session.TrySendRuntimeLog("info", "Log", "after-close", "", 1, 1, 1, 0, false),
                        "Closed session accepted a diagnostic.");
                    var signal = (AutoResetEvent)typeof(GatewaySession).GetField("_sendSignal", Private).GetValue(session);
                    var disposed = false;
                    try { signal.Set(); }
                    catch (ObjectDisposedException) { disposed = true; }
                    Require(disposed, "Sender exit leaked its signal handle.");
                }
            }
        }
    }

    static void VerifyCallbackDisposal()
    {
        foreach (var incoming in new[] { false, true })
        {
            using (var server = new LinkskyGatewayServer())
            using (var opened = new ManualResetEventSlim())
            using (var finished = new ManualResetEventSlim())
            {
                GatewaySession session = null;
                var triggered = 0;
                var elapsedMs = 0L;
                server.SessionOpened += current =>
                {
                    session = current;
                    current.FrameLogged += line =>
                    {
                        var prefix = incoming ? "gw  <- agent" : "gw  -> agent";
                        if (!line.StartsWith(prefix, StringComparison.Ordinal) ||
                            Interlocked.Exchange(ref triggered, 1) != 0) return;
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        current.Dispose(); // Called from receive/send worker, never Join itself.
                        elapsedMs = watch.ElapsedMilliseconds;
                        finished.Set();
                    };
                    opened.Set();
                };
                using (var socket = new AgentSocket(StartServer(server)))
                {
                    Require(opened.Wait(2000), "Callback test did not establish a session.");
                    if (incoming) socket.Send(new JObject { ["type"] = "test.dispose" });
                    else session.SendState("dispose", "now");
                    Require(finished.Wait(2000), "Worker callback disposal stalled.");
                    Require(elapsedMs < 900, "Worker callback waited for itself or another worker.");
                    Require(SpinWait.SpinUntil(() => SessionThreadsStopped(session), 2000),
                        "Callback disposal leaked worker threads.");
                }
            }
        }
        using (var server = new LinkskyGatewayServer())
        using (var finished = new ManualResetEventSlim())
        {
            GatewaySession session = null;
            server.SessionOpened += current =>
            {
                session = current;
                current.Dispose();
                server.Dispose(); // Accept-thread callback must not Join the accept thread.
                finished.Set();
            };
            using (var socket = new AgentSocket(StartServer(server)))
            {
                Require(finished.Wait(2000), "SessionOpened disposal stalled.");
                var accept = (Thread)typeof(LinkskyGatewayServer).GetField("_acceptThread", Private).GetValue(server);
                Require(accept.Join(2000), "Disposed accept thread remained alive.");
                Require(SessionThreadsStopped(session), "Disposed session restarted its workers.");
                Require(typeof(GatewaySession).GetField("_sendThread", Private).GetValue(session) == null,
                    "SessionOpened disposal must prevent workers from starting.");
                Require(server.Sessions.Count == 0, "Disposed server retained a session.");
            }
        }
    }

    static void VerifyStopDuringHandshake()
    {
        using (var server = new LinkskyGatewayServer())
        using (var client = new TcpClient())
        {
            var opened = 0;
            server.SessionOpened += _ => Interlocked.Increment(ref opened);
            client.Connect(IPAddress.Loopback, StartServer(server));
            var partial = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n");
            client.GetStream().Write(partial, 0, partial.Length);
            var pending = typeof(LinkskyGatewayServer).GetField("_handshakeClient", Private);
            Require(SpinWait.SpinUntil(() => pending.GetValue(server) != null, 2000),
                "Accept thread did not begin the partial handshake.");
            var accept = (Thread)typeof(LinkskyGatewayServer).GetField("_acceptThread", Private).GetValue(server);
            server.Stop();
            Require(accept.Join(2000), "Stop did not cancel the blocked handshake.");
            Require(opened == 0 && server.Sessions.Count == 0, "Stop registered a late session.");
        }
    }

    static void VerifyFaultyLogObserver()
    {
        using (var server = new LinkskyGatewayServer { MaxSessions = 1 })
        {
            var observed = 0;
            GatewaySession current = null;
            server.Log += line =>
            {
                if (line.StartsWith("gw  session opened,", StringComparison.Ordinal))
                    throw new InvalidOperationException("intentional log observer failure");
            };
            server.Log += line =>
            {
                if (line.StartsWith("gw  session opened,", StringComparison.Ordinal))
                    Interlocked.Increment(ref observed);
            };
            server.SessionOpened += session =>
            {
                current = session;
                session.SendRobotOnline(null, session.CallbackType);
            };
            var port = StartServer(server);
            for (var i = 0; i < 3; i++)
            {
                using (var socket = new AgentSocket(port))
                    Require((string)socket.Read()["state"] == "online",
                        "A faulty log observer prevented session initialization.");
                Require(SpinWait.SpinUntil(() =>
                    server.Sessions.Count == 0 && current != null && SessionThreadsStopped(current), 2000),
                    "A faulty log observer leaked a session slot or worker.");
            }
            Require(observed == 3, "A faulty log observer prevented later observers from running.");
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
        public string LastEventId;
        public void SendAudioStart(string e, string i) { LastEventId = e; Events.Add("start"); }
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
