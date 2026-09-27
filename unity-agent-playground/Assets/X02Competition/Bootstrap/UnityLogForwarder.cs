using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using X02Competition.Gateway;

namespace X02Competition.Bootstrap
{
    /// <summary>
    /// Captures Unity runtime logs on any thread, then queues them to the existing agent session.
    /// No Unity API, logging, or network I/O is allowed inside the capture callback.
    /// </summary>
    public sealed class UnityLogForwarder : MonoBehaviour
    {
        const int Capacity = 256;
        const int MessageLimit = 8192;
        const int StackLimit = 16384;
        const int PerFrameLimit = 32;
        static readonly object Gate = new object();
        static readonly Queue<Entry> Pending = new Queue<Entry>();
        static long _sequence;
        static long _dropped;

        public LinkskyGatewayServer Server { get; set; }
        public static int PendingCount { get { lock (Gate) return Pending.Count; } }

        sealed class Entry
        {
            public string Level, LogType, Message, Stack;
            public long Timestamp, Sequence;
            public int ThreadId;
            public bool Truncated;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitializeCapture()
        {
            // Also reset when Enter Play Mode has domain reload disabled.
            Application.logMessageReceivedThreaded -= Capture;
            lock (Gate)
            {
                Pending.Clear();
                _sequence = 0;
                _dropped = 0;
            }
            Application.logMessageReceivedThreaded += Capture;
        }

        static string Limit(string text, int maximum, ref bool truncated)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maximum) return text;
            truncated = true;
            if (char.IsHighSurrogate(text[maximum - 1])) maximum--;
            return text.Substring(0, maximum);
        }

        static void Capture(string message, string stackTrace, LogType type)
        {
            var truncated = false;
            var entry = new Entry
            {
                Level = type == LogType.Log ? "info" : type == LogType.Warning ? "warning" : "error",
                LogType = type.ToString(),
                Message = Limit(message, MessageLimit, ref truncated),
                Stack = Limit(stackTrace, StackLimit, ref truncated),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = Thread.CurrentThread.ManagedThreadId,
                Truncated = truncated,
            };
            lock (Gate)
            {
                entry.Sequence = ++_sequence;
                if (Pending.Count >= Capacity)
                {
                    Pending.Dequeue();
                    _dropped++;
                }
                Pending.Enqueue(entry);
            }
        }

        void LateUpdate() => FlushPending();

        /// <summary>Bounded main-thread drain; retains recent logs while disconnected/backpressured.</summary>
        public int FlushPending()
        {
            if (Server == null) return 0;
            var sessions = Server.Sessions;
            if (sessions.Count == 0) return 0;
            var sent = 0;
            lock (Gate)
            {
                while (sent < PerFrameLimit && Pending.Count > 0)
                {
                    var entry = Pending.Peek();
                    // A slow subscriber may drop diagnostics but cannot block the other subscribers.
                    foreach (var session in sessions)
                        if (session.IsOpen)
                            session.TrySendRuntimeLog(entry.Level, entry.LogType, entry.Message,
                                entry.Stack, entry.Timestamp, entry.Sequence, entry.ThreadId, _dropped,
                                entry.Truncated);
                    Pending.Dequeue();
                    sent++;
                }
            }
            return sent;
        }

        void OnApplicationQuit()
        {
            Application.logMessageReceivedThreaded -= Capture;
        }
    }
}
