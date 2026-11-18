namespace BatteryMonitor.Interop;

using System.Runtime.InteropServices;

public sealed class Win32BluetoothPropertyReader : IBluetoothPropertyReader
{
    public byte? ReadBatteryStrength(string devicePathOrId)
    {
        if (string.IsNullOrWhiteSpace(devicePathOrId))
        {
            return null;
        }

        Guid emptyGuid = Guid.Empty;
        IntPtr hDevInfo = Win32SetupDiPInvoke.SetupDiGetClassDevs(
            ref emptyGuid,
            "BTHENUM",
            IntPtr.Zero,
            Win32SetupDiPInvoke.DIGCF_ALLCLASSES | Win32SetupDiPInvoke.DIGCF_PRESENT
        );

        if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1))
        {
            return null;
        }

        try
        {
            var devInfoData = new Win32SetupDiPInvoke.SP_DEVINFO_DATA
            {
                cbSize = Marshal.SizeOf<Win32SetupDiPInvoke.SP_DEVINFO_DATA>()
            };

            uint memberIndex = 0;
            var key = Win32SetupDiPInvoke.PKEY_Device_BatteryStrength;

            while (Win32SetupDiPInvoke.SetupDiEnumDeviceInfo(hDevInfo, memberIndex++, ref devInfoData))
            {
                // Match device by instance ID if specified and not a wildcard
                if (!string.Equals(devicePathOrId, "ANY", StringComparison.OrdinalIgnoreCase))
                {
                    char[] idBuffer = new char[512];
                    if (Win32SetupDiPInvoke.SetupDiGetDeviceInstanceId(hDevInfo, ref devInfoData, idBuffer, (uint)idBuffer.Length, out uint reqSize))
                    {
                        string instanceId = new string(idBuffer, 0, (int)reqSize > 0 ? (int)reqSize - 1 : 0);
                        if (!instanceId.Contains(devicePathOrId, StringComparison.OrdinalIgnoreCase) &&
                            !devicePathOrId.Contains(instanceId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                }

                byte[] buffer = new byte[4];
                bool success = Win32SetupDiPInvoke.SetupDiGetDeviceProperty(
                    hDevInfo,
                    ref devInfoData,
                    ref key,
                    out uint propType,
                    buffer,
                    (uint)buffer.Length,
                    out _,
                    0
                );

                if (success && buffer.Length > 0 && buffer[0] <= 100)
                {
                    return buffer[0];
                }
            }
        }
        catch
        {
            // Fail gracefully without crashing
        }
        finally
        {
            Win32SetupDiPInvoke.SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return null;
    }
}
