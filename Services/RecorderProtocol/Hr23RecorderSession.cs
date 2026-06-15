using HR_Codex_v0.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HR_Codex_v0.Services.RecorderProtocol
{
    public sealed class Hr23RecorderSession : IDisposable
    {
        private const string PacketHeader = "packet_index,recv_time_epoch_s,session_elapsed_s,src_ip,src_port,length,raw_offset,total_bytes_after_write,starts_with_frame_header";
        private const string EventHeader = "event_time_epoch_s,session_elapsed_s,event,message";

        private readonly object _syncRoot = new object();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _errors = new List<string>();

        private Hr23RecorderState _state = Hr23RecorderState.Idle;
        private string _sessionId = "";
        private string _outputDir = "";
        private Dictionary<string, object> _timeBase = new Dictionary<string, object>();
        private Dictionary<string, object> _metadata = new Dictionary<string, object>();
        private string _alignmentMode = "local_recorder_start_epoch";
        private double? _alignmentEpochS;
        private double? _recorderStartEpochS;
        private double? _recordingStopEpochS;
        private DateTimeOffset? _firstPacketUtc;
        private DateTimeOffset? _lastPacketUtc;
        private DateTimeOffset? _rawFileClosedUtc;
        private string _lastSourceIp = "";
        private int _lastSourcePort;
        private long _packetCount;
        private long _totalBytes;
        private bool _acceptPackets;
        private bool _disposed;

        private BlockingCollection<Hr23RecorderPacket> _queue;
        private Task _writerTask;
        private FileStream _rawStream;
        private StreamWriter _packetWriter;
        private StreamWriter _eventWriter;

        public Hr23RecorderResponse GetStatus()
        {
            lock (_syncRoot)
                return BuildResponseUnsafe(true);
        }

        public Hr23RecorderResponse Prepare(Hr23RecorderPrepareRequest request)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                if (_state == Hr23RecorderState.Recording)
                    return FailureUnsafe("busy", "Recorder is currently recording.");
                if (_state == Hr23RecorderState.Prepared)
                    return FailureUnsafe("already_prepared", "Recorder already has a prepared session.");
                if (_state == Hr23RecorderState.Error)
                    return FailureUnsafe("error_state", "Recorder must be restarted after an I/O error.");
                if (request == null || string.IsNullOrWhiteSpace(request.OutputDir))
                    return FailureUnsafe("invalid_request", "prepare requires outputDir.");

                try
                {
                    CloseFilesUnsafe();
                    _queue?.Dispose();
                    _queue = null;
                    _writerTask = null;
                    Directory.CreateDirectory(request.OutputDir);

                    _sessionId = request.SessionId ?? "";
                    _outputDir = Path.GetFullPath(request.OutputDir);
                    _timeBase = CloneDictionary(request.TimeBase);
                    _metadata = CloneDictionary(request.Metadata);
                    _warnings.Clear();
                    _errors.Clear();
                    _packetCount = 0;
                    _totalBytes = 0;
                    _firstPacketUtc = null;
                    _lastPacketUtc = null;
                    _rawFileClosedUtc = null;
                    _lastSourceIp = "";
                    _lastSourcePort = 0;
                    _recorderStartEpochS = null;
                    _recordingStopEpochS = null;
                    _acceptPackets = false;

                    double externalStart;
                    if (TryGetDouble(_timeBase, "recordingStartEpochS", out externalStart))
                    {
                        _alignmentMode = "debug_monitor_recording_start_epoch";
                        _alignmentEpochS = externalStart;
                    }
                    else
                    {
                        _alignmentMode = "local_recorder_start_epoch";
                        _alignmentEpochS = null;
                    }

                    _rawStream = new FileStream(Path.Combine(_outputDir, "raw.dat"), FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                    _packetWriter = CreateWriter(Path.Combine(_outputDir, "packets.csv"));
                    _eventWriter = CreateWriter(Path.Combine(_outputDir, "events.csv"));
                    _packetWriter.WriteLine(PacketHeader);
                    _eventWriter.WriteLine(EventHeader);
                    _packetWriter.Flush();
                    _eventWriter.Flush();

                    _state = Hr23RecorderState.Prepared;
                    WriteEventUnsafe(EpochNow(), "prepared", "Recorder session prepared.");
                    WriteMetadataUnsafe();
                    _queue = new BlockingCollection<Hr23RecorderPacket>(new ConcurrentQueue<Hr23RecorderPacket>());
                    _writerTask = Task.Run(() => WriteLoop(_queue));
                    return BuildResponseUnsafe(true);
                }
                catch (Exception ex)
                {
                    _state = Hr23RecorderState.Error;
                    _errors.Add(ex.Message);
                    CloseFilesUnsafe();
                    return FailureUnsafe("io_error", ex.Message);
                }
            }
        }

        public Hr23RecorderResponse Start()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                if (_state != Hr23RecorderState.Prepared)
                    return FailureUnsafe("not_prepared", "Recorder must be prepared before start.");

                DateTimeOffset now = DateTimeOffset.UtcNow;
                _recorderStartEpochS = now.ToUnixTimeMilliseconds() / 1000.0;
                if (!_alignmentEpochS.HasValue)
                    _alignmentEpochS = _recorderStartEpochS;
                _state = Hr23RecorderState.Recording;
                _acceptPackets = true;
                WriteEventUnsafe(_recorderStartEpochS.Value, "started", "Recorder started.");
                WriteMetadataUnsafe();

                Hr23RecorderResponse response = BuildResponseUnsafe(true);
                response.StartEpochS = _recorderStartEpochS;
                response.StartUtc = now;
                return response;
            }
        }

        public void OnUdpPacket(UdpPacketReceivedEventArgs packet)
        {
            if (packet == null || packet.Length <= 0)
                return;

            lock (_syncRoot)
            {
                if (_state != Hr23RecorderState.Recording || !_acceptPackets || _queue == null || _queue.IsAddingCompleted)
                    return;

                byte[] copy = new byte[packet.Length];
                Buffer.BlockCopy(packet.Data, 0, copy, 0, packet.Length);
                try
                {
                    _queue.Add(new Hr23RecorderPacket
                    {
                        Data = copy,
                        SourceIp = packet.SourceEndPoint?.Address?.ToString() ?? "",
                        SourcePort = packet.SourceEndPoint?.Port ?? 0,
                        ReceiveUtc = packet.ReceiveUtc,
                        ReceiveEpochS = packet.ReceiveEpochS
                    });
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        public Hr23RecorderResponse Stop()
        {
            Task writerTask = null;
            bool shouldCloseFiles = false;
            bool wasRecording = false;

            lock (_syncRoot)
            {
                ThrowIfDisposed();
                if (_state == Hr23RecorderState.Stopped)
                    return BuildResponseUnsafe(true);

                wasRecording = _state == Hr23RecorderState.Recording;
                if (!wasRecording)
                {
                    string warning = "stop requested while recorder was not recording";
                    _warnings.Add(warning);
                    WriteEventUnsafe(EpochNow(), "warning", warning);
                }

                _acceptPackets = false;
                if (_queue != null && !_queue.IsAddingCompleted)
                    _queue.CompleteAdding();
                writerTask = _writerTask;
                shouldCloseFiles = _rawStream != null || _packetWriter != null || _eventWriter != null;
            }

            if (writerTask != null)
            {
                try
                {
                    writerTask.Wait();
                }
                catch (AggregateException ex)
                {
                    lock (_syncRoot)
                        _errors.Add(ex.GetBaseException().Message);
                }
            }

            lock (_syncRoot)
            {
                double now = EpochNow();
                _recordingStopEpochS = now;
                _state = Hr23RecorderState.Stopped;
                if (shouldCloseFiles)
                {
                    WriteEventUnsafe(
                        now,
                        "stopped",
                        wasRecording ? "Recorder stopped." : "Prepared recorder closed without starting.");
                    CloseRawFileUnsafe();
                    WriteEventUnsafe(EpochNow(), "closed", "Recorder files closed.");
                    WriteMetadataUnsafe();
                    CloseCsvFilesUnsafe();
                }
                return BuildResponseUnsafe(true);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;
            }

            Stop();
            lock (_syncRoot)
            {
                _disposed = true;
                _queue?.Dispose();
                _queue = null;
                _writerTask = null;
                CloseFilesUnsafe();
            }
        }

        private void WriteLoop(BlockingCollection<Hr23RecorderPacket> queue)
        {
            try
            {
                foreach (Hr23RecorderPacket packet in queue.GetConsumingEnumerable())
                    WritePacket(packet);
            }
            catch (Exception ex)
            {
                lock (_syncRoot)
                {
                    _acceptPackets = false;
                    _state = Hr23RecorderState.Error;
                    _errors.Add(ex.Message);
                    WriteEventUnsafe(EpochNow(), "error", ex.Message);
                    TryWriteMetadataUnsafe();
                }
            }
        }

        private void WritePacket(Hr23RecorderPacket packet)
        {
            long packetIndex;
            long rawOffset;
            long totalAfter;
            bool firstPacket;

            lock (_syncRoot)
            {
                packetIndex = _packetCount;
                rawOffset = _totalBytes;
                totalAfter = _totalBytes + packet.Data.Length;
                firstPacket = _packetCount == 0;
            }

            _rawStream.Write(packet.Data, 0, packet.Data.Length);
            _packetWriter.WriteLine(string.Join(",", new[]
            {
                packetIndex.ToString(CultureInfo.InvariantCulture),
                packet.ReceiveEpochS.ToString("R", CultureInfo.InvariantCulture),
                CalculateElapsed(packet.ReceiveEpochS).ToString("R", CultureInfo.InvariantCulture),
                Csv(packet.SourceIp),
                packet.SourcePort.ToString(CultureInfo.InvariantCulture),
                packet.Data.Length.ToString(CultureInfo.InvariantCulture),
                rawOffset.ToString(CultureInfo.InvariantCulture),
                totalAfter.ToString(CultureInfo.InvariantCulture),
                StartsWithFrameHeader(packet.Data) ? "true" : "false"
            }));

            lock (_syncRoot)
            {
                _packetCount++;
                _totalBytes = totalAfter;
                _lastPacketUtc = packet.ReceiveUtc;
                _lastSourceIp = packet.SourceIp;
                _lastSourcePort = packet.SourcePort;
                if (firstPacket)
                {
                    _firstPacketUtc = packet.ReceiveUtc;
                    WriteEventUnsafe(packet.ReceiveEpochS, "first_packet", "First UDP packet recorded.");
                }
            }
        }

        private Hr23RecorderResponse BuildResponseUnsafe(bool ok)
        {
            return new Hr23RecorderResponse
            {
                Ok = ok,
                State = _state.ToProtocolValue(),
                PacketCount = _packetCount,
                TotalBytes = _totalBytes,
                FirstPacketUtc = _firstPacketUtc,
                LastPacketUtc = _lastPacketUtc,
                RawFileClosedUtc = _rawFileClosedUtc
            };
        }

        private Hr23RecorderResponse FailureUnsafe(string error, string message)
        {
            Hr23RecorderResponse response = BuildResponseUnsafe(false);
            response.Error = error;
            response.Message = message;
            return response;
        }

        private void WriteEventUnsafe(double epochS, string eventName, string message)
        {
            if (_eventWriter == null)
                return;
            _eventWriter.WriteLine(string.Join(",", new[]
            {
                epochS.ToString("R", CultureInfo.InvariantCulture),
                CalculateElapsed(epochS).ToString("R", CultureInfo.InvariantCulture),
                Csv(eventName),
                Csv(message)
            }));
            _eventWriter.Flush();
        }

        private void WriteMetadataUnsafe()
        {
            var metadata = new Dictionary<string, object>
            {
                { "sessionId", _sessionId },
                { "state", _state.ToProtocolValue() },
                { "outputDir", _outputDir },
                { "timeBase", _timeBase },
                { "metadataFromDebugMonitor", _metadata },
                { "alignmentMode", _alignmentMode },
                { "alignmentEpochS", _alignmentEpochS },
                { "packetIndexBase", 0 },
                { "files", new Dictionary<string, object>
                    {
                        { "raw", "raw.dat" },
                        { "packets", "packets.csv" },
                        { "events", "events.csv" },
                        { "metadata", "metadata.json" }
                    }
                },
                { "packetCount", _packetCount },
                { "totalBytes", _totalBytes },
                { "firstPacketUtc", ToJsonTime(_firstPacketUtc) },
                { "lastPacketUtc", ToJsonTime(_lastPacketUtc) },
                { "rawFileClosedUtc", ToJsonTime(_rawFileClosedUtc) },
                { "recordingStartEpochS", _recorderStartEpochS },
                { "recordingStopEpochS", _recordingStopEpochS },
                { "udp", new Dictionary<string, object>
                    {
                        { "lastSourceIp", _lastSourceIp },
                        { "lastSourcePort", _lastSourcePort }
                    }
                },
                { "warnings", _warnings.ToArray() },
                { "errors", _errors.ToArray() }
            };

            File.WriteAllText(Path.Combine(_outputDir, "metadata.json"), _json.Serialize(metadata), new UTF8Encoding(false));
        }

        private void TryWriteMetadataUnsafe()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_outputDir))
                    WriteMetadataUnsafe();
            }
            catch
            {
            }
        }

        private void CloseRawFileUnsafe()
        {
            if (_rawStream == null)
                return;
            try
            {
                _rawStream.Flush();
            }
            finally
            {
                _rawStream.Dispose();
                _rawStream = null;
                _rawFileClosedUtc = DateTimeOffset.UtcNow;
            }
        }

        private void CloseCsvFilesUnsafe()
        {
            _packetWriter?.Flush();
            _eventWriter?.Flush();
            _packetWriter?.Dispose();
            _eventWriter?.Dispose();
            _packetWriter = null;
            _eventWriter = null;
        }

        private void CloseFilesUnsafe()
        {
            try
            {
                CloseRawFileUnsafe();
            }
            catch
            {
            }
            try
            {
                CloseCsvFilesUnsafe();
            }
            catch
            {
            }
        }

        private double CalculateElapsed(double epochS)
        {
            return _alignmentEpochS.HasValue ? epochS - _alignmentEpochS.Value : 0.0;
        }

        private static bool StartsWithFrameHeader(byte[] data)
        {
            return data != null && data.Length >= 4 && data[0] == 0xAA && data[1] == 0xBB && data[2] == 0x55 && data[3] == 0x66;
        }

        private static StreamWriter CreateWriter(string path)
        {
            return new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string ToJsonTime(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) : null;
        }

        private static Dictionary<string, object> CloneDictionary(Dictionary<string, object> source)
        {
            return source == null
                ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(source, StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryGetDouble(Dictionary<string, object> values, string key, out double value)
        {
            value = 0;
            object raw;
            if (values == null || !values.TryGetValue(key, out raw) || raw == null)
                return false;
            try
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double EpochNow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Hr23RecorderSession));
        }
    }
}
