namespace X02Competition.Protocol
{
    /// <summary>
    /// 网络和协议相关常量配置。
    /// 集中管理超时、缓冲区大小、限制等硬编码值。
    /// </summary>
    public static class NetworkConstants
    {
        // ---- WebSocket 配置 ----

        /// <summary>WebSocket 接收缓冲区大小（字节）。</summary>
        public const int ReceiveBufferSize = 256 * 1024;

        /// <summary>WebSocket 最大消息大小（字节）。</summary>
        public const int MaxMessageSize = 16 * 1024 * 1024;

        /// <summary>WebSocket 关闭超时（毫秒）。</summary>
        public const int CloseTimeoutMs = 500;

        // ---- 认证配置 ----

        /// <summary>默认时钟偏移容忍度（秒）。</summary>
        public const double DefaultMaxClockSkewSec = 300.0;

        // ---- 会话配置 ----

        /// <summary>默认最大会话数。</summary>
        public const int DefaultMaxSessions = 8;

        /// <summary>控制台固定优先级。</summary>
        public const int ControllerPriority = 1000;

        /// <summary>Agent 默认优先级。</summary>
        public const int DefaultAgentPriority = 50;

        // ---- 发送配置 ----

        /// <summary>每次发送循环的最大帧数。</summary>
        public const int MaxFramesPerSendBatch = 64;

        /// <summary>每次发送循环的最大日志数。</summary>
        public const int MaxLogsPerSendBatch = 16;

        // ---- 日志配置 ----

        /// <summary>日志队列容量。</summary>
        public const int LogQueueCapacity = 128;

        /// <summary>每帧最大日志刷新数。</summary>
        public const int MaxLogsPerFrame = 32;

        /// <summary>日志消息最大长度。</summary>
        public const int LogMessageMaxLength = 8192;

        /// <summary>日志堆栈最大长度。</summary>
        public const int LogStackMaxLength = 16384;

        // ---- 主线程泵配置 ----

        /// <summary>每次执行的最大待处理动作数。</summary>
        public const int MaxActionsPerExecute = 256;
    }
}
