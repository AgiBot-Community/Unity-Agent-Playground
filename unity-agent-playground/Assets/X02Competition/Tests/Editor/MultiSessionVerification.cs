#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using X02Competition.Bootstrap;
using X02Competition.Gateway;
using X02Competition.Protocol;
using Socket = AuditRegressionVerification.AgentSocket;

public static class MultiSessionVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int _checks;
    static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception("Multi-session: " + message);
    }
    static JObject ReadUntil(Socket socket, Func<JObject, bool> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            var frame = socket.Read(true);
            if (predicate(frame)) return frame;
        }
        throw new Exception("Multi-session: expected message missing");
    }
    static JObject Topology(Socket socket, int count) =>
        ReadUntil(socket, f => (string)f["type"] == LinkskyTypes.SessionState && ((JArray)f["sessions"]).Count == count);
    static void Pump(CompetitionLauncher launcher) =>
        typeof(MainThreadPump).GetMethod("Update", Private).Invoke(launcher.Pump, null);
    static void Until(CompetitionLauncher launcher, Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            Pump(launcher);
            if (predicate()) return;
            Thread.Sleep(2);
        }
        throw new Exception("Multi-session: timed out waiting for main-thread dispatch");
    }
    static JObject Skill(string eventId) => new JObject
    {
        ["type"] = LinkskyTypes.XlmResponseSkill, ["eventId"] = eventId,
        ["skillType"] = "gesture", ["skillName"] = "wave_hands",
    };
    static JObject Priority(string cid, JToken value) => new JObject
    {
        ["type"] = LinkskyTypes.SessionPrioritySet, ["eventId"] = Guid.NewGuid().ToString(),
        ["targetRobotCid"] = cid, ["priority"] = value,
    };
    public static void Verify()
    {
        var root = new GameObject("Multi-session regression");
        root.SetActive(false);
        var launcher = root.AddComponent<CompetitionLauncher>();
        launcher.Port = 0;
        launcher.AutoBeginInput = false;
        launcher.ShowDebugHud = false;
        launcher.Mode = CompetitionLauncher.InputMode.TestClip;
        root.SetActive(true);
        var recorder = new RecordingRuntime();
        var server = launcher.Server;
        server.SessionOpened += session => session.Runtime = recorder;
        server.Start(0);
        var listener = (TcpListener)typeof(LinkskyGatewayServer).GetField("_listener", Private).GetValue(server);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sockets = new List<Socket>();
        try
        {
            var a = new Socket(port, name: "agent-A"); sockets.Add(a);
            var aSync = a.Read(); var aCid = (string)aSync["robotCid"];
            var b = new Socket(port, name: "agent-B"); sockets.Add(b);
            var bSync = b.Read(); var bCid = (string)bSync["robotCid"];
            Check((bool)aSync["audioActive"] && !(bool)bSync["audioActive"], "equal priority must retain earlier voice owner");
            var controller = new Socket(port, "controller", false, "X2 Console"); sockets.Add(controller);
            var cSync = controller.Read(); var cCid = (string)cSync["robotCid"];
            var observer = new Socket(port, "observer", false, "viewer"); sockets.Add(observer);
            observer.Read();
            foreach (var socket in sockets) Topology(socket, 4);
            Pump(launcher);
            Check(server.Sessions.Count == 4, "four connections must coexist");
            Check(server.ControlOwner.RobotCid == cCid && server.ControlOwner.Priority == 1000,
                "controller must have fixed highest priority");
            Check(server.AudioOwner.RobotCid == aCid, "manual controller must not steal voice input");
            Check(ReferenceEquals(launcher.Runtime.Port, server), "runtime must bind shared gateway, not last socket");

            a.Send(Skill("blocked-A"));
            controller.Send(Skill("accepted-controller"));
            Until(launcher, () => recorder.Skills.Contains("accepted-controller"));
            Check(!recorder.Skills.Contains("blocked-A"), "lower priority action executed");
            Check((int)ReadUntil(a, f => (string)f["type"] == LinkskyTypes.Error)["errorCode"] == 4091,
                "lower priority must receive explicit rejection");

            controller.Send(new JObject { ["type"] = LinkskyTypes.SessionControlSet, ["enabled"] = false });
            Until(launcher, () => server.ControlOwner.RobotCid == aCid);
            foreach (var socket in sockets) Topology(socket, 4);
            Check(server.ManagementOwner.RobotCid == cCid && server.ManagementOwner.Priority == 1000,
                "releasing actions must preserve controller management and fixed priority");
            a.Send(Skill("voice-wave"));
            var openArms = Skill("voice-open");
            openArms["skillName"] = "open_arms";
            a.Send(openArms);
            Until(launcher, () => recorder.Skills.Contains("voice-wave") && recorder.Skills.Contains("voice-open"));
            Check(recorder.Names.Contains("wave_hands") && recorder.Names.Contains("open_arms"),
                "both spoken gesture commands must pass after releasing takeover");
            controller.Send(Priority(aCid, 60));
            Until(launcher, () => server.ControlOwner.Priority == 60);
            foreach (var socket in sockets) Topology(socket, 4);
            Check(server.ManagementOwner.RobotCid == cCid, "released controller lost priority management");
            a.Send(new JObject { ["type"] = LinkskyTypes.SessionControlSet, ["enabled"] = false });
            Until(launcher, () => a.HasData);
            Check((int)ReadUntil(a, f => (string)f["type"] == LinkskyTypes.Error)["errorCode"] == 4093,
                "agent changed the controller's takeover setting");
            controller.Send(new JObject { ["type"] = LinkskyTypes.SessionControlSet, ["enabled"] = "false" });
            Until(launcher, () => controller.HasData);
            Check((int)ReadUntil(controller, f => (string)f["type"] == LinkskyTypes.Error)["errorCode"] == 4093,
                "non-boolean takeover setting accepted");
            controller.Send(new JObject { ["type"] = LinkskyTypes.SessionControlSet, ["enabled"] = true });
            Until(launcher, () => server.ControlOwner.RobotCid == cCid);
            foreach (var socket in sockets) Topology(socket, 4);
            a.Send(Skill("blocked-after-reclaim"));
            Until(launcher, () => a.HasData);
            ReadUntil(a, f => (string)f["type"] == LinkskyTypes.Error);
            Check(!recorder.Skills.Contains("blocked-after-reclaim"), "reclaim did not stop lower-priority skills");

            controller.Send(Priority(bCid, 800));
            Until(launcher, () => server.AudioOwner.RobotCid == bCid);
            Pump(launcher);
            foreach (var socket in sockets) Topology(socket, 4);
            Check(server.ControlOwner.RobotCid == cCid, "agent priority must never outrank controller");
            Check(ReferenceEquals(launcher.Runtime.Port, server), "priority update unbound runtime");
            server.SendAudioStart("audio-B", "item");
            server.SendAudioAppend("audio-B", "item", new byte[3200]);
            server.SendAudioCommit("audio-B", "item");
            Check((string)b.Read()["type"] == LinkskyTypes.AudioRequestStart, "selected voice agent missed start");
            Check((string)b.Read()["type"] == LinkskyTypes.AudioRequestAppend, "selected voice agent missed audio");
            Check((string)b.Read()["type"] == LinkskyTypes.AudioRequestCommit, "selected voice agent missed commit");
            Check(!a.HasData && !controller.HasData && !observer.HasData, "audio was duplicated to other clients");

            b.Send(new JObject { ["type"] = LinkskyTypes.TtsResponseDone, ["eventId"] = "audio-B" });
            Until(launcher, () => recorder.Rounds.Contains("audio-B"));
            controller.Send(new JObject { ["type"] = LinkskyTypes.XlmResponseInterrupt, ["eventId"] = "stop" });
            Until(launcher, () => recorder.Interrupts == 1);
            b.Send(new JObject { ["type"] = LinkskyTypes.TtsResponseItemDelta, ["eventId"] = "audio-B", ["audio"] = "AAA=" });
            Until(launcher, () => b.HasData);
            Check((int)ReadUntil(b, f => (string)f["type"] == LinkskyTypes.Error)["errorCode"] == 4091,
                "late voice reply resumed after controller stop");
            Check(recorder.Audio == 0, "blocked PCM reached runtime");

            observer.Send(Skill("observer-command"));
            Until(launcher, () => observer.HasData);
            ReadUntil(observer, f => (string)f["type"] == LinkskyTypes.Error);
            Check(!recorder.Skills.Contains("observer-command"), "observer executed an action");
            b.Send(Priority(aCid, 900));
            Until(launcher, () => b.HasData);
            Check((int)ReadUntil(b, f => (string)f["type"] == LinkskyTypes.Error)["errorCode"] == 4093,
                "non-controller changed priorities");
            foreach (var value in new JToken[] { -1, 1000, 10.5, "20" })
            {
                controller.Send(Priority(aCid, value));
                Until(launcher, () => controller.HasData);
                Check((int)ReadUntil(controller, f => (string)f["type"] == LinkskyTypes.Error)["errorCode"] == 4093,
                    "invalid priority was accepted");
            }
            controller.Send(Priority(cCid, 0));
            Until(launcher, () => controller.HasData);
            ReadUntil(controller, f => (string)f["type"] == LinkskyTypes.Error);
            Check(server.ControlOwner.Priority == 1000, "controller priority was modified");

            Debug.LogWarning("multi-session-broadcast-log");
            var forwarder = root.GetComponent<UnityLogForwarder>();
            while (UnityLogForwarder.PendingCount > 0) forwarder.FlushPending();
            foreach (var socket in sockets)
                Check((string)ReadUntil(socket, f => (string)f["message"] == "multi-session-broadcast-log")["level"] == "warning",
                    "runtime log missing from subscriber");
            // Finish draining diagnostic backlog before the next ownership snapshot.
            controller.Dispose(); sockets.Remove(controller);
            Until(launcher, () => server.Sessions.Count == 3 && server.ControlOwner.RobotCid == bCid);
            foreach (var socket in sockets) Topology(socket, 3);
            Check(ReferenceEquals(launcher.Runtime.Port, server), "disconnecting one client disconnected all clients");
            b.Send(Skill("accepted-B"));
            Until(launcher, () => recorder.Skills.Contains("accepted-B"));
            Check(recorder.Skills.Count == 4, "unexpected lower-priority action reached runtime");

            var monitor = new Socket(port, "controller", false, "Management console", control: false);
            sockets.Add(monitor);
            var monitorSync = monitor.Read();
            foreach (var socket in sockets) Topology(socket, 4);
            Pump(launcher);
            Check(!(bool)monitorSync["controlEnabled"] && !(bool)monitorSync["controlActive"] &&
                !(bool)monitorSync["audioActive"] && (bool)monitorSync["canManage"],
                "monitor handshake must opt out of actions/audio while retaining management");
            Check((int)monitorSync["priority"] == 1000, "monitor console priority changed");
            Check(server.ControlOwner.RobotCid == bCid && server.AudioOwner.RobotCid == bCid,
                "opening monitoring console preempted the active Agent");
            monitor.Send(Priority(aCid, 80));
            Until(launcher, () => server.Sessions.Any(s => s.RobotCid == aCid && s.Priority == 80));
            foreach (var socket in sockets) Topology(socket, 4);
            Check(server.ControlOwner.RobotCid == bCid, "priority management stole action control");
            monitor.Dispose(); sockets.Remove(monitor);
            Until(launcher, () => server.Sessions.Count == 3);
            foreach (var socket in sockets) Topology(socket, 3);
            Check(server.ControlOwner.RobotCid == bCid, "closing monitoring console changed action owner");

            b.Dispose(); sockets.Remove(b);
            Until(launcher, () => server.Sessions.Count == 2 && server.AudioOwner.RobotCid == aCid);
            Check(server.ControlOwner.RobotCid == aCid, "owner did not fall back by priority");
            a.Dispose(); sockets.Remove(a);
            Until(launcher, () => server.Sessions.Count == 1);
            Check(server.AudioOwner == null && server.ControlOwner == null, "observer acquired ownership");
            observer.Dispose(); sockets.Remove(observer);
            Until(launcher, () => server.Sessions.Count == 0 && launcher.Runtime.Port == null);
            Debug.Log("MULTI_SESSION_VERIFICATION_PASSED: " + _checks +
                " checks; concurrent clients, mutable priorities, immutable controller, voice routing, broadcasts, rejection and handoff.");
        }
        finally
        {
            foreach (var socket in sockets) socket.Dispose();
            server.Stop();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    sealed class RecordingRuntime : IRobotRuntime
    {
        public readonly List<string> Skills = new List<string>();
        public readonly List<string> Names = new List<string>();
        public readonly List<string> Rounds = new List<string>();
        public int Audio, Interrupts;
        public void OnAgentConnected(string a, string c) { }
        public void OnAgentDisconnected() { }
        public void OnAgentAsrText(string e, bool f, string t) { }
        public void OnAgentLlmDelta(string e, string i, string t) { }
        public void OnAgentTtsDelta(string e, string i, byte[] pcm) => Audio++;
        public void OnAgentTtsDone(string e, string i) { }
        public void OnAgentRoundDone(string e) => Rounds.Add(e);
        public void OnAgentSkill(string e, string i, string t, string n, Dictionary<string, object> p)
        { Skills.Add(e); Names.Add(n); }
        public void OnAgentInterrupt(string e, string t, string tips) => Interrupts++;
        public void OnAgentError(string e, int c, string m) { }
    }
}
#endif
