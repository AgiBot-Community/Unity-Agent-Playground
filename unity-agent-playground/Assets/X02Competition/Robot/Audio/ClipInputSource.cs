using System.Collections.Generic;
using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// 语料回放输入源：把 AudioClip 按 16k/mono/100ms 实时帧流出（与麦克风同接口）。
    /// 用途：无麦克风环境联调、T4 打断用例、录视频时注入"用户语音"。
    /// 要求 clip 为 16k 单声道（否则告警并尽量降混）。
    /// </summary>
    public class ClipInputSource : MonoBehaviour, IAudioInputSource
    {
        public const int SampleRate = 16000;
        const int FrameSize = SampleRate / 10;

        public AudioClip Clip;
        public bool Loop;

        float[] _samples;   // 转换后的 16k mono
        int _cursor;        // 已发射采样数
        double _clockSec;   // 实时 pacing 时钟
        bool _running;

        readonly Queue<float[]> _frames = new Queue<float[]>();

        public bool IsRunning => _running;

        public void Begin()
        {
            if (_running || Clip == null) return;
            LoadClip();
            _cursor = 0;
            _clockSec = 0;
            _running = _samples != null;
        }

        public void End()
        {
            _running = false;
            _frames.Clear();
        }

        /// <summary>从头重新播放（DebugHud"重放语料"按钮用）。</summary>
        public void Replay()
        {
            if (Clip == null) return;
            LoadClip();
            _cursor = 0;
            _clockSec = 0;
            _running = _samples != null;
        }

        void LoadClip()
        {
            var data = new float[Clip.samples * Clip.channels];
            Clip.GetData(data, 0);
            // 降混到单声道
            if (Clip.channels > 1)
            {
                var mono = new float[Clip.samples];
                for (int i = 0; i < Clip.samples; i++)
                {
                    var s = 0f;
                    for (int c = 0; c < Clip.channels; c++) s += data[i * Clip.channels + c];
                    mono[i] = s / Clip.channels;
                }
                data = mono;
            }
            if (Clip.frequency != SampleRate)
            {
                Debug.LogWarning("[ClipInput] clip 采样率 " + Clip.frequency + " != 16000，" +
                    "简单重采样（线性插值）处理");
                var ratio = (float)Clip.frequency / SampleRate;
                var resampled = new float[(int)(data.Length / ratio)];
                for (int i = 0; i < resampled.Length; i++)
                {
                    var src = i * ratio;
                    var i0 = (int)src;
                    var i1 = System.Math.Min(i0 + 1, data.Length - 1);
                    var t = src - i0;
                    resampled[i] = data[i0] * (1 - t) + data[i1] * t;
                }
                data = resampled;
            }
            _samples = data;
        }

        void Update()
        {
            if (!_running || _samples == null) return;
            _clockSec += Time.deltaTime;

            var target = (int)(_clockSec * SampleRate);
            target = System.Math.Min(target, _samples.Length);
            while (_cursor + FrameSize <= target)
            {
                var frame = new float[FrameSize];
                System.Array.Copy(_samples, _cursor, frame, 0, FrameSize);
                _cursor += FrameSize;
                _frames.Enqueue(frame);
            }

            if (_cursor >= _samples.Length)
            {
                if (Loop)
                {
                    _cursor = 0;
                    _clockSec = 0;
                }
                else
                {
                    _running = false; // 播完自动停
                }
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
