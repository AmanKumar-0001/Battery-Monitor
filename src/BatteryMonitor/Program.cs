namespace BatteryMonitor;

using System.Threading;
using BatteryMonitor.Cli;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 1. If command-line arguments are provided, execute headless CLI
        if (args.Length > 0)
        {
            return CliHandler.RunAsync(args).GetAwaiter().GetResult();
        }

        // 2. Normal desktop startup: Single instance check via local session mutex
        using var mutex = new Mutex(true, CliHandler.SingleInstanceMutexName, out bool isOnlyInstance);
        if (!isOnlyInstance)
        {
            // Another instance is already running in this user session
            return 0;
        }

        // 3. Launch WPF Application
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
