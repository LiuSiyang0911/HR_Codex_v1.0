using HR_Codex_v0.Models;
using HR_Codex_v0.Services.RecorderProtocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;

namespace HR_Codex_v0.Services
{
    public static class AppServices
    {
        private static int _detectionSession;
        private static int _dataBatchSequence;

        public static UdpService UdpService { get; } = new UdpService();
        public static SignalProcessingService SignalProcessing { get; } = new SignalProcessingService();
        public static DataParserService DataParser { get; } = new DataParserService();
        public static RawDataSaveService RawDataSaver { get; } = new RawDataSaveService();
        public static Hr23RecorderSession Hr23Recorder { get; } = new Hr23RecorderSession();
        public static Hr23RecorderServer Hr23RecorderServer { get; } = CreateHr23RecorderServer();
        public static RadarConfig RadarConfig { get; } = new RadarConfig();
        public static ObservableCollection<DetectionPoint> DetectionPoints { get; } = new ObservableCollection<DetectionPoint>();
        public static int DetectionAutoExportSessionLimit { get; set; } = 100;
        public static string DetectionAutoExportDirectory { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HR_Codex_v1.0", "DetectionExports");

        public static byte[] LastDataBuffer { get; set; }
        public static int LastDataLength { get; set; }
        public static string LastDataTag { get; set; } = "";
        public static bool IsCyclicAcquisitionActive { get; private set; }

        public static event EventHandler<AcquiredDataBatch> DataBatchReady;
        public static event EventHandler<string> DetectionStatusChanged;
        public static event EventHandler<bool> CyclicAcquisitionStateChanged;

        static AppServices()
        {
            SignalProcessing.Initialize(RadarConfig);
            UdpService.DataReceived += (s, data) => RawDataSaver.Enqueue(data, data?.Length ?? 0);
            UdpService.PacketReceived += (s, packet) => Hr23Recorder.OnUdpPacket(packet);
            Hr23RecorderServer.LogMessage += (s, message) => Trace.WriteLine(message);
        }

        public static bool StartHr23RecorderServer()
        {
            return Hr23RecorderServer.Start();
        }

        public static void ShutdownHr23RecorderServer()
        {
            Hr23RecorderServer.Stop();
            Hr23Recorder.Stop();
            Hr23Recorder.Dispose();
        }

        public static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        public static DetectionAddResult AddDetectionPoints(IEnumerable<DetectionPoint> points)
        {
            var list = points?.ToList() ?? new List<DetectionPoint>();
            if (list.Count == 0)
                return new DetectionAddResult();

            var result = new DetectionAddResult { AddedCount = list.Count };
            List<DetectionPoint> exportSnapshot = null;

            RunOnUi(() =>
            {
                _detectionSession++;
                foreach (var point in list)
                {
                    point.Session = _detectionSession;
                    DetectionPoints.Add(point);
                }

                result.SessionCount = _detectionSession;
                result.TotalPointCount = DetectionPoints.Count;

                int limit = DetectionAutoExportSessionLimit;
                if (limit > 0 && _detectionSession >= limit)
                {
                    exportSnapshot = DetectionPoints
                        .Select(p => new DetectionPoint
                        {
                            Session = p.Session,
                            Range = p.Range,
                            Velocity = p.Velocity,
                            Amplitude = p.Amplitude
                        })
                        .ToList();

                    DetectionPoints.Clear();
                    _detectionSession = 0;
                    result.SessionCountAfterReset = 0;
                    result.AutoExported = true;
                }
            });

            if (exportSnapshot != null && exportSnapshot.Count > 0)
            {
                try
                {
                    result.ExportPath = ExportDetectionPointsCsv(exportSnapshot, DetectionAutoExportDirectory, "points_auto");
                    result.StatusMessage = $"检测点已累计 {result.SessionCount} 场，自动导出 {exportSnapshot.Count} 点到 {result.ExportPath}，已清空并重新计数";
                }
                catch (Exception ex)
                {
                    result.AutoExportError = ex.Message;
                    result.StatusMessage = $"检测点自动导出失败: {ex.Message}";
                }
            }
            else
            {
                int limit = DetectionAutoExportSessionLimit;
                string limitText = limit > 0 ? $"/{limit}" : "";
                result.StatusMessage = $"检测点累计 {result.SessionCount}{limitText} 场，本场新增 {result.AddedCount} 点，当前 {result.TotalPointCount} 点";
            }

            DetectionStatusChanged?.Invoke(null, result.StatusMessage);
            return result;
        }

        public static void ClearDetectionPoints()
        {
            RunOnUi(() =>
            {
                DetectionPoints.Clear();
                _detectionSession = 0;
            });
            DetectionStatusChanged?.Invoke(null, "检测点已清空，场次已重置");
        }

        public static string ExportDetectionPointsCsv(IEnumerable<DetectionPoint> points, string directory, string prefix)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var lines = new List<string> { "场次,距离,速度,幅度" };
            foreach (var p in points)
                lines.Add($"{p.Session},{p.Range:F2},{p.Velocity:F2},{p.Amplitude:F2}");

            File.WriteAllLines(path, lines, new UTF8Encoding(true));
            return path;
        }

        public static void PublishDataBatchReady(byte[] buffer, int length, string tag, bool isCyclic)
        {
            if (buffer == null || length <= 0)
                return;

            var batch = new AcquiredDataBatch(
                buffer,
                Math.Min(length, buffer.Length),
                tag ?? "",
                System.Threading.Interlocked.Increment(ref _dataBatchSequence),
                DateTime.Now,
                isCyclic);

            DataBatchReady?.Invoke(null, batch);
        }

        public static void SetCyclicAcquisitionActive(bool isActive)
        {
            if (IsCyclicAcquisitionActive == isActive)
                return;

            IsCyclicAcquisitionActive = isActive;
            CyclicAcquisitionStateChanged?.Invoke(null, isActive);
        }

        private static Hr23RecorderServer CreateHr23RecorderServer()
        {
            string hostText = ConfigurationManager.AppSettings["Hr23RecorderHost"];
            string portText = ConfigurationManager.AppSettings["Hr23RecorderPort"];
            IPAddress address;
            int port;
            if (!IPAddress.TryParse(hostText, out address))
                address = IPAddress.Loopback;
            if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
                port = 7070;
            return new Hr23RecorderServer(Hr23Recorder, address, port);
        }
    }

    public class DetectionAddResult
    {
        public int AddedCount { get; set; }
        public int SessionCount { get; set; }
        public int SessionCountAfterReset { get; set; }
        public int TotalPointCount { get; set; }
        public bool AutoExported { get; set; }
        public string ExportPath { get; set; }
        public string AutoExportError { get; set; }
        public string StatusMessage { get; set; }
    }

    public class AcquiredDataBatch
    {
        public AcquiredDataBatch(byte[] buffer, int length, string tag, int sequence, DateTime createdAt, bool isCyclic)
        {
            Buffer = buffer;
            Length = length;
            Tag = tag;
            Sequence = sequence;
            CreatedAt = createdAt;
            IsCyclic = isCyclic;
        }

        public byte[] Buffer { get; }
        public int Length { get; }
        public string Tag { get; }
        public int Sequence { get; }
        public DateTime CreatedAt { get; }
        public bool IsCyclic { get; }
    }
}
