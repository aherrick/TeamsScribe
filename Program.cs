using Velopack;

namespace TeamsScribe;

static class Program
{
    [STAThread]
    static void Main()
    {
        VelopackApp.Build().Run();
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
