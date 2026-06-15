using HR_Codex_v0.Models;
using System.Collections.Generic;

namespace HR_Codex_v0.Services
{
    public static class CommandFactory
    {
        private static readonly byte[] DefaultCmd = new byte[14] { 0xA5, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        public static EthCommand BuildRfCommand(bool on)
        {
            return new EthCommand(on ? "功放开" : "功放关",
                on
                    ? new byte[14] { 0xA5, 0x10, 0xA1, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x21, 0xD5 }
                    : new byte[14] { 0xA5, 0x10, 0xA1, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x21, 0xD4 });
        }

        public static EthCommand BuildMotionCommand(MotionType type, byte speed = 5)
        {
            var cmd = new EthCommand(type.ToString(), DefaultCmd);
            cmd.Cmd[1] = 0x10;
            cmd.Cmd[2] = 0xA1;

            switch (type)
            {
                case MotionType.Left:
                    BuildMotionBase(cmd, speed);
                    cmd.Cmd[7] = 0x04;
                    cmd.Name = "转台左转";
                    cmd.MotionSumCheck();
                    cmd.SumCheck(0);
                    break;
                case MotionType.Right:
                    BuildMotionBase(cmd, speed);
                    cmd.Cmd[7] = 0x02;
                    cmd.Name = "转台右转";
                    cmd.MotionSumCheck();
                    cmd.SumCheck(0);
                    break;
                case MotionType.Stop:
                    BuildMotionBase(cmd, 0);
                    cmd.Cmd[7] = 0x00;
                    cmd.Name = "转台停止";
                    cmd.MotionSumCheck();
                    cmd.SumCheck(0);
                    break;
                case MotionType.Enable:
                    cmd.Cmd[3] = 0x04;
                    cmd.Cmd[4] = 0x01;
                    cmd.Name = "转台使能";
                    cmd.SumCheck(0);
                    break;
                case MotionType.ReadbackOn:
                    BuildMotionBase(cmd, 0);
                    cmd.Cmd[7] = 0x09;
                    cmd.Cmd[9] = 0x05;
                    cmd.Name = "打开回读";
                    cmd.MotionSumCheck();
                    cmd.SumCheck(0);
                    break;
                case MotionType.ReadbackOff:
                    BuildMotionBase(cmd, 0);
                    cmd.Cmd[7] = 0x0B;
                    cmd.Cmd[9] = 0x05;
                    cmd.Name = "关闭回读";
                    cmd.MotionSumCheck();
                    cmd.SumCheck(0);
                    break;
                case MotionType.QueryStart:
                    cmd.Cmd[3] = 0x06;
                    cmd.Cmd[4] = 0x01;
                    cmd.Cmd[8] = 0x80;
                    cmd.Cmd[9] = 0x96;
                    cmd.Cmd[10] = 0x98;
                    cmd.Name = "开始查询";
                    cmd.SumCheck(0);
                    break;
                case MotionType.QueryStop:
                    cmd.Cmd[3] = 0x06;
                    cmd.Cmd[4] = 0x00;
                    cmd.Cmd[8] = 0x80;
                    cmd.Cmd[9] = 0x96;
                    cmd.Cmd[10] = 0x98;
                    cmd.Name = "结束查询";
                    cmd.SumCheck(0);
                    break;
                case MotionType.QueryContent:
                    BuildMotionBase(cmd, 0);
                    cmd.Cmd[7] = 0x51;
                    cmd.Name = "查询内容";
                    cmd.MotionSumCheck();
                    cmd.SumCheck(0);
                    break;
            }

            return cmd;
        }

        public static EthCommand BuildWaveformCommand(BandwidthType type)
        {
            var cmd = new EthCommand(type.ToString(), DefaultCmd);
            cmd.Cmd[1] = 0x10;
            cmd.Cmd[2] = 0xA1;
            switch (type)
            {
                case BandwidthType.Bia2: cmd.Cmd[3] = 0x08; cmd.Cmd[4] = 0x22; cmd.Name = "本振2955+1500"; break;
                case BandwidthType.Bw50: cmd.Cmd[3] = 0x07; cmd.Cmd[4] = 0x11; cmd.Name = "谐波带宽50MHz"; break;
                case BandwidthType.Bw75: cmd.Cmd[3] = 0x07; cmd.Cmd[4] = 0x22; cmd.Name = "谐波带宽75MHz"; break;
                case BandwidthType.Bw100: cmd.Cmd[3] = 0x07; cmd.Cmd[4] = 0x33; cmd.Name = "谐波带宽100MHz"; break;
                case BandwidthType.Bw125: cmd.Cmd[3] = 0x07; cmd.Cmd[4] = 0x44; cmd.Name = "谐波带宽125MHz"; break;
                case BandwidthType.Bw150: cmd.Cmd[3] = 0x07; cmd.Cmd[4] = 0x55; cmd.Name = "谐波带宽150MHz"; break;
                case BandwidthType.PointFreq: cmd.Cmd[3] = 0x07; cmd.Cmd[4] = 0xFF; cmd.Name = "收发点频"; break;
            }
            cmd.SumCheck(0);
            return cmd;
        }

        public static List<EthCommand> GetAllWaveformCommands()
        {
            return new List<EthCommand>
            {
                BuildWaveformCommand(BandwidthType.Bia2),
                BuildWaveformCommand(BandwidthType.Bw50),
                BuildWaveformCommand(BandwidthType.Bw75),
                BuildWaveformCommand(BandwidthType.Bw100),
                BuildWaveformCommand(BandwidthType.Bw125),
                BuildWaveformCommand(BandwidthType.Bw150),
                BuildWaveformCommand(BandwidthType.PointFreq)
            };
        }

        private static void BuildMotionBase(EthCommand cmd, byte speed)
        {
            cmd.Cmd[3] = 0x05;
            cmd.Cmd[4] = 0xFF;
            cmd.Cmd[5] = 0x01;
            cmd.Cmd[6] = 0x00;
            cmd.Cmd[8] = speed;
            cmd.Cmd[9] = 0x00;
        }
    }
}
