using HR_Codex_v0.Helpers;
using HR_Codex_v0.Models;
using HR_Codex_v0.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace HR_Codex_v0.ViewModels
{
    public class DetectionViewModel : ObservableObject
    {
        private string _statusMessage = "";

        public ObservableCollection<DetectionPoint> Points => AppServices.DetectionPoints;

        public int AutoExportSessionLimit
        {
            get => AppServices.DetectionAutoExportSessionLimit;
            set
            {
                int normalized = Math.Max(0, value);
                if (AppServices.DetectionAutoExportSessionLimit == normalized)
                    return;

                AppServices.DetectionAutoExportSessionLimit = normalized;
                OnPropertyChanged();
                StatusMessage = normalized > 0
                    ? $"自动导出阈值已设为 {normalized} 场"
                    : "自动导出已关闭";
            }
        }

        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand SaveCsvCommand { get; }
        public ICommand ClearCommand { get; }

        public DetectionViewModel()
        {
            SaveCsvCommand = new RelayCommand(_ => SaveCsv());
            ClearCommand = new RelayCommand(_ => Clear());
            AppServices.DetectionStatusChanged += (s, msg) => StatusMessage = msg;
        }

        private void SaveCsv()
        {
            if (Points.Count == 0)
            {
                StatusMessage = "没有可保存的检测点";
                return;
            }

            var dlg = new SaveFileDialog { Filter = "CSV 文件(*.csv)|*.csv", FileName = $"points_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (dlg.ShowDialog() == true)
            {
                WriteCsv(dlg.FileName, Points);
                StatusMessage = $"已保存 {dlg.FileName}";
            }
        }

        private void Clear()
        {
            AppServices.ClearDetectionPoints();
        }

        private void WriteCsv(string path, IEnumerable<DetectionPoint> points)
        {
            var lines = new List<string> { "场次,距离,速度,幅度" };
            foreach (var p in points)
                lines.Add($"{p.Session},{p.Range:F2},{p.Velocity:F2},{p.Amplitude:F2}");

            File.WriteAllLines(path, lines, new UTF8Encoding(true));
        }
    }
}
