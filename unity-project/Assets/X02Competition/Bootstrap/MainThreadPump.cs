using System;
using System.Collections.Concurrent;
using UnityEngine;
using X02Competition.Gateway;

namespace X02Competition.Bootstrap
{
    /// <summary>
    /// 主线程泵：把网关层投递的动作在 Unity 主线程执行，并驱动所有会话的入站队列。
    /// 挂在场景中由 CompetitionLauncher 自动装配。
    /// </summary>
    public class MainThreadPump : MonoBehaviour
    {
        readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public LinkskyGatewayServer Server { get; set; }

        /// <summary>任意线程调用：投递到主线程执行。</summary>
        public void Post(Action action) => _queue.Enqueue(action);

        void Update()
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            if (Server == null) return;
            var sessions = Server.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                sessions[i].ExecutePendingActions();
            }
        }
    }
}
