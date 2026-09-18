using System;

namespace X02Competition.Protocol
{
    /// <summary>协议标识生成。格式无兼容性要求（官方 SDK 仅透传），UUID v4 加前缀便于日志排查。</summary>
    public static class Ids
    {
        public static string NewEventId() => "evt-" + Guid.NewGuid().ToString("N");
        public static string NewItemId() => "item-" + Guid.NewGuid().ToString("N");
        public static string NewRobotCid() => "cid-" + Guid.NewGuid().ToString("N");
    }
}
