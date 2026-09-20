namespace X02Competition.Robot
{
    /// <summary>
    /// 音频输入源统一接口：产出 16k/mono/float 帧（每帧 100ms = 1600 采样）。
    /// 实现方：麦克风（MicInputSource）/ 语料回放（ClipInputSource，测试与演示用）。
    /// Begin/End 命名避开 MonoBehaviour.Start/Stop。
    /// </summary>
    public interface IAudioInputSource
    {
        /// <summary>开始采集。</summary>
        void Begin();

        /// <summary>停止采集。</summary>
        void End();

        bool IsRunning { get; }

        /// <summary>非阻塞读取已就绪的帧（一帧 Update 可能产出多帧）。返回 false 表示暂无数据。</summary>
        bool TryReadFrame(out float[] frame);
    }
}
