using HR_Codex_v0.Helpers;
using HR_Codex_v0.Services;
using HR_Codex_v0.Views;
using System.Windows.Controls;

namespace HR_Codex_v0.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly ConnectionView _connectionView = new ConnectionView();
        private readonly CommandView _commandView = new CommandView();
        private readonly MotionView _motionView = new MotionView();
        private readonly RadarConfigView _radarConfigView = new RadarConfigView();
        private readonly AcquisitionView _acquisitionView = new AcquisitionView();
        private readonly ProcessingView _processingView = new ProcessingView();
        private readonly DetectionView _detectionView = new DetectionView();
        private UserControl _currentView;
        private bool _isConnected;

        public UserControl CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                    OnPropertyChanged(nameof(ConnectionStatusText));
            }
        }

        public string ConnectionStatusText => IsConnected ? "已连接" : "未连接";

        public RelayCommand NavCommand { get; }

        public MainViewModel()
        {
            CurrentView = _connectionView;
            NavCommand = new RelayCommand(OnNavigate);
            IsConnected = AppServices.UdpService.IsConnected;
            AppServices.UdpService.ConnectionStateChanged += (s, connected) => AppServices.RunOnUi(() => IsConnected = connected);
        }

        private void OnNavigate(object parameter)
        {
            switch (parameter?.ToString())
            {
                case "Connection": CurrentView = _connectionView; break;
                case "Command": CurrentView = _commandView; break;
                case "Motion": CurrentView = _motionView; break;
                case "Config": CurrentView = _radarConfigView; break;
                case "Acquisition": CurrentView = _acquisitionView; break;
                case "Processing": CurrentView = _processingView; break;
                case "Detection": CurrentView = _detectionView; break;
            }
        }
    }
}
