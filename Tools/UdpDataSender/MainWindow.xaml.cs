using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace UdpDataSender
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource _cts;
        private Stopwatch _stopwatch;
        private long _sentBytes;
        private long _sentPackets;
        private long _lastUiBytes;
        private TimeSpan _lastUiTime;

        public MainWindow()
        {
            InitializeComponent();
            RefreshRateHint();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "数据文件 (*.dat)|*.dat|所有文件 (*.*)|*.*",
                FileName = FilePathBox.Text
            };

            if (dialog.ShowDialog() == true)
                FilePathBox.Text = dialog.FileName;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = ReadSettings();
                if (!File.Exists(settings.FilePath))
                {
                    AppendLog($"文件不存在: {settings.FilePath}");
                    return;
                }

                _cts = new CancellationTokenSource();
                _stopwatch = Stopwatch.StartNew();
                _sentBytes = 0;
                _sentPackets = 0;
                _lastUiBytes = 0;
                _lastUiTime = TimeSpan.Zero;

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusText.Text = "发送中";
                ProgressBar.Value = 0;

                AppendLog($"开始发送: {settings.FilePath}");
                AppendLog($"目标: {settings.TargetEndPoint}, 源端口: {settings.SourcePort}, 包长: {settings.PacketSize} bytes");
                AppendLog($"采样率: {settings.SampleRateMHz:F3} MHz, 每采样 {settings.BytesPerSample} bytes, 目标速率: {settings.TargetBytesPerSecond / 1024.0 / 1024.0:F2} MiB/s");

                await Task.Run(() => SendFileLoop(settings, _cts.Token), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                AppendLog("发送已停止");
            }
            catch (Exception ex)
            {
                AppendLog($"发送失败: {ex.Message}");
            }
            finally
            {
                _stopwatch?.Stop();
                _cts?.Dispose();
                _cts = null;
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusText.Text = "空闲";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void RefreshRateButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshRateHint();
        }

        private void SendFileLoop(SenderSettings settings, CancellationToken token)
        {
            using (var udp = CreateUdpClient(settings))
            {
                udp.Connect(settings.TargetEndPoint);

                if (settings.WaitAck)
                    _ = Task.Run(() => ReceiveAckLoop(udp, token), token);

                do
                {
                    SendOnePass(udp, settings, token);
                    if (settings.Loop)
                        AppendLog("文件发送完成，开始下一轮循环");
                }
                while (settings.Loop && !token.IsCancellationRequested);
            }
        }

        private void SendOnePass(UdpClient udp, SenderSettings settings, CancellationToken token)
        {
            var buffer = new byte[settings.PacketSize];
            long passBytes = 0;

            using (var stream = new FileStream(settings.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, settings.PacketSize * 256, FileOptions.SequentialScan))
            {
                long startOffset = FindFirstFrameHeader(stream);
                if (startOffset > 0)
                {
                    stream.Position = startOffset;
                    AppendLog($"从首个帧头偏移 {startOffset} bytes 开始发送");
                }

                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    udp.Client.Send(buffer, 0, read, SocketFlags.None);

                    _sentBytes += read;
                    passBytes += read;
                    _sentPackets++;

                    PaceSender(_sentBytes, settings.TargetBytesPerSecond, token);
                    UpdateUi(settings, stream.Length, passBytes);
                }
            }
        }

        private long FindFirstFrameHeader(FileStream stream)
        {
            byte[] header = { 0xAA, 0xBB, 0x55, 0x66 };
            long originalPosition = stream.Position;
            stream.Position = 0;

            int matched = 0;
            long position = 0;
            int value;
            while ((value = stream.ReadByte()) >= 0)
            {
                if ((byte)value == header[matched])
                {
                    matched++;
                    if (matched == header.Length)
                    {
                        long headerPosition = position - header.Length + 1;
                        stream.Position = originalPosition;
                        return headerPosition;
                    }
                }
                else
                {
                    matched = (byte)value == header[0] ? 1 : 0;
                }
                position++;
            }

            stream.Position = originalPosition;
            return 0;
        }

        private UdpClient CreateUdpClient(SenderSettings settings)
        {
            UdpClient udp;
            if (settings.SourcePort > 0)
            {
                var localAddress = string.IsNullOrWhiteSpace(settings.SourceIp)
                    ? IPAddress.Any
                    : IPAddress.Parse(settings.SourceIp);
                udp = new UdpClient(new IPEndPoint(localAddress, settings.SourcePort));
            }
            else
            {
                udp = new UdpClient();
            }

            udp.Client.SendBufferSize = 4 * 1024 * 1024;
            udp.Client.ReceiveBufferSize = 1024 * 1024;
            udp.Client.ReceiveTimeout = 200;
            return udp;
        }

        private void ReceiveAckLoop(UdpClient udp, CancellationToken token)
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = udp.Receive(ref remote);
                    if (data != null && data.Length > 0)
                        Dispatcher.BeginInvoke(new Action(() => AppendLog($"收到 ACK: {BitConverter.ToString(data)} from {remote}")));
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() => AppendLog($"ACK 接收停止: {ex.Message}")));
                    break;
                }
            }
        }

        private void PaceSender(long totalBytes, double targetBytesPerSecond, CancellationToken token)
        {
            if (targetBytesPerSecond <= 0)
                return;

            double expectedSeconds = totalBytes / targetBytesPerSecond;
            while (_stopwatch.Elapsed.TotalSeconds < expectedSeconds)
            {
                token.ThrowIfCancellationRequested();
                double waitMs = (expectedSeconds - _stopwatch.Elapsed.TotalSeconds) * 1000.0;
                if (waitMs > 2)
                    Thread.Sleep(Math.Min(5, (int)waitMs));
                else
                    Thread.SpinWait(200);
            }
        }

        private void UpdateUi(SenderSettings settings, long fileLength, long passBytes)
        {
            var now = _stopwatch.Elapsed;
            if ((now - _lastUiTime).TotalMilliseconds < 100)
                return;

            long deltaBytes = _sentBytes - _lastUiBytes;
            double deltaSeconds = Math.Max(0.001, (now - _lastUiTime).TotalSeconds);
            double speed = deltaBytes / deltaSeconds / 1024.0 / 1024.0;
            double progress = fileLength <= 0 ? 0 : Math.Min(100, passBytes * 100.0 / fileLength);

            _lastUiBytes = _sentBytes;
            _lastUiTime = now;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                SentBytesText.Text = $"{_sentBytes / 1024.0 / 1024.0:F2} MiB";
                SpeedText.Text = $"{speed:F2} MiB/s";
                PacketCountText.Text = _sentPackets.ToString(CultureInfo.InvariantCulture);
                ElapsedText.Text = now.ToString(@"hh\:mm\:ss");
                ProgressBar.Value = progress;
                StatusText.Text = settings.Loop ? $"发送中：本轮 {progress:F1}%" : $"发送中：{progress:F1}%";
            }));
        }

        private SenderSettings ReadSettings()
        {
            string filePath = FilePathBox.Text.Trim();
            var target = new IPEndPoint(IPAddress.Parse(TargetIpBox.Text.Trim()), ParseInt(TargetPortBox.Text, "目标端口"));
            int packetSize = ParseInt(PacketSizeBox.Text, "UDP 包长");
            if (packetSize <= 0 || packetSize > 65507)
                throw new InvalidOperationException("UDP 包长必须在 1 到 65507 之间");

            double sampleRateMHz = ParseDouble(SampleRateBox.Text, "采样率");
            int bytesPerSample = ParseInt(BytesPerSampleBox.Text, "每采样字节数");
            double rateScale = ParseDouble(RateScaleBox.Text, "速率倍率");
            double targetBytesPerSecond = sampleRateMHz * 1_000_000.0 * bytesPerSample * rateScale;

            return new SenderSettings
            {
                FilePath = filePath,
                TargetEndPoint = target,
                SourceIp = SourceIpBox.Text.Trim(),
                SourcePort = ParseInt(SourcePortBox.Text, "源端口"),
                PacketSize = packetSize,
                SampleRateMHz = sampleRateMHz,
                BytesPerSample = bytesPerSample,
                RateScale = rateScale,
                TargetBytesPerSecond = targetBytesPerSecond,
                Loop = LoopBox.IsChecked == true,
                WaitAck = WaitAckBox.IsChecked == true
            };
        }

        private void RefreshRateHint()
        {
            try
            {
                double sampleRateMHz = ParseDouble(SampleRateBox.Text, "采样率");
                int bytesPerSample = ParseInt(BytesPerSampleBox.Text, "每采样字节数");
                double rateScale = ParseDouble(RateScaleBox.Text, "速率倍率");
                double bytesPerSecond = sampleRateMHz * 1_000_000.0 * bytesPerSample * rateScale;
                RateHintText.Text = $"目标速率: {bytesPerSecond / 1024.0 / 1024.0:F2} MiB/s";
            }
            catch
            {
                RateHintText.Text = "目标速率: 参数无效";
            }
        }

        private int ParseInt(string value, string name)
        {
            if (!int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                throw new InvalidOperationException($"{name} 不是有效整数");
            return result;
        }

        private double ParseDouble(string value, string name)
        {
            if (!double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                throw new InvalidOperationException($"{name} 不是有效数字");
            return result;
        }

        private void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(message)));
                return;
            }

            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogBox.ScrollToEnd();
        }

        private class SenderSettings
        {
            public string FilePath { get; set; }
            public IPEndPoint TargetEndPoint { get; set; }
            public string SourceIp { get; set; }
            public int SourcePort { get; set; }
            public int PacketSize { get; set; }
            public double SampleRateMHz { get; set; }
            public int BytesPerSample { get; set; }
            public double RateScale { get; set; }
            public double TargetBytesPerSecond { get; set; }
            public bool Loop { get; set; }
            public bool WaitAck { get; set; }
        }
    }
}
