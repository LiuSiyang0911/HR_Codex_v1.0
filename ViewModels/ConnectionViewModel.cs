using HR_Codex_v0.Helpers;
using HR_Codex_v0.Services;
using System;
using System.Text;
using System.Windows.Input;

namespace HR_Codex_v0.ViewModels
{
    public class ConnectionViewModel : ObservableObject
    {
        private string _localIp = "192.168.0.110";
        private string _remoteIp = "192.168.0.255";
        private string _localPort = "20202";
        private string _remotePort = "23480";
        private string _logText = "";
        private bool _isConnected;

        public string LocalIp { get => _localIp; set => SetProperty(ref _localIp, value); }
        public string RemoteIp { get => _remoteIp; set => SetProperty(ref _remoteIp, value); }
        public string LocalPort { get => _localPort; set => SetProperty(ref _localPort, value); }
        public string RemotePort { get => _remotePort; set => SetProperty(ref _remotePort, value); }
        public string LogText { get => _logText; set => SetProperty(ref _logText, value); }
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ExchangePortCommand { get; }
        public ICommand LocalTestCommand { get; }
        public ICommand ClearLogCommand { get; }

        public ConnectionViewModel()
        {
            ConnectCommand = new RelayCommand(_ => Connect(), _ => !IsConnected);
            DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
            ExchangePortCommand = new RelayCommand(_ => ExchangePorts());
            LocalTestCommand = new RelayCommand(_ => UseLocalTestEndpoint(), _ => !IsConnected);
            ClearLogCommand = new RelayCommand(_ => LogText = "");
            IsConnected = AppServices.UdpService.IsConnected;

            AppServices.UdpService.LogMessage += (s, msg) => AppendLog(msg);
            AppServices.UdpService.ConnectionStateChanged += (s, connected) => AppServices.RunOnUi(() => IsConnected = connected);
        }

        private void Connect()
        {
            try
            {
                AppServices.UdpService.Connect(LocalIp, int.Parse(LocalPort), RemoteIp, int.Parse(RemotePort));
                AppendLog($"连接至 {RemoteIp}:{RemotePort}");
            }
            catch (Exception ex)
            {
                AppendLog($"连接失败: {ex.Message}");
            }
        }

        private void Disconnect()
        {
            AppServices.UdpService.Disconnect();
            AppendLog("UDP 已断开");
        }

        private void ExchangePorts()
        {
            var temp = LocalPort;
            LocalPort = RemotePort;
            RemotePort = temp;
        }

        private void UseLocalTestEndpoint()
        {
            LocalIp = "127.0.0.1";
            LocalPort = "20202";
            RemoteIp = "127.0.0.1";
            RemotePort = "23480";
            AppendLog("已切换到本机测试地址: 127.0.0.1:20202 -> 127.0.0.1:23480");
        }

        private void AppendLog(string msg)
        {
            AppServices.RunOnUi(() =>
            {
                var sb = new StringBuilder(LogText);
                sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
                LogText = sb.ToString();
            });
        }
    }
}
