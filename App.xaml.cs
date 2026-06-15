using System.Windows;

namespace HR_Codex_v0
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (!Services.AppServices.StartHr23RecorderServer())
            {
                var error = Services.AppServices.Hr23RecorderServer.LastError;
                MessageBox.Show(
                    "HR2.3 Recorder Server 启动失败。\n\n" +
                    "debug_monitor 将无法通过 TCP 控制本上位机同步保存雷达数据。\n\n" +
                    "请检查 127.0.0.1:7070 是否被其他程序占用，或在 App.config 中修改 Hr23RecorderPort。\n\n" +
                    "错误信息: " + (string.IsNullOrWhiteSpace(error) ? "未知错误" : error),
                    "HR2.3 Recorder Server",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Services.AppServices.ShutdownHr23RecorderServer();
            base.OnExit(e);
        }
    }
}
