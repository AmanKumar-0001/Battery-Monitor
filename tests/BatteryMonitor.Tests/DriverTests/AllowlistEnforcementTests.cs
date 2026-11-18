namespace BatteryMonitor.Tests.DriverTests;

using System.Security;
using BatteryMonitor.Drivers.Allowlist;

public class AllowlistEnforcementTests
{
    [Theory]
    [InlineData(HidppAllowlist.FeatureUnifiedBattery)]
    [InlineData(HidppAllowlist.FeatureUnifiedBatteryV2)]
    [InlineData(HidppAllowlist.FeatureBatteryVoltage)]
    public void Allowlist_PermitsApprovedBatteryFeatures(ushort featureId)
    {
        byte[] request = HidppAllowlist.CreateFeatureDiscoveryRequest(deviceIndex: 1, featureId);

        Assert.NotNull(request);
        Assert.Equal(20, request.Length);
        Assert.Equal(HidppAllowlist.ReportIdLong, request[0]);
        Assert.Equal(1, request[1]);
        Assert.Equal(0x00, request[2]); // Root
        Assert.Equal(0x00, request[3]); // Func 0
        Assert.Equal((byte)((featureId >> 8) & 0xFF), request[4]);
        Assert.Equal((byte)(featureId & 0xFF), request[5]);

        // Must validate cleanly
        HidppAllowlist.ValidateOutgoingRequest(request);
    }

    [Theory]
    [InlineData((ushort)0x0001)] // Feature Set
    [InlineData((ushort)0x1800)] // Device Name
    [InlineData((ushort)0x1814)] // Change Host
    [InlineData((ushort)0x1D4B)] // Wireless Device Pairing (Write)
    [InlineData((ushort)0x0003)] // Device Reset (Write)
    [InlineData((ushort)0xFFFF)] // Non-existent
    public void Allowlist_RejectsUnapprovedFeatureDiscovery(ushort forbiddenFeatureId)
    {
        Assert.Throws<SecurityException>(() =>
        {
            HidppAllowlist.CreateFeatureDiscoveryRequest(deviceIndex: 1, forbiddenFeatureId);
        });
    }

    [Fact]
    public void Allowlist_PermitsApprovedBatteryStatusRequest()
    {
        byte[] request = HidppAllowlist.CreateBatteryStatusRequest(deviceIndex: 1, featureIndex: 0x04);

        Assert.NotNull(request);
        Assert.Equal(20, request.Length);
        Assert.Equal(HidppAllowlist.ReportIdLong, request[0]);
        Assert.Equal(1, request[1]);
        Assert.Equal(0x04, request[2]); // Discovered feature index
        Assert.Equal((byte)(HidppAllowlist.FunctionGetBatteryStatus << 4), request[3]); // Func 1

        // Must validate cleanly
        HidppAllowlist.ValidateOutgoingRequest(request);
    }

    [Theory]
    [InlineData(0x02)] // Func 2 (Write parameters)
    [InlineData(0x03)] // Func 3 (Write configuration)
    [InlineData(0x07)] // Func 7 (Proprietary write)
    [InlineData(0x0F)] // Func 15 (Firmware operation)
    public void Allowlist_RejectsForbiddenFunctionIdsOnBatteryFeature(byte forbiddenFuncId)
    {
        byte[] frame = new byte[20];
        frame[0] = HidppAllowlist.ReportIdLong;
        frame[1] = 1;
        frame[2] = 0x04; // feature index
        frame[3] = (byte)(forbiddenFuncId << 4); // forbidden func

        Assert.Throws<SecurityException>(() =>
        {
            HidppAllowlist.ValidateOutgoingRequest(frame);
        });
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x03)]
    [InlineData(0x05)]
    public void Allowlist_RejectsForbiddenFunctionsOnRootFeature(byte forbiddenFuncId)
    {
        byte[] frame = new byte[20];
        frame[0] = HidppAllowlist.ReportIdLong;
        frame[1] = 1;
        frame[2] = 0x00; // Root feature
        frame[3] = (byte)(forbiddenFuncId << 4);
        frame[4] = 0x10;
        frame[5] = 0x00;

        Assert.Throws<SecurityException>(() =>
        {
            HidppAllowlist.ValidateOutgoingRequest(frame);
        });
    }

    [Theory]
    [InlineData((byte)0x01)]
    [InlineData((byte)0x02)]
    [InlineData((byte)0x05)]
    [InlineData((byte)0x80)]
    public void Allowlist_RejectsForbiddenReportIds(byte forbiddenReportId)
    {
        byte[] frame = new byte[20];
        frame[0] = forbiddenReportId;
        frame[1] = 1;
        frame[2] = 0x00;
        frame[3] = 0x00;

        Assert.Throws<SecurityException>(() =>
        {
            HidppAllowlist.ValidateOutgoingRequest(frame);
        });
    }

    [Fact]
    public void Allowlist_RejectsTruncatedFrames()
    {
        byte[] shortFrame = new byte[3] { 0x11, 0x01, 0x00 };
        Assert.Throws<SecurityException>(() =>
        {
            HidppAllowlist.ValidateOutgoingRequest(shortFrame);
        });
    }
}
