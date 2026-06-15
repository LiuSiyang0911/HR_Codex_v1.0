using HR_Codex_v0.Helpers;
using HR_Codex_v0.Models;
using HR_Codex_v0.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace HR_Codex_v0.ViewModels
{
    public class ProcessingViewModel : ObservableObject
    {
        private const int AutoProcessIntervalMs = 1000;
        private readonly object _queueLock = new object();
        private readonly object _signalLock = new object();
        private AcquiredDataBatch _pendingBatch;
        private bool _isAutoProcessing;
        private int _droppedBatches;
        private string _selectedMode = "RDM";
        private string _statusMessage = "";
        private string _saveFilePath;
        private string _saveStatusMessage = "Raw data save idle";
        private bool _isSavingRawData;
        private bool _isStoppingRawDataSave;
        private double _savedRawDataGb;

        public string SelectedMode
        {
            get => _selectedMode;
            set => SetProperty(ref _selectedMode, "RDM");
        }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public string SaveFilePath { get => _saveFilePath; set => SetProperty(ref _saveFilePath, value); }
        public string SaveStatusMessage { get => _saveStatusMessage; set => SetProperty(ref _saveStatusMessage, value); }
        public bool IsSavingRawData
        {
            get => _isSavingRawData;
            set
            {
                if (SetProperty(ref _isSavingRawData, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                    OnPropertyChanged(nameof(IsSavePathEditable));
                    OnPropertyChanged(nameof(CanStartRawDataSave));
                    OnPropertyChanged(nameof(CanStopRawDataSave));
                }
            }
        }
        public bool IsStoppingRawDataSave
        {
            get => _isStoppingRawDataSave;
            set
            {
                if (SetProperty(ref _isStoppingRawDataSave, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                    OnPropertyChanged(nameof(CanStartRawDataSave));
                    OnPropertyChanged(nameof(CanStopRawDataSave));
                }
            }
        }
        public bool IsSavePathEditable => !IsSavingRawData;
        public bool CanStartRawDataSave => !IsSavingRawData;
        public bool CanStopRawDataSave => IsSavingRawData && !IsStoppingRawDataSave;
        public double SavedRawDataGb { get => _savedRawDataGb; set => SetProperty(ref _savedRawDataGb, value); }

        public double[] FftProfile { get; private set; }
        public double[,] RdmMap { get; private set; }
        public double[,] PpiMap { get; private set; }
        public double[] Displacement { get; private set; }
        public double[] TdProfile { get; private set; }
        public double[] TdPeaks { get; private set; }
        public DetectionPoint[] DetectionPoints { get; private set; }

        public ICommand ProcessCommand { get; }
        public ICommand ClearPointsCommand { get; }
        public ICommand BrowseSavePathCommand { get; }
        public ICommand StartSaveCommand { get; }
        public ICommand StopSaveCommand { get; }

        public event Action ProcessingCompleted;

        public ProcessingViewModel()
        {
            ProcessCommand = new RelayCommand(_ => ProcessLatestManually(), _ => AppServices.SignalProcessing.State.Nr > 0);
            ClearPointsCommand = new RelayCommand(_ => ClearPoints());
            BrowseSavePathCommand = new RelayCommand(_ => BrowseSavePath(), _ => !IsSavingRawData);
            StartSaveCommand = new RelayCommand(_ => StartRawDataSave(), _ => CanStartRawDataSave);
            StopSaveCommand = new RelayCommand(_ => StopRawDataSave(), _ => CanStopRawDataSave);
            SaveFilePath = BuildDefaultSaveDirectory();
            IsSavingRawData = AppServices.RawDataSaver.IsSaving;
            SavedRawDataGb = AppServices.RawDataSaver.SavedGb;
            AppServices.RawDataSaver.StatusChanged += OnRawDataSaveStatusChanged;
            AppServices.RawDataSaver.SavingStateChanged += OnRawDataSaveStateChanged;
            AppServices.RawDataSaver.SavedBytesChanged += OnRawDataSavedBytesChanged;
            AppServices.DataBatchReady += OnDataBatchReady;
        }

        public void ClearPoints()
        {
            AppServices.RunOnUi(() =>
            {
                DetectionPoints = null;
                AppServices.ClearDetectionPoints();
                OnPropertyChanged(nameof(DetectionPoints));
                ProcessingCompleted?.Invoke();
            });
        }

        private void BrowseSavePath()
        {
            using (var dlg = new Forms.FolderBrowserDialog())
            {
                dlg.Description = "Select raw data save folder";
                dlg.SelectedPath = GetInitialSaveDirectory();
                dlg.ShowNewFolderButton = true;

                if (dlg.ShowDialog() == Forms.DialogResult.OK)
                    SaveFilePath = dlg.SelectedPath;
            }
        }

        private void StartRawDataSave()
        {
            try
            {
                string directory = string.IsNullOrWhiteSpace(SaveFilePath) ? BuildDefaultSaveDirectory() : SaveFilePath;
                SaveFilePath = directory;
                AppServices.RawDataSaver.Start(directory);
            }
            catch (Exception ex)
            {
                SaveStatusMessage = $"Save start failed: {ex.Message}";
            }
        }

        private void StopRawDataSave()
        {
            IsStoppingRawDataSave = true;
            AppServices.RawDataSaver.Stop();
        }

        private void OnRawDataSaveStatusChanged(object sender, string message)
        {
            RunOnUiAsync(() => SaveStatusMessage = message);
        }

        private void OnRawDataSaveStateChanged(object sender, bool isSaving)
        {
            RunOnUiAsync(() =>
            {
                IsSavingRawData = isSaving;
                IsStoppingRawDataSave = AppServices.RawDataSaver.IsStopping;
            });
        }

        private void OnRawDataSavedBytesChanged(object sender, long bytes)
        {
            RunOnUiAsync(() => SavedRawDataGb = bytes / 1024.0 / 1024.0 / 1024.0);
        }

        private void RunOnUiAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }

        private string BuildDefaultSaveDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "HR_Codex_v1.0",
                "RawData");
        }

        private string GetInitialSaveDirectory()
        {
            return string.IsNullOrWhiteSpace(SaveFilePath) ? BuildDefaultSaveDirectory() : SaveFilePath;
        }

        private async void ProcessLatestManually()
        {
            var buffer = AppServices.LastDataBuffer;
            var length = AppServices.LastDataLength;
            if (buffer == null || length == 0)
            {
                StatusMessage = "无数据，请先采集或加载";
                return;
            }

            var batch = new AcquiredDataBatch(buffer, Math.Min(length, buffer.Length), AppServices.LastDataTag, 0, DateTime.Now, false);
            StatusMessage = "手动处理开始";
            var result = await Task.Run(() => ProcessBatch(batch, SelectedMode, "手动处理", 0));
            ApplyResult(result);
        }

        private void OnDataBatchReady(object sender, AcquiredDataBatch batch)
        {
            if (batch == null)
                return;

            if (!batch.IsCyclic)
            {
                Task.Run(() =>
                {
                    string mode = SelectedMode;
                    var result = ProcessBatch(batch, mode, $"单次批次 #{batch.Sequence}", 0);
                    ApplyResult(result);
                });
                return;
            }

            lock (_queueLock)
            {
                if (_pendingBatch != null)
                    _droppedBatches++;

                _pendingBatch = batch;

                if (_isAutoProcessing)
                    return;

                _isAutoProcessing = true;
            }

            Task.Run(ProcessAutoLoopAsync);
        }

        private async Task ProcessAutoLoopAsync()
        {
            while (true)
            {
                await Task.Delay(AutoProcessIntervalMs);

                AcquiredDataBatch batch;
                int dropped;
                lock (_queueLock)
                {
                    batch = _pendingBatch;
                    dropped = _droppedBatches;
                    _pendingBatch = null;
                    _droppedBatches = 0;
                }

                if (batch == null)
                {
                    lock (_queueLock)
                    {
                        if (_pendingBatch == null)
                        {
                            _isAutoProcessing = false;
                            return;
                        }
                    }

                    continue;
                }

                string mode = SelectedMode;
                string source = batch.IsCyclic ? $"循环批次 #{batch.Sequence}" : $"单次批次 #{batch.Sequence}";
                var result = ProcessBatch(batch, mode, source, dropped);
                ApplyResult(result);
            }
        }

        private ProcessingResult ProcessBatch(AcquiredDataBatch batch, string mode, string source, int dropped)
        {
            var swTotal = Stopwatch.StartNew();
            var swParse = new Stopwatch();
            var swSignal = new Stopwatch();
            var result = new ProcessingResult { Mode = mode, Source = source, DroppedBatches = dropped };

            try
            {
                var parser = AppServices.DataParser;

                swParse.Start();
                var headers = parser.FindHeaders(batch.Buffer, batch.Length);
                if (headers.Count < 2)
                {
                    swParse.Stop();
                    result.StatusMessage = $"{source} 未找到有效帧头：数据长度 {batch.Length} bytes";
                    return result;
                }

                var (startIdx, _) = parser.FindNormalFrames(headers);
                if (startIdx < 0)
                {
                    swParse.Stop();
                    result.StatusMessage = $"{source} 未找到标准帧：{parser.BuildFrameDiagnostics(headers, batch.Length)}";
                    return result;
                }

                const int samplesPerFrame = 8192;
                int frameNum = (int)Math.Ceiling((double)AppServices.RadarConfig.Np * AppServices.RadarConfig.Nr / samplesPerFrame);
                var (real, imag, ptz) = parser.ParseFrames(batch.Buffer, batch.Length, headers, startIdx, frameNum);
                double azimuth = parser.CalculateMedianAzimuth(ptz);
                swParse.Stop();

                swSignal.Start();
                lock (_signalLock)
                {
                    ProcessSignal(mode, real, imag, azimuth, result);
                }
                swSignal.Stop();

                swTotal.Stop();
                string droppedText = dropped > 0 ? $"，丢弃旧批次 {dropped} 个" : "";
                result.StatusMessage = $"{source} {mode} 完成，总耗时 {swTotal.Elapsed.TotalSeconds:F2} s" +
                    $"（读数据 {swParse.Elapsed.TotalSeconds:F2} s + 信号处理 {swSignal.Elapsed.TotalSeconds:F2} s）{droppedText}";
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                result.StatusMessage = $"{source} 处理失败: {ex.Message} (总耗时 {swTotal.Elapsed.TotalSeconds:F2} s)";
            }

            return result;
        }

        private void ProcessSignal(string mode, short[] real, short[] imag, double azimuth, ProcessingResult result)
        {
            var cfg = AppServices.RadarConfig;
            var sp = AppServices.SignalProcessing;
            int offset = cfg.Offset;

            switch (mode)
            {
                case "FFT":
                    result.FftProfile = sp.ProcessFFT(real, imag, offset);
                    break;
                case "PPI":
                    result.PpiMap = sp.ProcessPPI(real, imag, offset, azimuth);
                    break;
                case "RDM":
                    var (rdm, pts) = sp.ProcessRDM(real, imag, offset, cfg.Xlim, cfg.Threshold);
                    result.RdmMap = rdm;
                    result.DetectionPoints = pts.Take(cfg.PointsMax).ToArray();
                    result.FftProfile = BuildRangeProfile(rdm);
                    break;
                case "RDM_ref":
                    result.RdmMap = sp.ProcessRDM_ref(real, imag, offset);
                    break;
                case "RDM_in":
                    var (rdmIn, disp) = sp.ProcessRDM_in(real, imag, offset, (int)cfg.Rr);
                    result.RdmMap = rdmIn;
                    result.Displacement = disp;
                    break;
                case "TD":
                    var (prof, peaks, _) = sp.ProcessTD(real, imag, offset);
                    result.TdProfile = prof;
                    result.TdPeaks = peaks;
                    break;
            }
        }

        private double[] BuildRangeProfile(double[,] rdm)
        {
            int ncut = rdm.GetLength(0);
            int np = rdm.GetLength(1);
            var profile = new double[ncut];
            for (int i = 0; i < ncut; i++)
            {
                double max = double.MinValue;
                for (int j = 0; j < np; j++)
                    if (rdm[i, j] > max) max = rdm[i, j];
                profile[i] = max;
            }
            return profile;
        }

        private void ApplyResult(ProcessingResult result)
        {
            AppServices.RunOnUi(() =>
            {
                FftProfile = result.FftProfile;
                RdmMap = result.RdmMap;
                PpiMap = result.PpiMap;
                Displacement = result.Displacement;
                TdProfile = result.TdProfile;
                TdPeaks = result.TdPeaks;
                DetectionPoints = result.DetectionPoints;

                DetectionAddResult detectionResult = null;
                if (DetectionPoints != null && DetectionPoints.Length > 0)
                    detectionResult = AppServices.AddDetectionPoints(DetectionPoints);

                StatusMessage = detectionResult != null && !string.IsNullOrWhiteSpace(detectionResult.StatusMessage)
                    ? $"{result.StatusMessage}；{detectionResult.StatusMessage}"
                    : result.StatusMessage;
                ProcessingCompleted?.Invoke();
            });
        }

        private class ProcessingResult
        {
            public string Mode { get; set; }
            public string Source { get; set; }
            public string StatusMessage { get; set; }
            public int DroppedBatches { get; set; }
            public double[] FftProfile { get; set; }
            public double[,] RdmMap { get; set; }
            public double[,] PpiMap { get; set; }
            public double[] Displacement { get; set; }
            public double[] TdProfile { get; set; }
            public double[] TdPeaks { get; set; }
            public DetectionPoint[] DetectionPoints { get; set; }
        }
    }
}
