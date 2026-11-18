namespace BatteryMonitor.Drivers.Allowlist;

using System.Security;

public static class HidppAllowlist
{
    // Report IDs
    public const byte ReportIdShort = 0x10;
    public const byte ReportIdLong = 0x11;

    // Feature IDs
    public const ushort FeatureRoot = 0x0000;
    public const ushort FeatureUnifiedBattery = 0x1000;
    public const ushort FeatureBatteryVoltage = 0x1001;
    public const ushort FeatureUnifiedBatteryV2 = 0x1004;

    // Function IDs
    public const byte FunctionGetFeature = 0x00;        // Feature 0x0000 func 0
    public const byte FunctionGetCapabilities = 0x00;   // Feature 0x1000/0x1004 func 0
    public const byte FunctionGetBatteryStatus = 0x01;  // Feature 0x1000/0x1004 func 1

    private static readonly HashSet<ushort> AllowedTargetFeatures = new()
    {
        FeatureUnifiedBattery,
        FeatureUnifiedBatteryV2,
        FeatureBatteryVoltage
    };

    public static bool IsAllowedTargetFeature(ushort featureId)
    {
        return AllowedTargetFeatures.Contains(featureId);
    }

    /// <summary>
    /// Validates and constructs a 20-byte HID++ 2.0 Feature Discovery request.
    /// Throws SecurityException if the target feature ID is not allowlisted.
    /// </summary>
    public static byte[] CreateFeatureDiscoveryRequest(byte deviceIndex, ushort targetFeatureId)
    {
        if (!IsAllowedTargetFeature(targetFeatureId))
        {
            throw new SecurityException(
                $"HID++ Security Violation: Feature ID 0x{targetFeatureId:X4} is not on the read-only discovery allowlist."
            );
        }

        byte[] packet = new byte[20];
        packet[0] = ReportIdLong;
        packet[1] = deviceIndex;
        packet[2] = 0x00; // Root feature
        packet[3] = (byte)(FunctionGetFeature << 4); // Function 0, Software ID 0
        packet[4] = (byte)((targetFeatureId >> 8) & 0xFF);
        packet[5] = (byte)(targetFeatureId & 0xFF);
        return packet;
    }

    /// <summary>
    /// Validates and constructs a 20-byte HID++ 2.0 Battery Status request.
    /// Throws SecurityException if the function ID is not on the read-only allowlist.
    /// </summary>
    public static byte[] CreateBatteryStatusRequest(byte deviceIndex, byte featureIndex)
    {
        if (featureIndex == 0x00)
        {
            throw new ArgumentException("Feature index cannot be 0 for battery query.", nameof(featureIndex));
        }

        byte[] packet = new byte[20];
        packet[0] = ReportIdLong;
        packet[1] = deviceIndex;
        packet[2] = featureIndex;
        packet[3] = (byte)(FunctionGetBatteryStatus << 4); // Function 1, Software ID 0
        return packet;
    }

    /// <summary>
    /// Validates an arbitrary outgoing HID++ request frame against the security allowlist.
    /// Throws SecurityException if the frame contains non-allowlisted commands.
    /// </summary>
    public static void ValidateOutgoingRequest(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
        {
            throw new SecurityException("HID++ Security Violation: Frame is too short.");
        }

        byte reportId = frame[0];
        if (reportId != ReportIdLong && reportId != ReportIdShort)
        {
            throw new SecurityException($"HID++ Security Violation: Report ID 0x{reportId:X2} is forbidden.");
        }

        byte featureIndex = frame[2];
        byte funcId = (byte)((frame[3] >> 4) & 0x0F);

        if (featureIndex == 0x00) // Root feature discovery
        {
            if (funcId != FunctionGetFeature)
            {
                throw new SecurityException($"HID++ Security Violation: Function 0x{funcId:X} is forbidden on Root feature.");
            }

            if (frame.Length >= 6)
            {
                ushort targetFeature = (ushort)((frame[4] << 8) | frame[5]);
                if (!IsAllowedTargetFeature(targetFeature))
                {
                    throw new SecurityException($"HID++ Security Violation: Feature 0x{targetFeature:X4} is forbidden.");
                }
            }
        }
        else // Feature invocation on discovered index
        {
            if (funcId != FunctionGetCapabilities && funcId != FunctionGetBatteryStatus)
            {
                throw new SecurityException(
                    $"HID++ Security Violation: Function ID 0x{funcId:X} is not on the read-only allowlist (only func 0 and 1 allowed)."
                );
            }
        }
    }
}
