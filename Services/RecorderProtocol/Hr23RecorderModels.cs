using System;
using System.Collections.Generic;

namespace HR_Codex_v0.Services.RecorderProtocol
{
    public sealed class Hr23RecorderPrepareRequest
    {
        public string SessionId { get; set; }
        public string OutputDir { get; set; }
        public Dictionary<string, object> TimeBase { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }

    public sealed class Hr23RecorderResponse
    {
        public bool Ok { get; set; }
        public string State { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
        public long PacketCount { get; set; }
        public long TotalBytes { get; set; }
        public DateTimeOffset? FirstPacketUtc { get; set; }
        public DateTimeOffset? LastPacketUtc { get; set; }
        public DateTimeOffset? RawFileClosedUtc { get; set; }
        public double? StartEpochS { get; set; }
        public DateTimeOffset? StartUtc { get; set; }
    }

    internal sealed class Hr23RecorderPacket
    {
        public byte[] Data { get; set; }
        public string SourceIp { get; set; }
        public int SourcePort { get; set; }
        public DateTimeOffset ReceiveUtc { get; set; }
        public double ReceiveEpochS { get; set; }
    }
}
