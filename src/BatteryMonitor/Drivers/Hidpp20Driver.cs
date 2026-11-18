namespace BatteryMonitor.Drivers;

using System.Security;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Drivers.Allowlist;
using BatteryMonitor.Interop;

public sealed class Hidpp20Driver : IBatteryDriver
{
    public const ushort LogitechVendorId = 0x046D;

    private readonly IHidTransportFactory _transportFactory;

    public string DriverId => "Logitech.HID++2.0";
    public int Priority => 10;

    public Hidpp20Driver(IHidTransportFactory transportFactory)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    public bool Matches(DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        return deviceInfo.VendorId == LogitechVendorId && deviceInfo.Transport == TransportType.UsbHid;
    }

    public async Task<BatteryReading?> TryReadAsync(DeviceInfo deviceInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        if (!Matches(deviceInfo))
        {
            return null;
        }

        byte deviceIndex = deviceInfo.SubDeviceIndex != 0xFF ? deviceInfo.SubDeviceIndex : (byte)0x01;

        await using var transport = _transportFactory.Open(deviceInfo.DevicePath);

        // 1. Discover Feature Index for 0x1000 (Unified Battery) or 0x1004 (Unified Battery V2)
        byte featureIndex = await DiscoverFeatureIndexAsync(transport, deviceIndex, HidppAllowlist.FeatureUnifiedBattery, cancellationToken);
        if (featureIndex == 0x00)
        {
            featureIndex = await DiscoverFeatureIndexAsync(transport, deviceIndex, HidppAllowlist.FeatureUnifiedBatteryV2, cancellationToken);
        }

        if (featureIndex == 0x00)
        {
            // Device does not support unified battery feature
            return null;
        }

        // 2. Query Battery Status using discovered featureIndex
        byte[] statusRequest = HidppAllowlist.CreateBatteryStatusRequest(deviceIndex, featureIndex);
        HidppAllowlist.ValidateOutgoingRequest(statusRequest);

        await transport.WriteAsync(statusRequest, cancellationToken);

        byte[] response = new byte[20];
        int bytesRead = await transport.ReadAsync(response, cancellationToken);

        return ParseBatteryStatusResponse(response.AsSpan(0, bytesRead), featureIndex);
    }

    private static async Task<byte> DiscoverFeatureIndexAsync(
        IHidTransport transport,
        byte deviceIndex,
        ushort targetFeatureId,
        CancellationToken cancellationToken)
    {
        byte[] discoveryRequest = HidppAllowlist.CreateFeatureDiscoveryRequest(deviceIndex, targetFeatureId);
        HidppAllowlist.ValidateOutgoingRequest(discoveryRequest);

        await transport.WriteAsync(discoveryRequest, cancellationToken);

        byte[] response = new byte[20];
        int bytesRead = await transport.ReadAsync(response, cancellationToken);

        if (bytesRead < 7)
        {
            return 0x00;
        }

        // Check if device returned an HID++ error (Report ID 0x10/0x11, byte 2 == 0x8F)
        if (response[2] == 0x8F)
        {
            return 0x00;
        }

        // Validate response corresponds to feature discovery (Root feature 0x00, func 0)
        if (response[2] == 0x00 && ((response[3] >> 4) & 0x0F) == HidppAllowlist.FunctionGetFeature)
        {
            return response[4]; // Assigned feature index
        }

        return 0x00;
    }

    public static BatteryReading? ParseBatteryStatusResponse(ReadOnlySpan<byte> response, byte expectedFeatureIndex)
    {
        if (response.Length < 7)
        {
            throw new ArgumentException("Response frame is truncated (expected at least 7 bytes).", nameof(response));
        }

        byte reportId = response[0];
        if (reportId != HidppAllowlist.ReportIdLong && reportId != HidppAllowlist.ReportIdShort)
        {
            return null;
        }

        // Check error response (0x8F)
        if (response[2] == 0x8F)
        {
            return null;
        }

        // Verify feature index matches
        if (response[2] != expectedFeatureIndex)
        {
            return null;
        }

        // Verify function ID is GetBatteryStatus (0x1)
        byte funcId = (byte)((response[3] >> 4) & 0x0F);
        if (funcId != HidppAllowlist.FunctionGetBatteryStatus)
        {
            return null;
        }

        int percentage = Math.Clamp((int)response[4], 0, 100);
        byte statusByte = response.Length >= 7 ? response[6] : (byte)0x00;

        ChargingStatus status = statusByte switch
        {
            0x00 => ChargingStatus.Discharging,
            0x01 => ChargingStatus.Charging,
            0x02 => ChargingStatus.Full,
            0x03 => ChargingStatus.SlowCharge,
            _ => ChargingStatus.Invalid
        };

        // If status is Invalid and percentage is 0, device may be asleep or link dropped
        bool isAsleep = (status == ChargingStatus.Invalid && percentage == 0) || (response.Length >= 8 && (response[7] & 0x80) != 0);

        return new BatteryReading(percentage, status, isAsleep);
    }
}
