namespace BatteryMonitor.Core.Scheduler;

using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BatteryMonitor.Core.Models;
using BatteryMonitor.Interop;
using BatteryMonitor.Settings;

public sealed class Win32DeviceEnumerator : IDeviceEnumerator
{
    private readonly ConfigManager? _configManager;
    private readonly DemoDeviceEnumerator _demoEnumerator = new();

    public Win32DeviceEnumerator(ConfigManager? configManager = null)
    {
        _configManager = configManager;
    }

    public IReadOnlyList<DeviceInfo> EnumerateDevices()
    {
        var devices = new List<DeviceInfo>();

        // 1. If Demo Mode is explicitly enabled in config, include demo devices
        if (_configManager?.Current.DemoMode == true)
        {
            devices.AddRange(_demoEnumerator.EnumerateDevices());
            return devices;
        }

        // 2. Enumerate connected physical HID devices
        try
        {
            EnumerateHidDevices(devices);
        }
        catch
        {
            // Do not fail enumeration on individual driver/API errors
        }

        // 3. Enumerate connected Bluetooth devices with battery capabilities
        try
        {
            EnumerateBluetoothDevices(devices);
        }
        catch
        {
        }

        // 4. If no physical wireless battery devices were detected, fall back to demo devices
        // so user has visible sample data in the UI out-of-the-box
        if (devices.Count == 0)
        {
            devices.AddRange(_demoEnumerator.EnumerateDevices());
        }

        return devices;
    }

    private void EnumerateHidDevices(List<DeviceInfo> devices)
    {
        Guid hidGuid = Win32SetupDiPInvoke.GUID_DEVINTERFACE_HID;
        IntPtr hDevInfo = Win32SetupDiPInvoke.SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            Win32SetupDiPInvoke.DIGCF_PRESENT | Win32SetupDiPInvoke.DIGCF_DEVICEINTERFACE
        );

