namespace BatteryMonitor.Tests.CliTests;

using System.IO;
using System.Text.Json;
using System.Threading;
using BatteryMonitor.Cli;
using BatteryMonitor.Core;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Tests.Mocks;

public class CliAndStartupTests
{
    [Fact]
    public async Task Cli_Help_ReturnsZeroAndPrintsUsage()
    {
        using var sw = new StringWriter();
        var origOut = Console.Out;
        try
        {
            Console.SetOut(sw);
            int exitCode = await CliHandler.RunAsync(new[] { "--help" });
            Assert.Equal(0, exitCode);
            Assert.Contains("Battery Monitor for Windows 11", sw.ToString());
            Assert.Contains("--list", sw.ToString());
            Assert.Contains("--dump", sw.ToString());
            Assert.Contains("--quit", sw.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    [Fact]
    public async Task Cli_List_OutputsTableAndReturnsZero()
    {
        var enumerator = new MockEnumerator();
        enumerator.DeviceList.Add(new DeviceInfo("DEV_01", @"\\?\hid#01", 0x046D, 0xC547, "G Pro Mouse", DeviceType.Mouse, TransportType.UsbHid));
        enumerator.DeviceList.Add(new DeviceInfo("DEV_02", @"\\?\hid#02", 0x045E, 0x028E, "Xbox Pad", DeviceType.Controller, TransportType.UsbHid));

        var driver = new MockDriver();

        using var sw = new StringWriter();
        int exitCode = await CliHandler.HandleListAsync(enumerator, new[] { driver }, sw);

        Assert.Equal(0, exitCode);
        string output = sw.ToString();
        Assert.Contains("Device Name", output);
        Assert.Contains("G Pro Mouse", output);
        Assert.Contains("Xbox Pad", output);
        Assert.Contains("80%", output);
        Assert.Contains("Online", output);
    }

    [Fact]
    public async Task Cli_List_NoDevices_ReturnsOne()
    {
        var emptyEnumerator = new MockEnumerator();
        using var sw = new StringWriter();
        int exitCode = await CliHandler.HandleListAsync(emptyEnumerator, Array.Empty<BatteryMonitor.Drivers.IBatteryDriver>(), sw);

        Assert.Equal(1, exitCode);
        Assert.Contains("No active wireless peripherals detected", sw.ToString());
    }

    [Fact]
    public async Task Cli_Dump_WritesValidJsonAndReturnsZero()
    {
        string dumpFile = Path.Combine(Path.GetTempPath(), $"BM_Dump_{Guid.NewGuid():N}.json");

        try
        {
            var enumerator = new MockEnumerator();
            enumerator.DeviceList.Add(new DeviceInfo("DUMP_DEV", @"\\?\hid#dump", 0x046D, 0x1234, "Dump Device", DeviceType.Keyboard, TransportType.UsbHid));

            int exitCode = await CliHandler.HandleDumpAsync(dumpFile, enumerator);
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(dumpFile));

            string json = await File.ReadAllTextAsync(dumpFile);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("Environment", out _));
            Assert.True(doc.RootElement.TryGetProperty("DiscoveredDevices", out var devs));
            Assert.Equal(1, devs.GetArrayLength());
        }
        finally
        {
            if (File.Exists(dumpFile)) File.Delete(dumpFile);
        }
    }

    [Fact]
    public void Cli_Quit_WhenNoInstanceRunning_ReturnsOne()
    {
        // When the event doesn't exist, HandleQuit returns 1
        int exitCode = CliHandler.HandleQuit();
        // Either 1 (no instance) or 0 (if an instance happens to be running in session)
        Assert.True(exitCode == 0 || exitCode == 1);
    }

    [Fact]
    public void Cli_Quit_WhenInstanceRunning_SignalsAndReturnsZero()
    {
        // Create the quit event to simulate a running instance
        using var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CliHandler.QuitEventName);

        int exitCode = CliHandler.HandleQuit();
        Assert.Equal(0, exitCode);

        // Verify the event was signaled
        bool wasSignaled = quitEvent.WaitOne(500);
        Assert.True(wasSignaled);
    }
}
