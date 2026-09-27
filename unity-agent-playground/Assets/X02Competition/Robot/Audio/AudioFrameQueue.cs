using System;
using System.Collections.Generic;

namespace X02Competition.Robot
{
    /// <summary>Main-thread audio backlog: keep recent frames when a consumer falls behind.</summary>
    public sealed class AudioFrameQueue
    {
        readonly Queue<float[]> _frames = new Queue<float[]>();
        readonly int _capacity;

        public AudioFrameQueue(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public void Enqueue(float[] frame)
        {
            if (_frames.Count >= _capacity) _frames.Dequeue();
            _frames.Enqueue(frame);
        }

        public bool TryRead(out float[] frame)
        {
            if (_frames.Count == 0) { frame = null; return false; }
            frame = _frames.Dequeue();
            return true;
        }

        public void Clear() => _frames.Clear();
    }
}