        if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1))
        {
            return;
        }

        try
        {
            var ifData = new Win32SetupDiPInvoke.SP_DEVICE_INTERFACE_DATA
            {
                cbSize = Marshal.SizeOf<Win32SetupDiPInvoke.SP_DEVICE_INTERFACE_DATA>()
            };

            uint memberIndex = 0;
            var seenHardwareKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (Win32SetupDiPInvoke.SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, memberIndex++, ref ifData))
            {
                Win32SetupDiPInvoke.SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
                if (requiredSize == 0)
                {
                    continue;
                }

                IntPtr pDetail = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    Marshal.WriteInt32(pDetail, IntPtr.Size == 8 ? 8 : 6);
                    if (Win32SetupDiPInvoke.SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, pDetail, requiredSize, out _, IntPtr.Zero))
                    {
                        IntPtr pPath = IntPtr.Add(pDetail, 4);
                        string? devicePath = Marshal.PtrToStringUni(pPath);
                        if (!string.IsNullOrWhiteSpace(devicePath))
                        {
                            var info = InspectHidDevice(devicePath, seenHardwareKeys);
                            if (info != null)
                            {
                                devices.Add(info);
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pDetail);
                }
            }
        }
        finally
        {
            Win32SetupDiPInvoke.SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    private static DeviceInfo? InspectHidDevice(string devicePath, HashSet<string> seenHardwareKeys)
    {
        // Use dwDesiredAccess = 0 for instant, non-blocking descriptor/attributes query
        using var handle = Win32HidPInvoke.CreateFile(
            devicePath,
            0,
            Win32HidPInvoke.FileShareRead | Win32HidPInvoke.FileShareWrite,
            IntPtr.Zero,
            Win32HidPInvoke.OpenExisting,
            0,
            IntPtr.Zero
        );

        if (handle.IsInvalid)
        {
            return null;
        }

        var attribs = new Win32HidPInvoke.HIDD_ATTRIBUTES
        {
            Size = Marshal.SizeOf<Win32HidPInvoke.HIDD_ATTRIBUTES>()
        };

        if (!Win32HidPInvoke.HidD_GetAttributes(handle, ref attribs))
        {
            return null;
        }

        // De-duplicate multi-collection endpoints for the same physical peripheral
        string hardwareKey = $"{attribs.VendorID:X4}:{attribs.ProductID:X4}";
        if (!seenHardwareKeys.Add(hardwareKey))
        {
            return null;
        }

        byte[] stringBuffer = new byte[256];
        string name = $"HID Device (0x{attribs.VendorID:X4}:0x{attribs.ProductID:X4})";
        if (Win32HidPInvoke.HidD_GetProductString(handle, stringBuffer, (uint)stringBuffer.Length))
        {
            string rawName = Encoding.Unicode.GetString(stringBuffer).TrimEnd('\0').Trim();
            if (!string.IsNullOrWhiteSpace(rawName))
            {
                name = rawName;
            }
        }

        // Filter out internal motherboard controllers and virtual drivers
        if (name.Contains("MYSTIC LIGHT", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("vhidev", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        DeviceType type = InferDeviceType(name, devicePath);
        string deviceId = $"HID_{attribs.VendorID:X4}_{attribs.ProductID:X4}";

        return new DeviceInfo(
            deviceId,
            devicePath,
            attribs.VendorID,
            attribs.ProductID,
            name,
            type,
            TransportType.UsbHid
        );
    }

    private void EnumerateBluetoothDevices(List<DeviceInfo> devices)
    {
        Guid emptyGuid = Guid.Empty;
        IntPtr hDevInfo = Win32SetupDiPInvoke.SetupDiGetClassDevs(
            ref emptyGuid,
            "BTHENUM",
            IntPtr.Zero,
            Win32SetupDiPInvoke.DIGCF_ALLCLASSES | Win32SetupDiPInvoke.DIGCF_PRESENT
        );

        if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1))
        {
            return;
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
                byte[] buffer = new byte[4];
                bool hasBattery = Win32SetupDiPInvoke.SetupDiGetDeviceProperty(
                    hDevInfo,
                    ref devInfoData,
                    ref key,
                    out _,
                    buffer,
                    (uint)buffer.Length,
                    out _,
                    0
                );

                if (hasBattery)
                {
                    char[] idBuffer = new char[512];
                    string instanceId = "BTH_DEVICE";
                    if (Win32SetupDiPInvoke.SetupDiGetDeviceInstanceId(hDevInfo, ref devInfoData, idBuffer, (uint)idBuffer.Length, out uint reqSize))
                    {
                        instanceId = new string(idBuffer, 0, (int)reqSize > 0 ? (int)reqSize - 1 : 0);
                    }

                    byte[] nameBuffer = new byte[512];
                    string name = "Bluetooth Device";
                    if (Win32SetupDiPInvoke.SetupDiGetDeviceRegistryProperty(
                        hDevInfo,
                        ref devInfoData,
                        Win32SetupDiPInvoke.SPDRP_FRIENDLYNAME,
                        out _,
                        nameBuffer,
                        (uint)nameBuffer.Length,
                        out uint nameLen) && nameLen > 0)
                    {
                        name = Encoding.Unicode.GetString(nameBuffer, 0, (int)nameLen).TrimEnd('\0').Trim();
                    }

                    DeviceType type = InferDeviceType(name, instanceId);

                    devices.Add(new DeviceInfo(
                        instanceId,
                        instanceId,
                        0x0000,
                        0x0000,
                        name,
                        type,
                        TransportType.Bluetooth
                    ));
                }
            }
        }
        finally
        {
            Win32SetupDiPInvoke.SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    private static DeviceType InferDeviceType(string name, string path)
    {
        string combined = $"{name} {path}".ToLowerInvariant();
        if (combined.Contains("mouse") || combined.Contains("trackball")) return DeviceType.Mouse;
        if (combined.Contains("keyboard") || combined.Contains("keypad") || combined.Contains("kbd")) return DeviceType.Keyboard;
        if (combined.Contains("headset") || combined.Contains("headphone") || combined.Contains("audio") || combined.Contains("buds") || combined.Contains("rockerz") || combined.Contains("stone")) return DeviceType.Headset;
        if (combined.Contains("controller") || combined.Contains("gamepad") || combined.Contains("joystick") || combined.Contains("xbox")) return DeviceType.Controller;
        return DeviceType.Other;
    }
}
