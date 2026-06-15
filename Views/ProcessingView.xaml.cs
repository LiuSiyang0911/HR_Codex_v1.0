using HR_Codex_v0.ViewModels;
using ScottPlot;
using System;
using System.Drawing;
using System.Linq;
using HR_Codex_v0.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HR_Codex_v0.Views
{
    public partial class ProcessingView : UserControl
    {
        private ProcessingViewModel _vm;
        private bool _isInitialized;

        public ProcessingView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            _vm = DataContext as ProcessingViewModel;
            if (_vm != null)
                _vm.ProcessingCompleted += OnProcessingCompleted;
            AppServices.CyclicAcquisitionStateChanged += OnCyclicAcquisitionStateChanged;

            SetupPlot(PlotProfile, "Range (m)", "Amplitude (dB)");
            SetupPlot(PlotRdm, "Velocity (m/s)", "Range (m)");
            SetupPlot(PlotDisp, "Sample Points", "Displacement (mm)");
            SetupPlot(PlotPoints, "Velocity (m/s)", "Range (m)");

            // 设置初始视图范围
            var (velMax, rangeMax, xlim) = GetPhysicalLimits();
            LockPlotBounds(PlotRdm, -xlim, xlim, 0, rangeMax);
            LockPlotBounds(PlotPoints, -xlim, xlim, 0, rangeMax);
            ApplyPlotInteractionState(AppServices.IsCyclicAcquisitionActive);
            RefreshAllPlots();
            Dispatcher.BeginInvoke(new Action(RefreshAllPlots), DispatcherPriority.ApplicationIdle);
        }

        private void OnCyclicAcquisitionStateChanged(object sender, bool isActive)
        {
            Dispatcher.Invoke(() => ApplyPlotInteractionState(isActive));
        }

        private (double velMax, double rangeMax, double xlim) GetPhysicalLimits()
        {
            var cfg = AppServices.RadarConfig;
            double c = 3e8;
            double velMax = c / (4.0 * cfg.Fc * cfg.Pri);
            double rangeMax = (cfg.Ncut - 1) * c / (2.0 * cfg.Bandwidth);
            double xlim = Math.Min(cfg.Xlim, velMax);
            return (velMax, rangeMax, xlim);
        }

        private void SetupPlot(WpfPlot plot, string xLabel, string yLabel)
        {
            var figure = Color.FromArgb(19, 29, 42);
            var data = Color.FromArgb(11, 17, 32);
            var grid = Color.FromArgb(30, 42, 58);
            var axis = Color.FromArgb(143, 149, 158);
            var label = Color.FromArgb(201, 205, 212);

            plot.Plot.Style(figureBackground: figure, dataBackground: data);
            plot.Plot.XAxis.Color(axis);
            plot.Plot.YAxis.Color(axis);
            plot.Plot.XAxis.TickMarkColor(axis);
            plot.Plot.YAxis.TickMarkColor(axis);
            plot.Plot.XAxis.TickLabelStyle(color: label, fontSize: 12);
            plot.Plot.YAxis.TickLabelStyle(color: label, fontSize: 12);
            plot.Plot.XAxis.Label(xLabel, color: label, size: 12);
            plot.Plot.YAxis.Label(yLabel, color: label, size: 12);
            plot.Plot.Grid(color: grid);

            plot.Configuration.ScrollWheelZoom = true;
            plot.Configuration.LeftClickDragPan = true;
            plot.Configuration.MiddleClickDragZoom = true;
            plot.Configuration.DoubleClickBenchmark = false;
            plot.Configuration.EnablePlotObjectEditor = false; // 禁用双击弹窗
            plot.Refresh();
        }

        private void RefreshAllPlots()
        {
            PlotProfile.Refresh();
            PlotRdm.Refresh();
            PlotDisp.Refresh();
            PlotPoints.Refresh();
        }

        private void ApplyPlotInteractionState(bool isCyclicActive)
        {
            bool allowWheelZoom = !isCyclicActive;
            PlotProfile.Configuration.ScrollWheelZoom = allowWheelZoom;
            PlotRdm.Configuration.ScrollWheelZoom = allowWheelZoom;
            PlotDisp.Configuration.ScrollWheelZoom = allowWheelZoom;
            PlotPoints.Configuration.ScrollWheelZoom = allowWheelZoom;
        }

        private void StyleColorbar(ScottPlot.Plottable.Colorbar colorbar)
        {
            var tick = Color.FromArgb(201, 205, 212);
            var mark = Color.FromArgb(143, 149, 158);
            colorbar.TickMarkColor = mark;
            colorbar.TickMarkLength = 4;
            colorbar.TickMarkWidth = 1;
            colorbar.TickLabelFont.Color = tick;
            colorbar.TickLabelFont.Size = 12;
            colorbar.LabelFont.Color = tick;
            colorbar.LabelFont.Size = 12;
            colorbar.Width = 16;
        }

        private void LockPlotBounds(WpfPlot plot, double xMin, double xMax, double yMin, double yMax)
        {
            // 设置初始视图范围（边界在 UpdateRdmPlot 中每次重新设置，确保参数变更后生效）
            plot.Plot.SetAxisLimits(xMin, xMax, yMin, yMax);
        }

        private void OnProcessingCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                switch (_vm.SelectedMode)
                {
                    case "FFT":
                        UpdateProfilePlot(_vm.FftProfile);
                        break;
                    case "PPI":
                        UpdatePpiPlot(_vm.PpiMap);
                        break;
                    case "RDM":
                    case "RDM_ref":
                    case "RDM_in":
                        UpdateRdmPlot(_vm.RdmMap);
                        UpdateProfilePlot(_vm.FftProfile); // RDM 模式下也显示距离剖面
                        break;
                    case "TD":
                        UpdateProfilePlot(_vm.TdProfile);
                        break;
                }

                if (_vm.Displacement != null)
                    UpdateDispPlot(_vm.Displacement);

                UpdatePointsPlot(_vm.DetectionPoints);
            });
        }

        private void UpdateProfilePlot(double[] data)
        {
            PlotProfile.Plot.Clear();
            if (data == null || data.Length == 0)
            {
                PlotProfile.Refresh();
                return;
            }
            // X 轴按实际距离标定：sampleRate = 1/dr (每米一个采样点)
            var cfg = AppServices.RadarConfig;
            double dr = 3e8 / (2.0 * cfg.Bandwidth);
            double rangeMax = (data.Length - 1) * dr;
            PlotProfile.Plot.SetAxisLimits(xMin: 0, xMax: rangeMax, yMin: data.Min() - 5, yMax: data.Max() + 5);
            var sig = PlotProfile.Plot.AddSignal(data, sampleRate: 1.0 / dr);
            sig.Color = Color.FromArgb(255, 215, 0);
            sig.LineWidth = 1.5f;
            PlotProfile.Refresh();
        }

        private void UpdateRdmPlot(double[,] data)
        {
            PlotRdm.Plot.Clear();
            if (data == null) return;

            var cfg = AppServices.RadarConfig;
            double c = 3e8;
            // 每次更新都重新计算物理范围，确保参数变更后生效
            double velMax = c / (4.0 * cfg.Fc * cfg.Pri);
            double rangeMax = (cfg.Ncut - 1) * c / (2.0 * cfg.Bandwidth);
            double xlim = Math.Min(cfg.Xlim, velMax);
            double dr = c / (2.0 * cfg.Bandwidth);
            int ncut = data.GetLength(0);
            int np = data.GetLength(1);

            var hm = PlotRdm.Plot.AddHeatmap(data, ScottPlot.Drawing.Colormap.Jet, lockScales: false);
            hm.OffsetX = -velMax;
            hm.OffsetY = 0;
            hm.CellWidth = (2.0 * velMax) / np;
            hm.CellHeight = dr;
            hm.FlipVertically = true;
            hm.Smooth = true;

            var cb = PlotRdm.Plot.AddColorbar(hm, space: 72);
            StyleColorbar(cb);

            ApplyRangeVelocityLimits(PlotRdm, velMax, xlim, rangeMax);
            PlotRdm.Refresh();
        }

        private void UpdatePpiPlot(double[,] data)
        {
            PlotRdm.Plot.Clear();
            if (data == null) return;
            var hm = PlotRdm.Plot.AddHeatmap(data, ScottPlot.Drawing.Colormap.Jet, lockScales: false);
            hm.FlipVertically = true;
            hm.Smooth = true;
            var cb = PlotRdm.Plot.AddColorbar(hm, space: 72);
            StyleColorbar(cb);
            var (velMax, rangeMax, xlim) = GetPhysicalLimits();
            ApplyRangeVelocityLimits(PlotRdm, velMax, xlim, rangeMax);
            PlotRdm.Refresh();
        }

        private void UpdateDispPlot(double[] data)
        {
            PlotDisp.Plot.Clear();
            if (data == null || data.Length == 0)
            {
                PlotDisp.Refresh();
                return;
            }
            PlotDisp.Plot.SetAxisLimits(xMin: 0, xMax: data.Length, yMin: data.Min() - 1, yMax: data.Max() + 1);
            var sig = PlotDisp.Plot.AddSignal(data, sampleRate: 1.0);
            sig.Color = Color.FromArgb(74, 222, 128);
            PlotDisp.Refresh();
        }

        private void UpdatePointsPlot(Models.DetectionPoint[] points)
        {
            PlotPoints.Plot.Clear();

            // 每次更新都重新计算物理范围，确保参数变更后生效
            var (velMax, rangeMax, xlim) = GetPhysicalLimits();
            ApplyRangeVelocityLimits(PlotPoints, velMax, xlim, rangeMax);

            if (points == null || points.Length == 0)
            {
                PlotPoints.Refresh();
                return;
            }
            double[] xs = points.Select(p => p.Velocity).ToArray();
            double[] ys = points.Select(p => p.Range).ToArray();
            var scatter = PlotPoints.Plot.AddScatterPoints(xs, ys);
            scatter.Color = Color.FromArgb(255, 215, 0);
            scatter.MarkerSize = 8;
            PlotPoints.Refresh();
        }

        private void ApplyRangeVelocityLimits(WpfPlot plot, double velMax, double xlim, double rangeMax)
        {
            plot.Plot.AxisScaleLock(false);
            plot.Plot.XAxis.SetBoundary(-velMax, velMax);
            plot.Plot.YAxis.SetBoundary(0, rangeMax);
            plot.Plot.SetAxisLimitsX(-xlim, xlim);
            plot.Plot.SetAxisLimitsY(0, rangeMax);
        }
    }
}

