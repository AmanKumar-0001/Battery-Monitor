namespace BatteryMonitor.Fallbacks;

using BatteryMonitor.Core.Models;
using BatteryMonitor.Drivers;
using BatteryMonitor.Interop;

public sealed class GenericHidBatteryDriver : IBatteryDriver
{
    private readonly IHidTransportFactory _transportFactory;

    public string DriverId => "HID.Generic";
    public int Priority => 100; // Fallback driver

    public GenericHidBatteryDriver(IHidTransportFactory transportFactory)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    public bool Matches(DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        return deviceInfo.Transport == TransportType.UsbHid;
    }

    public async Task<BatteryReading?> TryReadAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        if (!Matches(deviceInfo))
        {
            return null;
        }

        await using var transport = _transportFactory.Open(deviceInfo.DevicePath);

        byte[] buffer = new byte[64];

        // 1. Attempt Feature Report first (instant return, zero idle delay)
        buffer[0] = 0x00;
        try
        {
            bool featureSuccess = await transport.GetFeatureAsync(buffer, cancellationToken);
            if (featureSuccess && buffer.Length >= 2 && buffer[1] > 0 && buffer[1] <= 100)
            {
                var reading = ParseGenericHidReport(buffer);
                if (reading != null)
                {
                    return reading;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        // 2. Fallback to short-timeout Input Report read (200ms)
        using var readCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readCts.Token);
        try
        {
            int bytesRead = await transport.ReadAsync(buffer, linkedCts.Token);
            if (bytesRead >= 2)
            {
                return ParseGenericHidReport(buffer.AsSpan(0, bytesRead));
            }
        }
        catch (OperationCanceledException) when (readCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Device was idle; return gracefully
            return null;
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static BatteryReading? ParseGenericHidReport(ReadOnlySpan<byte> report)
    {
        if (report.Length < 2)
        {
            return null;
        }

        // Standard HID Battery Strength is commonly at byte 1 (if report[0] is Report ID)
        // Values typically range 0..100 or 0..255
        byte rawValue = report[1];

        int percentage;
        if (rawValue <= 100)
        {
            percentage = rawValue;
        }
        else
        {
            // Scale 0..255 to 0..100
            percentage = (int)Math.Round((rawValue / 255.0) * 100.0);
        }

        ChargingStatus status = ChargingStatus.Discharging;
        // If report has status flags at byte 2
        if (report.Length > 2)
        {
            byte flags = report[2];
            if ((flags & 0x01) != 0) // Bit 0: Charging
            {
                status = ChargingStatus.Charging;
            }
        }

        return new BatteryReading(percentage, status);
    }
}
