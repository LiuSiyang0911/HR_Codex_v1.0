using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HR_Codex_v0.Services
{
    public class RawDataSaveService
    {
        private const long MaxBytesPerFile = 10L * 1024 * 1024 * 1024;
        private readonly object _syncRoot = new object();
        private BlockingCollection<SaveItem> _queue;
        private string _directory;
        private string _tempFilePath;
        private string _finalFilePath;
        private long _savedBytes;
        private int _lastProgressTick;
        private bool _stopRequested;
        private DateTime _stopTime;

        public bool IsSaving { get; private set; }
        public bool IsStopping
        {
            get
            {
                lock (_syncRoot)
                    return _stopRequested;
            }
        }
        public string DirectoryPath => _directory ?? "";
        public string TempFilePath => _tempFilePath ?? "";
        public string FinalFilePath => _finalFilePath ?? "";
        public long SavedBytes => Interlocked.Read(ref _savedBytes);
        public double SavedGb => SavedBytes / 1024.0 / 1024.0 / 1024.0;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<bool> SavingStateChanged;
        public event EventHandler<long> SavedBytesChanged;

        public void Start(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Save directory is empty", nameof(directory));

            lock (_syncRoot)
            {
                if (IsSaving)
                    return;

                Directory.CreateDirectory(directory);
                _directory = directory;
                _tempFilePath = BuildTempPath(directory);
                _finalFilePath = "";
                _queue = new BlockingCollection<SaveItem>();
                _stopRequested = false;
                _stopTime = DateTime.MinValue;
                _lastProgressTick = Environment.TickCount;
                Interlocked.Exchange(ref _savedBytes, 0);
                IsSaving = true;
                Task.Run(() => WriteLoop(_queue, _tempFilePath, directory));
            }

            SavedBytesChanged?.Invoke(this, 0);
            SavingStateChanged?.Invoke(this, true);
            StatusChanged?.Invoke(this, $"Saving raw data in {directory}");
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                if (!IsSaving || _stopRequested)
                    return;

                _stopRequested = true;
                _stopTime = DateTime.Now;
                _queue?.CompleteAdding();
            }

            SavingStateChanged?.Invoke(this, true);
            StatusChanged?.Invoke(this, $"Stopping save, finalizing {SavedGb:F3} GB");
        }

        public void Enqueue(byte[] buffer, int length)
        {
            BlockingCollection<SaveItem> queue;

            lock (_syncRoot)
            {
                if (!IsSaving || _stopRequested || _queue == null || buffer == null || length <= 0)
                    return;

                queue = _queue;
            }

            try
            {
                queue.Add(new SaveItem(buffer, Math.Min(length, buffer.Length)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void WriteLoop(BlockingCollection<SaveItem> queue, string tempFilePath, string directory)
        {
            string finalMessage = null;

            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
                {
                    foreach (var item in queue.GetConsumingEnumerable())
                    {
                        long saved = SavedBytes;
                        long remaining = MaxBytesPerFile - saved;
                        if (remaining <= 0)
                        {
                            RequestStopFromWorker();
                            finalMessage = "Save reached 10 GB limit";
                            break;
                        }

                        int bytesToWrite = item.Length;
                        if (bytesToWrite > remaining)
                            bytesToWrite = (int)remaining;

                        stream.Write(item.Buffer, 0, bytesToWrite);
                        long total = Interlocked.Add(ref _savedBytes, bytesToWrite);
                        RaiseSavedBytesChangedThrottled(total);

                        if (bytesToWrite < item.Length || total >= MaxBytesPerFile)
                        {
                            RequestStopFromWorker();
                            finalMessage = "Save reached 10 GB limit";
                            break;
                        }
                    }
                }

                DateTime finalTime;
                lock (_syncRoot)
                    finalTime = _stopTime == DateTime.MinValue ? DateTime.Now : _stopTime;

                string finalPath = BuildFinalPath(directory, finalTime);
                File.Move(tempFilePath, finalPath);
                lock (_syncRoot)
                    _finalFilePath = finalPath;

                SavedBytesChanged?.Invoke(this, SavedBytes);
                StatusChanged?.Invoke(this, $"{finalMessage ?? "Save stopped"}, saved to {finalPath}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
            }
            finally
            {
                lock (_syncRoot)
                {
                    IsSaving = false;
                    _stopRequested = false;
                    _queue?.Dispose();
                    _queue = null;
                }

                SavingStateChanged?.Invoke(this, false);
            }
        }

        private void RequestStopFromWorker()
        {
            lock (_syncRoot)
            {
                _stopRequested = true;
                if (_stopTime == DateTime.MinValue)
                    _stopTime = DateTime.Now;
                _queue?.CompleteAdding();
            }
        }

        private void RaiseSavedBytesChangedThrottled(long total)
        {
            int now = Environment.TickCount;
            if (unchecked(now - _lastProgressTick) < 250)
                return;

            _lastProgressTick = now;
            SavedBytesChanged?.Invoke(this, total);
        }

        private string BuildTempPath(string directory)
        {
            string path;
            do
            {
                path = Path.Combine(directory, $"data_saving_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.tmp");
            }
            while (File.Exists(path));

            return path;
        }

        private string BuildFinalPath(string directory, DateTime timestamp)
        {
            string stamp = timestamp.ToString("yyyyMMdd_HHmmss");
            string basePath = Path.Combine(directory, $"data{stamp}.dat");
            if (!File.Exists(basePath))
                return basePath;

            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, $"data{stamp}_{i:000}.dat");
                if (!File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(directory, $"data{stamp}_{Guid.NewGuid():N}.dat");
        }

        private class SaveItem
        {
            public SaveItem(byte[] buffer, int length)
            {
                Buffer = buffer;
                Length = length;
            }

            public byte[] Buffer { get; }
            public int Length { get; }
        }
    }
}
