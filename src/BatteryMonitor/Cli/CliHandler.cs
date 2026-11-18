namespace BatteryMonitor.Cli;

using System.IO;
using System.Text.Json;
using System.Threading;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Core.Scheduler;
using BatteryMonitor.Drivers;
using BatteryMonitor.Fallbacks;
using BatteryMonitor.Interop;
using BatteryMonitor.Settings;

public static class CliHandler
{
    public const string QuitEventName = @"Local\BatteryMonitor_QuitEvent";
    public const string SingleInstanceMutexName = @"Local\BatteryMonitor_SingleInstance_Mutex";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();

        switch (cmd)
        {
            case "--list":
                return await HandleListAsync();

            case "--dump":
                string dumpPath = args.Length > 1 ? args[1] : "battery_dump.json";
                return await HandleDumpAsync(dumpPath);

            case "--quit":
                return HandleQuit();

            case "--help":
            case "-h":
            case "/?":
                PrintHelp();
                return 0;

            default:
                Console.Error.WriteLine($"Unknown option: {args[0]}");
                PrintHelp();
                return 1;
        }
    }

    public static async Task<int> HandleListAsync(
        IDeviceEnumerator? customEnumerator = null,
        IEnumerable<IBatteryDriver>? customDrivers = null,
        TextWriter? output = null)
    {
        var writer = output ?? Console.Out;
        var configManager = new ConfigManager();
        var config = configManager.Load();

        IDeviceEnumerator enumerator = customEnumerator
            ?? new Win32DeviceEnumerator(configManager);

        var drivers = customDrivers ?? new IBatteryDriver[]
        {
            new Hidpp20Driver(new Win32HidTransportFactory()),
            new GenericHidBatteryDriver(new Win32HidTransportFactory()),
            new BluetoothPropertyDriver(new Win32BluetoothPropertyReader()),
            new DemoBatteryDriver()
        };

        var scheduler = new PollingScheduler(enumerator, drivers, configManager);
        await scheduler.PollAllDevicesAsync(CancellationToken.None);

        var devices = scheduler.Devices.Values.ToList();
        if (devices.Count == 0)
        {
            writer.WriteLine("No active wireless peripherals detected.");
            return 1;
        }

        writer.WriteLine("{0,-30} {1,-12} {2,-10} {3,-12} {4,-12}", "Device Name", "Type", "Battery", "State", "Status");
        writer.WriteLine(new string('-', 80));

        foreach (var dev in devices)
        {
            string pct = dev.Percentage.HasValue ? $"{dev.Percentage.Value}%" : "--";
            writer.WriteLine("{0,-30} {1,-12} {2,-10} {3,-12} {4,-12}",
                dev.DisplayName.Length > 30 ? dev.DisplayName[..27] + "..." : dev.DisplayName,
                dev.Info.Type,
                pct,
                dev.State,
                dev.ChargingStatus);
        }

        return 0;
    }

    public static async Task<int> HandleDumpAsync(string destinationPath, IDeviceEnumerator? customEnumerator = null)
    {
        try
        {
            var configManager = new ConfigManager();
            var config = configManager.Load();

            IDeviceEnumerator enumerator = customEnumerator
                ?? new Win32DeviceEnumerator(configManager);

            var devices = enumerator.EnumerateDevices();

            var dumpData = new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Environment = new
                {
                    OSVersion = Environment.OSVersion.ToString(),
                    Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                    MachineName = Environment.MachineName,
                    ProcessPath = Environment.ProcessPath
                },
                Configuration = config,
                DiscoveredDevices = devices.Select(d => new
                {
                    d.DeviceId,
                    d.DevicePath,
                    VendorId = $"0x{d.VendorId:X4}",
                    ProductId = $"0x{d.ProductId:X4}",
                    d.Name,
                    Type = d.Type.ToString(),
                    Transport = d.Transport.ToString(),
                    d.SubDeviceIndex
                })
            };

            string json = JsonSerializer.Serialize(dumpData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(destinationPath, json);

            Console.WriteLine($"Successfully dumped diagnostic data to: {Path.GetFullPath(destinationPath)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing dump file: {ex.Message}");
            return 1;
        }
    }

    public static int HandleQuit()
    {
        try
        {
            using var quitEvent = EventWaitHandle.OpenExisting(QuitEventName);
            quitEvent.Set();
            Console.WriteLine("Signaled running BatteryMonitor instance to exit.");
            return 0;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            Console.WriteLine("No running BatteryMonitor instance found.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to signal running instance: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"Battery Monitor for Windows 11 — Command Line Interface

Usage:
  BatteryMonitor.exe [options]

Options:
  --list          Enumerate all connected peripherals and current battery levels.
  --dump [path]   Write diagnostic hardware descriptors and properties to a JSON file (default: battery_dump.json).
  --quit          Signal the background running instance to flush data and terminate cleanly (no elevation needed).
  --help, -h      Display this help message.

Exit Codes:
  0   Operation completed successfully.
  1   Device not found, no running instance, or argument error.
");
    }
}

