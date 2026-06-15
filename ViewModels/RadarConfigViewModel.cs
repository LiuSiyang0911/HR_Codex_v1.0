using HR_Codex_v0.Helpers;
using HR_Codex_v0.Services;
using System;

namespace HR_Codex_v0.ViewModels
{
    public class RadarConfigViewModel : ObservableObject
    {
        private string _statusMessage = "已按默认参数初始化";

        public int Nr { get => AppServices.RadarConfig.Nr; set { AppServices.RadarConfig.Nr = value; Initialize("参数已更新"); } }
        public int Ncut { get => AppServices.RadarConfig.Ncut; set { AppServices.RadarConfig.Ncut = value; Initialize("参数已更新"); } }
        public int Np { get => AppServices.RadarConfig.Np; set { AppServices.RadarConfig.Np = value; Initialize("参数已更新"); } }
        public double Pri { get => AppServices.RadarConfig.Pri; set { AppServices.RadarConfig.Pri = value; Initialize("参数已更新"); } }
        public double BandwidthMHz { get => AppServices.RadarConfig.Bandwidth / 1e6; set { AppServices.RadarConfig.Bandwidth = value * 1e6; Initialize("参数已更新"); } }
        public int Vptz { get => AppServices.RadarConfig.Vptz; set { AppServices.RadarConfig.Vptz = value; Initialize("参数已更新"); } }
        public int Offset { get => AppServices.RadarConfig.Offset; set { AppServices.RadarConfig.Offset = value; Initialize("参数已更新"); } }
        public int FftMean { get => AppServices.RadarConfig.FftMean; set { AppServices.RadarConfig.FftMean = value; Initialize("参数已更新"); } }
        public double Xlim { get => AppServices.RadarConfig.Xlim; set { AppServices.RadarConfig.Xlim = value; Initialize("参数已更新"); } }
        public double Rr { get => AppServices.RadarConfig.Rr; set { AppServices.RadarConfig.Rr = value; Initialize("参数已更新"); } }
        public double Threshold { get => AppServices.RadarConfig.Threshold; set { AppServices.RadarConfig.Threshold = value; Initialize("参数已更新"); } }
        public int PointsMax { get => AppServices.RadarConfig.PointsMax; set { AppServices.RadarConfig.PointsMax = value; Initialize("参数已更新"); } }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public RadarConfigViewModel()
        {
            Initialize("已按默认参数初始化");
        }

        private void Initialize(string message)
        {
            try
            {
                AppServices.SignalProcessing.Initialize(AppServices.RadarConfig);
                StatusMessage = message;
            }
            catch (Exception ex)
            {
                StatusMessage = $"初始化失败: {ex.Message}";
            }
        }
    }
}
