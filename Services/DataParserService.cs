using System;
using System.Collections.Generic;
using System.Linq;

namespace HR_Codex_v0.Services
{
    public class DataParserService
    {
        private readonly byte[] _frameHeader = new byte[] { 0xAA, 0xBB, 0x55, 0x66 };
        private const int NormalFrameLength = 32788;
        private const int FrameLen = 8192 + 4 + 1;
        private const int PtzDataLength = 16;

        public List<int> FindHeaders(byte[] buffer, int length)
        {
            var indices = new List<int>();
            if (buffer == null || length < _frameHeader.Length)
                return indices;

            int safeLength = Math.Min(length, buffer.Length);
            for (int i = 0; i <= safeLength - _frameHeader.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < _frameHeader.Length; j++)
                {
                    if (buffer[i + j] != _frameHeader[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    indices.Add(i);
            }

            return indices;
        }

        public (int startIndex, int frameLength) FindNormalFrames(List<int> headers)
        {
            if (headers == null || headers.Count < 2)
                return (-1, 0);

            for (int i = 0; i < headers.Count - 1; i++)
            {
                int len = headers[i + 1] - headers[i];
                if (len == NormalFrameLength)
                    return (i, len);
            }

            return (-1, 0);
        }

        public string BuildFrameDiagnostics(List<int> headers, int dataLength)
        {
            if (headers == null || headers.Count < 2)
                return $"帧头数 {headers?.Count ?? 0}，数据长度 {dataLength} bytes";

            int nearestLen = 0;
            int nearestDiff = int.MaxValue;
            int nearestIndex = 0;
            for (int i = 0; i < headers.Count - 1; i++)
            {
                int len = headers[i + 1] - headers[i];
                int diff = Math.Abs(len - NormalFrameLength);
                if (diff < nearestDiff)
                {
                    nearestDiff = diff;
                    nearestLen = len;
                    nearestIndex = i;
                }
            }

            return $"帧头数 {headers.Count}，数据长度 {dataLength} bytes，最接近帧距 {nearestLen}，标准帧距 {NormalFrameLength}，位置 {nearestIndex}";
        }

        public (short[] real, short[] imag, List<byte> ptzData) ParseFrames(byte[] buffer, int length, List<int> headers, int startIndex, int frameNum)
        {
            if (buffer == null)
                throw new InvalidOperationException("数据缓冲区为空");
            if (headers == null || startIndex < 0 || startIndex >= headers.Count)
                throw new InvalidOperationException("帧头索引无效");
            if (frameNum <= 0)
                throw new InvalidOperationException("帧数无效");

            int safeLength = Math.Min(length, buffer.Length);
            int firstHeader = headers[startIndex];
            int requiredLength = firstHeader + frameNum * FrameLen * 4;
            if (requiredLength > safeLength)
                throw new InvalidOperationException($"数据长度不足：需要 {requiredLength} 字节，实际 {safeLength} 字节");

            const int ptzSamplesPerFrame = 4;
            int samplesPerFrame = FrameLen - 1 - ptzSamplesPerFrame;
            int totalSamples = frameNum * samplesPerFrame;

            var real = new short[totalSamples];
            var imag = new short[totalSamples];
            var ptzData = new List<byte>(frameNum * PtzDataLength);

            int sampleIdx = 0;
            for (int i = 0; i < frameNum; i++)
            {
                int headerPos = firstHeader + i * FrameLen * 4;
                for (int j = 0; j < FrameLen; j++)
                {
                    int byteIdx = headerPos + j * 4;
                    if (byteIdx + 3 >= safeLength)
                        throw new InvalidOperationException($"帧数据越界：位置 {byteIdx}，长度 {safeLength}");

                    if (j >= 2 && j <= 8 && j % 2 == 0)
                    {
                        for (int k = 3; k >= 0; k--)
                            ptzData.Add(buffer[byteIdx + k]);
                    }
                    else if (j != 0)
                    {
                        real[sampleIdx] = BitConverter.ToInt16(buffer, byteIdx);
                        imag[sampleIdx] = BitConverter.ToInt16(buffer, byteIdx + 2);
                        sampleIdx++;
                    }
                }
            }

            return (real, imag, ptzData);
        }

        public double CalculateMedianAzimuth(List<byte> ptzData)
        {
            if (ptzData == null || ptzData.Count % PtzDataLength != 0)
                return 0;

            var azimuths = new List<double>();
            for (int i = 0; i < ptzData.Count; i += PtzDataLength)
            {
                if (IsValidPtzFrame(ptzData, i))
                {
                    double az = (ptzData[i + 4] << 8 | ptzData[i + 5]) / 100.0;
                    azimuths.Add(360 - az);
                }
            }

            return azimuths.Count > 0 ? azimuths.OrderBy(a => a).ElementAt(azimuths.Count / 2) : 0;
        }

        private bool IsValidPtzFrame(List<byte> data, int index)
        {
            if (data == null || index < 0 || index + 6 >= data.Count)
                return false;

            int sum = 0;
            for (int i = index + 1; i <= index + 5; i++)
                sum += data[i];

            return data[index + 6] == (sum % 256);
        }
    }
}
