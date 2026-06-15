using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HR_Codex_v0.Services
{
    public class UdpService
    {
        private readonly object _syncRoot = new object();
        private UdpClient _udpClient;
        private IPEndPoint _remotePoint;
        private IPEndPoint _localPoint;
        private IPEndPoint _lastReceivedPoint;
        private CancellationTokenSource _cts;
        private Task _receiveTask;

        public bool IsConnected => _udpClient != null;
        public bool IsReceiving => _receiveTask != null && !_receiveTask.IsCompleted;
        public string RemoteEndPointText => _remotePoint?.ToString() ?? "";
        public string LocalEndPointText => _localPoint?.ToString() ?? "";
        public string LastReceivedEndPointText => _lastReceivedPoint?.ToString() ?? "";

        public event EventHandler<byte[]> DataReceived;
        public event EventHandler<string> LogMessage;
        public event EventHandler<bool> ConnectionStateChanged;

        public void Connect(string localIp, int localPort, string remoteIp, int remotePort)
        {
            try
            {
                Disconnect();

                var localAddress = string.IsNullOrWhiteSpace(localIp) || localIp.Trim() == "0.0.0.0"
                    ? IPAddress.Any
                    : IPAddress.Parse(localIp.Trim());
                var localEp = new IPEndPoint(localAddress, localPort);
                var client = new UdpClient(localEp)
                {
                    EnableBroadcast = true
                };
                client.Client.ReceiveBufferSize = 64 * 1024 * 1024;
                client.Client.SendBufferSize = 4 * 1024 * 1024;
                client.Client.ReceiveTimeout = 250;

                lock (_syncRoot)
                {
                    _udpClient = client;
                    _localPoint = localEp;
                    _remotePoint = new IPEndPoint(IPAddress.Parse(remoteIp.Trim()), remotePort);
                    _lastReceivedPoint = null;
                }

                LogMessage?.Invoke(this, $"UDP 连接已建立，本机 {localEp}，发送目标 {_remotePoint}");
                ConnectionStateChanged?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"连接失败: {ex.Message}");
                throw;
            }
        }

        public void Disconnect()
        {
            StopReceiving();

            lock (_syncRoot)
            {
                _udpClient?.Close();
                _udpClient = null;
                _localPoint = null;
                _remotePoint = null;
                _lastReceivedPoint = null;
            }

            ConnectionStateChanged?.Invoke(this, false);
        }

        public async Task SendAsync(byte[] data)
        {
            UdpClient client;
            IPEndPoint remote;

            lock (_syncRoot)
            {
                client = _udpClient;
                remote = _remotePoint;
            }

            if (client == null || remote == null || data == null || data.Length == 0)
                return;

            try
            {
                await client.SendAsync(data, data.Length, remote);
            }
            catch (SocketException ex)
            {
                LogMessage?.Invoke(this, $"发送到 {remote} 失败: {ex.Message}");
                throw;
            }
        }

        public bool StartReceiving()
        {
            UdpClient client;
            CancellationToken token;

            lock (_syncRoot)
            {
                if (_udpClient == null)
                    return false;

                if (_receiveTask != null && !_receiveTask.IsCompleted)
                    return false;

                _cts = new CancellationTokenSource();
                client = _udpClient;
                token = _cts.Token;
                _receiveTask = Task.Run(() => ReceiveLoop(client, token), token);
                return true;
            }
        }

        public void StopReceiving()
        {
            lock (_syncRoot)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void ReceiveLoop(UdpClient client, CancellationToken token)
        {
            var source = new IPEndPoint(IPAddress.Any, 0);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var buffer = client.Receive(ref source);
                    if (buffer != null && buffer.Length > 0)
                    {
                        bool sourceChanged = false;
                        lock (_syncRoot)
                        {
                            if (_lastReceivedPoint == null ||
                                !_lastReceivedPoint.Address.Equals(source.Address) ||
                                _lastReceivedPoint.Port != source.Port)
                            {
                                _lastReceivedPoint = new IPEndPoint(source.Address, source.Port);
                                sourceChanged = true;
                            }
                        }

                        if (sourceChanged)
                            LogMessage?.Invoke(this, $"收到数据来源: {source}");

                        DataReceived?.Invoke(this, buffer);
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (!token.IsCancellationRequested)
                        LogMessage?.Invoke(this, $"接收错误: {ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        LogMessage?.Invoke(this, $"接收错误: {ex.Message}");
                    break;
                }
            }
        }
    }
}
