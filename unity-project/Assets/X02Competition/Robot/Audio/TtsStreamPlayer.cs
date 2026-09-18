using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// TTS 流式播放器：接收 on_tts_item_delta 的 PCM 分块，边收边播（dynamic AudioClip + SetData 增量写）。
    /// 打断语义（协议文档 §5）：
    ///   - Append 追加数据；Stop（收到 interrupt / 新一轮 audio_request.start / 手动）立即停并清空；
    ///   - IsStreamingActive = 本轮还有未播完的音频（半双工门控冻结麦克风上行）。
    /// 单轮上限 ClipSeconds，超出截断告警（正常应答远小于此）。
    /// </summary>
    public class TtsStreamPlayer : MonoBehaviour
    {
        public const int SampleRate = 16000;
        const float ClipSeconds = 60f;      // 单轮 TTS 上限
        const float PreBufferSec = 0.15f;   // 起播缓冲
        const float CatchUpSec = 0.05f;     // 播放头距写头小于此则暂停等待

        public AudioSource Source;

        /// <summary>播放能量（0~1，每帧播时采样），供口型/律动等消费。</summary>
        public event System.Action<float> PlaybackEnergy;

        AudioClip _clip;
        int _write;           // 绝对写指针（采样数，不回绕，上限 clip 长度）
        bool _roundActive;    // 本轮 TTS 是否活动
        bool _roundClosed;    // tts_response.done 已到
        readonly float[] _energyBuf = new float[800]; // 50ms 播放头窗口

        int ClipSamples => (int)(ClipSeconds * SampleRate);

        /// <summary>本轮 TTS 是否仍在活动（已写入未播完，或仍在接收）。</summary>
        public bool IsStreamingActive => _roundActive && PendingSamples > (int)(CatchUpSec * SampleRate);

        int PendingSamples
        {
            get
            {
                if (_clip == null || Source == null) return 0;
                return _write - Source.timeSamples;
            }
        }

        void Awake()
        {
            if (Source == null) Source = gameObject.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.spatialBlend = 0f;
        }

        /// <summary>追加 PCM（16k/16bit/mono）。在新一轮首个 delta 时自动复位。</summary>
        public void Append(string eventId, string itemId, byte[] pcm)
        {
            if (pcm == null || pcm.Length < 2)
            {
                Debug.LogWarning("[TtsPlayer] Append 丢弃: pcm=" + (pcm == null ? "null" : pcm.Length + "B"));
                return;
            }
            EnsureClip();

            var sampleCount = pcm.Length / 2;
            var space = ClipSamples - _write;
            if (space <= 0)
            {
                Debug.LogWarning("[TtsPlayer] 单轮 TTS 超过 " + ClipSeconds + "s，截断");
                return;
            }
            var take = Mathf.Min(sampleCount, space);

            var floats = new float[take];
            for (int i = 0; i < take; i++)
            {
                short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                floats[i] = s / 32768f;
            }
            _clip.SetData(floats, _write);
            _write += take;
            _roundActive = true;
            Debug.Log("[TtsPlayer] Append +" + take + " samples (write=" + _write + ", item=" + itemId + ")");
        }

        /// <summary>本轮 TTS 结束标记（tts_response.done）。播放头追上后自动收尾。</summary>
        public void MarkRoundDone() => _roundClosed = true;

        /// <summary>立即停止并清空（interrupt / 新 audio_request.start / 手动）。</summary>
        public void Stop()
        {
            if (_roundActive) // 正在播/待播被掐 —— 打调用点定位是谁
                Debug.Log("[TtsPlayer] Stop 掐断活动轮\n" + System.Environment.StackTrace);
            if (Source != null)
            {
                Source.Stop();
                Source.clip = null;
            }
            if (_clip != null) Destroy(_clip);
            _clip = null;
            _write = 0;
            _roundActive = false;
            _roundClosed = false;
        }

        void EnsureClip()
        {
            if (_clip != null) return;
            _clip = AudioClip.Create("tts-stream", ClipSamples, 1, SampleRate, false);
            _write = 0;
            _roundClosed = false;
            Source.clip = _clip;
            Source.timeSamples = 0;
            Source.Stop();
        }

        void Update()
        {
            if (_clip == null || Source == null || !_roundActive) return;

            if (Source.isPlaying)
            {
                EmitPlaybackEnergy();
                if (PendingSamples < (int)(CatchUpSec * SampleRate))
                {
                    Source.Pause();
                    if (_roundClosed) FinishRound();
                }
                return;
            }

            if (PendingSamples >= (int)(PreBufferSec * SampleRate))
            {
                Source.timeSamples = Mathf.Min(Source.timeSamples, ClipSamples - 1);
                Source.Play();
                Debug.Log("[TtsPlayer] Play 开始播放, pending=" + PendingSamples +
                          " samples (" + (PendingSamples / (float)SampleRate).ToString("F2") + "s)");
            }
            else if (_roundClosed && PendingSamples <= 0)
            {
                FinishRound();
            }
        }

        void FinishRound()
        {
            // 本轮播完：复位，下一轮 Append 重建 clip
            Stop();
            PlaybackEnergy?.Invoke(0f);
        }

        /// <summary>取播放头后 50ms 窗口 RMS 归一化（口型驱动）。</summary>
        void EmitPlaybackEnergy()
        {
            if (PlaybackEnergy == null) return;
            var pos = Source.timeSamples;
            if (pos + _energyBuf.Length > _write) return; // 越界（未写入）
            _clip.GetData(_energyBuf, pos);
            float sum = 0f;
            for (int i = 0; i < _energyBuf.Length; i++)
            {
                var v = _energyBuf[i];
                sum += v * v;
            }
            var rms = Mathf.Sqrt(sum / _energyBuf.Length);
            PlaybackEnergy?.Invoke(Mathf.Clamp01(rms * 5f));
        }
    }
}
