using System;
using System.Linq;

namespace HR_Codex_v0.Models
{
    public class EthCommand
    {
        public string Name { get; set; }
        public byte[] Cmd { get; set; } = new byte[14];

        public EthCommand() { }

        public EthCommand(string name, byte[] preset)
        {
            Name = name;
            if (preset != null && preset.Length == 14)
                preset.CopyTo(Cmd, 0);
        }

        public void SumCheck(int frameNum = 0)
        {
            Cmd[12] = (byte)frameNum;
            int sum = 0;
            for (int i = 1; i <= 12; i++) sum += Cmd[i];
            Cmd[13] = (byte)sum;
        }

        public void MotionSumCheck()
        {
            Cmd[10] = (byte)((Cmd[5] + Cmd[6] + Cmd[7] + Cmd[8] + Cmd[9]) & 0xFF);
            Cmd[11] = 0x00;
        }

        public override string ToString() => Name;

        public string ToHexString() => string.Join(" ", Cmd.Select(b => b.ToString("X2")));
    }

    public enum MotionType { Left, Right, Stop, Enable, ReadbackOn, ReadbackOff, QueryStart, QueryStop, QueryContent }
    public enum BandwidthType { Bw50, Bw75, Bw100, Bw125, Bw150, Bia2, PointFreq }
}

