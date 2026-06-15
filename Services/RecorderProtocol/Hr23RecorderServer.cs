using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HR_Codex_v0.Services.RecorderProtocol
{
    public sealed class Hr23RecorderServer : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly Hr23RecorderSession _session;
        private readonly IPAddress _address;
        private readonly int _configuredPort;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptTask;

        public Hr23RecorderServer(Hr23RecorderSession session, IPAddress address, int port)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _address = address ?? IPAddress.Loopback;
            if (port < 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            _configuredPort = port;
            Port = port;
        }

        public int Port { get; private set; }
        public bool IsRunning { get; private set; }
        public string LastError { get; private set; }
        public event EventHandler<string> LogMessage;

        public bool Start()
        {
            lock (_syncRoot)
            {
                if (IsRunning)
                    return true;
                try
                {
                    _listener = new TcpListener(_address, _configuredPort);
                    _listener.Start();
                    Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                    _cts = new CancellationTokenSource();
                    IsRunning = true;
                    LastError = null;
                    _acceptTask = Task.Run(() => AcceptLoop(_listener, _cts.Token));
                    LogMessage?.Invoke(this, "HR2.3 Recorder Server listening on " + _address + ":" + Port + ".");
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    IsRunning = false;
                    _listener = null;
                    LogMessage?.Invoke(this, "HR2.3 Recorder Server failed to start on " + _address + ":" + _configuredPort + ": " + ex.Message);
                    return false;
                }
            }
        }

        public void Stop()
        {
            Task acceptTask;
            lock (_syncRoot)
            {
                if (!IsRunning)
                    return;
                IsRunning = false;
                _cts?.Cancel();
                _listener?.Stop();
                acceptTask = _acceptTask;
                _listener = null;
                _acceptTask = null;
            }

            try
            {
                acceptTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            lock (_syncRoot)
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task AcceptLoop(TcpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClient(client));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (!token.IsCancellationRequested)
                        LogMessage?.Invoke(this, "HR2.3 Recorder Server accept failed: " + ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        LogMessage?.Invoke(this, "HR2.3 Recorder Server error: " + ex.Message);
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                using (NetworkStream stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                {
                    Dictionary<string, object> response;
                    try
                    {
                        string line = reader.ReadLine();
                        response = HandleLine(line);
                    }
                    catch (Exception ex)
                    {
                        response = ToDictionary(Failure("server_error", ex.Message));
                    }
                    writer.WriteLine(_json.Serialize(response));
                }
            }
        }

        private Dictionary<string, object> HandleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return ToDictionary(Failure("invalid_request", "Request must contain one JSON line."));
            if (line.Length > 1024 * 1024)
                return ToDictionary(Failure("request_too_large", "Request line exceeds 1 MiB."));

            Dictionary<string, object> request;
            try
            {
                request = _json.Deserialize<Dictionary<string, object>>(line);
            }
            catch (Exception ex)
            {
                return ToDictionary(Failure("invalid_json", ex.Message));
            }

            object rawCommand;
            string command = request != null && request.TryGetValue("cmd", out rawCommand)
                ? Convert.ToString(rawCommand, CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant()
                : null;
            switch (command)
            {
                case "status":
                    return ToDictionary(_session.GetStatus());
                case "prepare":
                    return ToDictionary(_session.Prepare(new Hr23RecorderPrepareRequest
                    {
                        SessionId = GetString(request, "sessionId"),
                        OutputDir = GetString(request, "outputDir"),
                        TimeBase = GetDictionary(request, "timeBase"),
                        Metadata = GetDictionary(request, "metadata")
                    }));
                case "start":
                    return ToDictionary(_session.Start());
                case "stop":
                    return ToDictionary(_session.Stop());
                default:
                    return ToDictionary(Failure("unknown_command", "Unsupported command: " + (command ?? "<missing>")));
            }
        }

        private Hr23RecorderResponse Failure(string error, string message)
        {
            Hr23RecorderResponse response = _session.GetStatus();
            response.Ok = false;
            response.Error = error;
            response.Message = message;
            return response;
        }

        private static Dictionary<string, object> ToDictionary(Hr23RecorderResponse response)
        {
            var result = new Dictionary<string, object>
            {
                { "ok", response.Ok },
                { "state", response.State },
                { "packetCount", response.PacketCount },
                { "totalBytes", response.TotalBytes },
                { "firstPacketUtc", FormatTime(response.FirstPacketUtc) },
                { "lastPacketUtc", FormatTime(response.LastPacketUtc) }
            };
            if (!string.IsNullOrWhiteSpace(response.Error))
                result["error"] = response.Error;
            if (!string.IsNullOrWhiteSpace(response.Message))
                result["message"] = response.Message;
            if (response.RawFileClosedUtc.HasValue)
                result["rawFileClosedUtc"] = FormatTime(response.RawFileClosedUtc);
            if (response.StartEpochS.HasValue || response.StartUtc.HasValue)
            {
                result["time"] = new Dictionary<string, object>
                {
                    { "startEpochS", response.StartEpochS },
                    { "startUtc", FormatTime(response.StartUtc) }
                };
            }
            return result;
        }

        private static string GetString(Dictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : null;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> values, string key)
        {
            object value;
            if (values == null || !values.TryGetValue(key, out value) || value == null)
                return new Dictionary<string, object>();
            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
                return dictionary;
            var typed = value as IDictionary<string, object>;
            return typed == null ? new Dictionary<string, object>() : new Dictionary<string, object>(typed);
        }

        private static string FormatTime(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) : null;
        }
    }
}
