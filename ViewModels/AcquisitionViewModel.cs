using HR_Codex_v0.Helpers;
using HR_Codex_v0.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Input;

namespace HR_Codex_v0.ViewModels
{
    public class AcquisitionViewModel : ObservableObject
    {
        private const int PacketLength = 1400;
        private const int FrameLen = 8192 + 4 + 1;
        private const int SamplesPerFrame = 8192;
        private const int NormalFrameLength = FrameLen * 4;
        private const int FrameSearchSlack = 1;
        private const int CyclicIntervalMs = 1000;
        private const int SingleProgressIntervalMs = 250;

        private readonly object _bufferLock = new object();
        private readonly Timer _cycleTimer = new Timer();
        private byte[] _receiveBuffer = new byte[0];
        private int _bufferIndex;
        private int _targetBytes;
        private int _lastProgressUpdateTick;
        private bool _isReceiving;
        private bool _isCyclic;
        private string _statusMessage = "";
        private string _dataTag = "";
        private double _receivedMb;
        private double _targetMb;

        public bool IsReceiving
        {
            get => _isReceiving;
            set
            {
                if (SetProperty(ref _isReceiving, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }
        public bool IsCyclic
        {
            get => _isCyclic;
            set
            {
                if (SetProperty(ref _isCyclic, value))
                    AppServices.SetCyclicAcquisitionActive(value);
            }
        }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public string DataTag
        {
            get => _dataTag;
            set
            {
                if (SetProperty(ref _dataTag, value))
                    AppServices.LastDataTag = value?.Trim() ?? "";
            }
        }
        public double ReceivedMb { get => _receivedMb; set => SetProperty(ref _receivedMb, value); }
        public double TargetMb { get => _targetMb; set => SetProperty(ref _targetMb, value); }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand CyclicCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand LoadCommand { get; }

        public AcquisitionViewModel()
        {
            StartCommand = new RelayCommand(_ => StartReceiving(), _ => !IsReceiving);
            StopCommand = new RelayCommand(_ => StopReceiving("已停止采集"), _ => IsReceiving);
            CyclicCommand = new RelayCommand(_ => ToggleCyclic());
            SaveCommand = new RelayCommand(_ => SaveData());
            LoadCommand = new RelayCommand(_ => LoadData());

            _cycleTimer.Interval = CyclicIntervalMs;
            _cycleTimer.AutoReset = true;
            _cycleTimer.Elapsed += OnCycleElapsed;

            AppServices.UdpService.DataReceived += OnDataReceivedContinuous;
        }

        private bool StartReceiving()
        {
            if (!AppServices.UdpService.IsConnected)
            {
                StatusMessage = "UDP 未连接，无法采集";
                return false;
            }

            int targetBytes = ComputeReceiveTargetBytes();
            int capacity = ComputeReceiveBufferCapacity(targetBytes);

            lock (_bufferLock)
            {
                _targetBytes = targetBytes;
                _receiveBuffer = new byte[capacity];
                _bufferIndex = 0;
                AppServices.LastDataBuffer = _receiveBuffer;
                AppServices.LastDataLength = 0;
                AppServices.LastDataTag = DataTag?.Trim() ?? "";
            }

            if (!AppServices.UdpService.StartReceiving() && !AppServices.UdpService.IsReceiving)
            {
                StatusMessage = "接收任务正在切换，等待下一次启动";
                IsReceiving = false;
                return false;
            }

            IsReceiving = true;
            ReceivedMb = 0;
            _lastProgressUpdateTick = Environment.TickCount;
            TargetMb = targetBytes / 1024.0 / 1024.0;
            StatusMessage = $"{(IsCyclic ? "循环" : "单次")}采集开始，目标 {TargetMb:F2} MB";
            return true;
        }

        private void StopReceiving(string message)
        {
            AppServices.UdpService.StopReceiving();

            lock (_bufferLock)
            {
                AppServices.LastDataBuffer = _receiveBuffer;
                AppServices.LastDataLength = _bufferIndex;
                ReceivedMb = _bufferIndex / 1024.0 / 1024.0;
            }

            IsReceiving = false;
            StatusMessage = $"{message}，已接收 {ReceivedMb:F2} MB";
        }

        private void ToggleCyclic()
        {
            IsCyclic = !IsCyclic;
            if (IsCyclic)
            {
                _cycleTimer.Start();
                if (!IsReceiving)
                    StartReceiving();
                StatusMessage = "循环采集已启动";
            }
            else
            {
                _cycleTimer.Stop();
                StatusMessage = "循环采集已停止";
            }
        }

        private void OnCycleElapsed(object sender, ElapsedEventArgs e)
        {
            AppServices.RunOnUi(() =>
            {
                if (IsCyclic && !IsReceiving)
                    StartReceiving();
            });
        }

        private async void OnDataReceivedContinuous(object sender, byte[] data)
        {
            if (data == null || data.Length == 0)
                return;

            var completedBatches = new List<(byte[] buffer, int length, string tag, bool isCyclic)>();
            bool full = false;
            double receivedMb;

            lock (_bufferLock)
            {
                if (!IsReceiving)
                    return;

                int dataOffset = 0;
                while (dataOffset < data.Length && IsReceiving)
                {
                    int blockLimit = Math.Min(_targetBytes, _receiveBuffer.Length);
                    int bytesToCopy = Math.Min(data.Length - dataOffset, blockLimit - _bufferIndex);
                    if (bytesToCopy > 0)
                    {
                        Buffer.BlockCopy(data, dataOffset, _receiveBuffer, _bufferIndex, bytesToCopy);
                        _bufferIndex += bytesToCopy;
                        dataOffset += bytesToCopy;
                    }

                    if (_bufferIndex >= _targetBytes || bytesToCopy == 0)
                    {
                        full = true;
                        bool isCyclic = IsCyclic;
                        completedBatches.Add((_receiveBuffer, _bufferIndex, DataTag?.Trim() ?? "", isCyclic));

                        if (!isCyclic)
                            break;

                        _receiveBuffer = new byte[ComputeReceiveBufferCapacity(_targetBytes)];
                        _bufferIndex = 0;
                    }
                }

                AppServices.LastDataBuffer = _receiveBuffer;
                AppServices.LastDataLength = _bufferIndex;
                receivedMb = _bufferIndex / 1024.0 / 1024.0;
            }

            int nowTick = Environment.TickCount;
            int progressInterval = IsCyclic ? CyclicIntervalMs : SingleProgressIntervalMs;
            if (full || unchecked(nowTick - _lastProgressUpdateTick) >= progressInterval)
            {
                _lastProgressUpdateTick = nowTick;
                AppServices.RunOnUi(() => ReceivedMb = receivedMb);
            }

            foreach (var batch in completedBatches)
            {
                if (!batch.isCyclic)
                    AppServices.RunOnUi(() => StopReceiving("Single acquisition completed"));
                else
                    AppServices.RunOnUi(() => StatusMessage = $"Cyclic acquisition batch completed, {batch.length / 1024.0 / 1024.0:F2} MB");

                AppServices.PublishDataBatchReady(batch.buffer, batch.length, batch.tag, batch.isCyclic);

                if (AppServices.UdpService.IsConnected)
                {
                    try
                    {
                        await AppServices.UdpService.SendAsync(new byte[] { 0x00 });
                    }
                    catch (Exception ex)
                    {
                        AppServices.RunOnUi(() =>
                            StatusMessage = $"{StatusMessage}; end notification failed: {ex.Message}");
                    }
                }
            }
        }

        private async void OnDataReceived(object sender, byte[] data)
        {
            bool full = false;
            double receivedMb;

            lock (_bufferLock)
            {
                if (!IsReceiving || data == null || data.Length == 0)
                    return;

                int bytesToCopy = Math.Min(data.Length, _receiveBuffer.Length - _bufferIndex);
                if (bytesToCopy > 0)
                {
                    Buffer.BlockCopy(data, 0, _receiveBuffer, _bufferIndex, bytesToCopy);
                    _bufferIndex += bytesToCopy;
                }

                if (_bufferIndex >= _targetBytes || bytesToCopy < data.Length)
                    full = true;

                AppServices.LastDataBuffer = _receiveBuffer;
                AppServices.LastDataLength = _bufferIndex;
                receivedMb = _bufferIndex / 1024.0 / 1024.0;
            }

            int nowTick = Environment.TickCount;
            int progressInterval = IsCyclic ? CyclicIntervalMs : SingleProgressIntervalMs;
            if (full || unchecked(nowTick - _lastProgressUpdateTick) >= progressInterval)
            {
                _lastProgressUpdateTick = nowTick;
                AppServices.RunOnUi(() => ReceivedMb = receivedMb);
            }

            if (full)
            {
                byte[] completedBuffer;
                int completedLength;
                string completedTag;
                bool isCyclic;
                lock (_bufferLock)
                {
                    completedBuffer = _receiveBuffer;
                    completedLength = _bufferIndex;
                    completedTag = DataTag?.Trim() ?? "";
                    isCyclic = IsCyclic;
                }

                AppServices.RunOnUi(() => StopReceiving(isCyclic ? "本轮采集已满" : "单次采集已满"));
                AppServices.PublishDataBatchReady(completedBuffer, completedLength, completedTag, isCyclic);

                if (AppServices.UdpService.IsConnected)
                {
                    try
                    {
                        await AppServices.UdpService.SendAsync(new byte[] { 0x00 });
                    }
                    catch (Exception ex)
                    {
                        AppServices.RunOnUi(() =>
                            StatusMessage = $"{StatusMessage}；结束通知发送失败: {ex.Message}");
                    }
                }
                // In cyclic mode the timer starts the next receive window.
                // This keeps acquisition and plotting at roughly 1 Hz.
            }
        }

        private void SaveData()
        {
            byte[] data;
            lock (_bufferLock)
                data = _receiveBuffer.Take(_bufferIndex).ToArray();

            if (data.Length == 0)
            {
                StatusMessage = "没有可保存的数据";
                return;
            }

            var dlg = new SaveFileDialog { Filter = "二进制数据文件(*.dat)|*.dat", FileName = BuildDefaultFileName() };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllBytes(dlg.FileName, data);
                AppServices.LastDataTag = DataTag?.Trim() ?? "";
                StatusMessage = $"已保存: {dlg.FileName}";
            }
        }

        private void LoadData()
        {
            var dlg = new OpenFileDialog { Filter = "二进制数据文件(*.dat)|*.dat" };
            if (dlg.ShowDialog() == true)
            {
                byte[] fileData = File.ReadAllBytes(dlg.FileName);
                lock (_bufferLock)
                {
                    _targetBytes = fileData.Length;
                    _receiveBuffer = fileData.Length > 0 ? fileData : new byte[0];
                    Buffer.BlockCopy(fileData, 0, _receiveBuffer, 0, fileData.Length);
                    _bufferIndex = fileData.Length;
                    AppServices.LastDataBuffer = _receiveBuffer;
                    AppServices.LastDataLength = _bufferIndex;
                    AppServices.LastDataTag = DataTag?.Trim() ?? "";
                    ReceivedMb = _bufferIndex / 1024.0 / 1024.0;
                    TargetMb = ReceivedMb;
                }

                StatusMessage = $"已加载: {dlg.FileName}";
            }
        }

        private int ComputeReceiveTargetBytes()
        {
            var cfg = AppServices.RadarConfig;
            int frameNum = (int)Math.Ceiling((double)cfg.Np * cfg.Nr / SamplesPerFrame);
            return Math.Max(1, frameNum + FrameSearchSlack) * NormalFrameLength;
        }

        private int ComputeReceiveBufferCapacity(int targetBytes)
        {
            return targetBytes + PacketLength * 2;
        }

        private string BuildDefaultFileName()
        {
            string tag = SanitizeFileNamePart(DataTag);
            string suffix = string.IsNullOrWhiteSpace(tag) ? "" : $"_{tag}";
            return $"Data{DateTime.Now:yyyyMMdd_HHmmss}{suffix}.dat";
        }

        private string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = value.Trim()
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray();
            return new string(chars);
        }
    }
}
