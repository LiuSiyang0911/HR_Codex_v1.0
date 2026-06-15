using HR_Codex_v0.Helpers;
using HR_Codex_v0.Models;
using HR_Codex_v0.Services;
using System;
using System.Windows.Input;

namespace HR_Codex_v0.ViewModels
{
    public class MotionViewModel : ObservableObject
    {
        private int _speed = 5;
        private string _statusMessage = "";

        public int Speed { get => _speed; set => SetProperty(ref _speed, value); }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand MoveLeftCommand { get; }
        public ICommand MoveRightCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand EnableCommand { get; }
        public ICommand ReadbackOnCommand { get; }
        public ICommand ReadbackOffCommand { get; }
        public ICommand QueryStartCommand { get; }
        public ICommand QueryStopCommand { get; }
        public ICommand QueryContentCommand { get; }

        public MotionViewModel()
        {
            MoveLeftCommand = new RelayCommand(_ => SendMotion(MotionType.Left));
            MoveRightCommand = new RelayCommand(_ => SendMotion(MotionType.Right));
            StopCommand = new RelayCommand(_ => SendMotion(MotionType.Stop));
            EnableCommand = new RelayCommand(_ => SendMotion(MotionType.Enable));
            ReadbackOnCommand = new RelayCommand(_ => SendMotion(MotionType.ReadbackOn));
            ReadbackOffCommand = new RelayCommand(_ => SendMotion(MotionType.ReadbackOff));
            QueryStartCommand = new RelayCommand(_ => SendMotion(MotionType.QueryStart));
            QueryStopCommand = new RelayCommand(_ => SendMotion(MotionType.QueryStop));
            QueryContentCommand = new RelayCommand(_ => SendMotion(MotionType.QueryContent));
        }

        private async void SendMotion(MotionType type)
        {
            try
            {
                var cmd = CommandFactory.BuildMotionCommand(type, (byte)Speed);
                if (!AppServices.UdpService.IsConnected)
                {
                    StatusMessage = $"{cmd.Name} 指令已生成，UDP 未连接";
                    return;
                }

                await AppServices.UdpService.SendAsync(cmd.Cmd);
                StatusMessage = $"已发送: {cmd.Name} ({cmd.ToHexString()})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"失败: {ex.Message}";
            }
        }
    }
}

