namespace X02Competition.Robot
{
    /// <summary>
    /// 能量 + 迟滞 VAD 状态机（纯 C#，可 EditMode 单测）。
    /// 输入：16k/mono float 帧（建议 100ms）；输出：start/append/commit 对应的事件。
    /// 参数需与真机抓包结果校准（见设计文档风险登记 #3）。
    /// </summary>
    public sealed class VadGate
    {
        /// <summary>进入说话的能量阈值（RMS）。</summary>
        public float StartRms = 0.02f;
        /// <summary>保持说话的能量阈值（低于它开始累计静音时长）。</summary>
        public float StopRms = 0.008f;
        /// <summary>静音多久判定说完（毫秒）→ commit。</summary>
        public double SilenceMs = 600;
        /// <summary>最长说话时长（毫秒），强制 commit。</summary>
        public double MaxSpeechMs = 15000;

        /// <summary>半双工门控：false 时冻结（TTS 播放中）；若正处于说话中则强制结束。</summary>
        public bool Enabled = true;

        public bool InSpeech { get; private set; }

        double _speechStartTs;
        double _lastLoudTs;

        public enum Event { None, SpeechStarted, SpeechEnded }

        /// <summary>
        /// 喂入一帧。nowSec 为单调递增时钟（Time.realtimeSinceStartup 或测试注入）。
        /// SpeechStarted：本帧为第一帧（调用方应先发 start 再把本帧作为首个 append）。
        /// SpeechEnded：本帧之后应发 commit。
        /// </summary>
        public Event Feed(float[] frame, double nowSec)
        {
            var rms = ComputeRms(frame);
            return FeedRms(rms, nowSec);
        }

        public Event FeedRms(float rms, double nowSec)
        {
            if (!Enabled)
            {
                if (InSpeech)
                {
                    InSpeech = false;
                    return Event.SpeechEnded;
                }
                return Event.None;
            }

            if (!InSpeech)
            {
                if (rms >= StartRms)
                {
                    InSpeech = true;
                    _speechStartTs = nowSec;
                    _lastLoudTs = nowSec;
                    return Event.SpeechStarted;
                }
                return Event.None;
            }

            // 说话中
            if (rms >= StopRms) _lastLoudTs = nowSec;

            var speechMs = (nowSec - _speechStartTs) * 1000.0;
            var silenceMs = (nowSec - _lastLoudTs) * 1000.0;

            if (speechMs >= MaxSpeechMs || silenceMs >= SilenceMs)
            {
                InSpeech = false;
                // 注意：即使说话极短也必须发 commit（start 已上行，协议要求闭合事件）
                return Event.SpeechEnded;
            }
            return Event.None;
        }

        /// <summary>强制复位（会话切换等）。</summary>
        public void Reset() => InSpeech = false;

        public static float ComputeRms(float[] frame)
        {
            if (frame == null || frame.Length == 0) return 0f;
            double sum = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                var v = frame[i];
                sum += v * v;
            }
            return (float)System.Math.Sqrt(sum / frame.Length);
        }
    }
}
