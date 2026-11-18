namespace BatteryMonitor.Core.Models;

public sealed record DeviceInfo(
    string DeviceId,
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    string Name,
    DeviceType Type,
    TransportType Transport,
    byte SubDeviceIndex = 0xFF
);
