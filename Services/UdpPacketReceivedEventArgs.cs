using System;
using System.Net;

namespace HR_Codex_v0.Services
{
    public sealed class UdpPacketReceivedEventArgs : EventArgs
    {
        public UdpPacketReceivedEventArgs(byte[] data, IPEndPoint sourceEndPoint)
            : this(data, sourceEndPoint, DateTimeOffset.UtcNow)
        {
        }

        public UdpPacketReceivedEventArgs(byte[] data, IPEndPoint sourceEndPoint, DateTimeOffset receiveUtc)
        {
            Data = data ?? Array.Empty<byte>();
            Length = Data.Length;
            SourceEndPoint = sourceEndPoint;
            ReceiveUtc = receiveUtc;
            ReceiveEpochS = receiveUtc.ToUnixTimeMilliseconds() / 1000.0;
        }

        public byte[] Data { get; }
        public int Length { get; }
        public IPEndPoint SourceEndPoint { get; }
        public DateTimeOffset ReceiveUtc { get; }
        public double ReceiveEpochS { get; }
    }
}
