using System.Windows;

namespace HR_Codex_v0
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Services.AppServices.StartHr23RecorderServer();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Services.AppServices.ShutdownHr23RecorderServer();
            base.OnExit(e);
        }
    }
}

