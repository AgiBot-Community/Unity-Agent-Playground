using System.Collections.Generic;
using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// 麦克风采集 → 16k/mono/100ms 帧。Microphone API 只能在主线程调用。
    /// 读取采用"整秒缓冲 GetData + 游标追赶"，实现简单且足够（100ms 帧延迟可接受）。
    /// </summary>
    public class MicInputSource : MonoBehaviour, IAudioInputSource
    {
        public const int SampleRate = 16000;
        const int BufferSeconds = 1;
        const int FrameSize = SampleRate / 10; // 100ms

        public string DeviceName; // 空 = 默认设备

        AudioClip _clip;
        int _lastPos;
        readonly float[] _secBuffer = new float[SampleRate * BufferSeconds];
        readonly Queue<float[]> _frames = new Queue<float[]>();
        readonly List<float> _pending = new List<float>(FrameSize * 2);
        bool _running;

        public bool IsRunning => _running;

        public void Begin()
        {
            if (_running) return;
            var dev = string.IsNullOrEmpty(DeviceName) ? null : DeviceName;
            _clip = Microphone.Start(dev, true, BufferSeconds, SampleRate);
            _lastPos = 0;
            _running = _clip != null;
            if (!_running) Debug.LogWarning("[MicInput] Microphone.Start failed");
        }

        public void End()
        {
            if (!_running) return;
            _running = false;
            var dev = string.IsNullOrEmpty(DeviceName) ? null : DeviceName;
            Microphone.End(dev);
            _frames.Clear();
            _pending.Clear();
        }

        void Update()
        {
            if (!_running || _clip == null) return;
            var dev = string.IsNullOrEmpty(DeviceName) ? null : DeviceName;
            var pos = Microphone.GetPosition(dev);
            if (pos == _lastPos) return;
            if (pos < 0) return;

            _clip.GetData(_secBuffer, 0);

            // 从 _lastPos 追到 pos（处理 1s 缓冲回绕）
            var n = SampleRate * BufferSeconds;
            var count = pos > _lastPos ? pos - _lastPos : n - _lastPos + pos;
            if (count > n) count = n; // 落后超过整圈，只取最近一圈
            for (int i = 0; i < count; i++)
            {
                _pending.Add(_secBuffer[(_lastPos + i) % n]);
            }
            _lastPos = pos;

            while (_pending.Count >= FrameSize)
            {
                var frame = new float[FrameSize];
                for (int i = 0; i < FrameSize; i++) frame[i] = _pending[i];
                _pending.RemoveRange(0, FrameSize);
                _frames.Enqueue(frame);
            }
        }

        public bool TryReadFrame(out float[] frame)
        {
            if (_frames.Count > 0)
            {
                frame = _frames.Dequeue();
                return true;
            }
            frame = null;
            return false;
        }
    }
}
