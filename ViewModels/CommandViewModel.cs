using HR_Codex_v0.Helpers;
using HR_Codex_v0.Models;
using HR_Codex_v0.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace HR_Codex_v0.ViewModels
{
    public class CommandViewModel : ObservableObject
    {
        private string _hexCommand = "";
        private EthCommand _selectedWaveform;
        private string _statusMessage = "";

        public string HexCommand
        {
            get => _hexCommand;
            set
            {
                if (SetProperty(ref _hexCommand, value))
                    ParseHexToByteTexts(value);
            }
        }

        public string[] ByteTexts { get; } = Enumerable.Repeat("00", 14).ToArray();
        public ObservableCollection<EthCommand> Waveforms { get; }

        public EthCommand SelectedWaveform
        {
            get => _selectedWaveform;
            set
            {
                if (SetProperty(ref _selectedWaveform, value) && value != null)
                {
                    HexCommand = value.ToHexString();
                    StatusMessage = $"已选择: {value.Name}";
                }
            }
        }

        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand SendCommand { get; }
        public ICommand RfOnCommand { get; }
        public ICommand RfOffCommand { get; }

        public CommandViewModel()
        {
            Waveforms = new ObservableCollection<EthCommand>(CommandFactory.GetAllWaveformCommands());
            SendCommand = new RelayCommand(_ => Send());
            RfOnCommand = new RelayCommand(_ => SetRf(true));
            RfOffCommand = new RelayCommand(_ => SetRf(false));
            SelectedWaveform = Waveforms.FirstOrDefault(c => c.Name.Contains("100MHz"));
        }

        private void ParseHexToByteTexts(string hex)
        {
            var bytes = TryParseHex(hex, out string error, allowAnyLength: true);
            if (bytes == null)
            {
                StatusMessage = error;
                return;
            }

            for (int i = 0; i < 14; i++)
                ByteTexts[i] = i < bytes.Length ? bytes[i].ToString("X2") : "00";

            OnPropertyChanged(nameof(ByteTexts));
        }

        private async void Send()
        {
            try
            {
                var bytes = TryParseByteTexts(out string error);
                if (bytes == null)
                {
                    StatusMessage = error;
                    return;
                }

                if (!AppServices.UdpService.IsConnected)
                {
                    StatusMessage = "发送失败: UDP 未连接";
                    return;
                }

                await AppServices.UdpService.SendAsync(bytes);
                StatusMessage = $"指令已发送: {string.Join(" ", bytes.Select(b => b.ToString("X2")))}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送失败: {ex.Message}";
            }
        }

        private async void SetRf(bool on)
        {
            try
            {
                var cmd = CommandFactory.BuildRfCommand(on);
                HexCommand = cmd.ToHexString();

                if (!AppServices.UdpService.IsConnected)
                {
                    StatusMessage = $"{cmd.Name} 指令已生成，UDP 未连接";
                    return;
                }

                await AppServices.UdpService.SendAsync(cmd.Cmd);
                StatusMessage = $"已发送: {cmd.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送失败: {ex.Message}";
            }
        }

        private byte[] TryParseByteTexts(out string error)
        {
            error = null;
            var bytes = new byte[14];

            for (int i = 0; i < bytes.Length; i++)
            {
                string text = Regex.Replace(ByteTexts[i] ?? "", @"\s+", "");
                if (text.Length == 0)
                {
                    error = $"Byte {i + 1} is empty";
                    return null;
                }
                if (text.Length > 2 || !Regex.IsMatch(text, @"\A[0-9a-fA-F]+\z"))
                {
                    error = $"Byte {i + 1} is not a valid HEX byte";
                    return null;
                }

                bytes[i] = Convert.ToByte(text, 16);
                ByteTexts[i] = bytes[i].ToString("X2");
            }

            HexCommand = string.Join(" ", bytes.Select(b => b.ToString("X2")));
            OnPropertyChanged(nameof(ByteTexts));
            return bytes;
        }

        private byte[] TryParseHex(string hex, out string error, bool allowAnyLength)
        {
            error = null;
            string compact = Regex.Replace(hex ?? "", @"\s+", "");
            if (compact.Length == 0)
            {
                error = "HEX 指令为空";
                return null;
            }
            if (compact.Length % 2 != 0)
            {
                error = "HEX 指令长度必须为偶数";
                return null;
            }
            if (!Regex.IsMatch(compact, @"\A[0-9a-fA-F]+\z"))
            {
                error = "HEX 指令包含非法字符";
                return null;
            }

            int byteCount = compact.Length / 2;
            if (!allowAnyLength && byteCount != 14)
            {
                error = $"发送指令必须为 14 字节，当前为 {byteCount} 字节";
                return null;
            }

            var bytes = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
                bytes[i] = Convert.ToByte(compact.Substring(i * 2, 2), 16);

            return bytes;
        }
    }
}
