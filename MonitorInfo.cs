using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SoloScreen;

/// <summary>
/// 查询显示器的友好名称。
/// 名称来源：显示器 EDID 中的型号名（如 "P27QBD-RG"）；
/// 内外屏判定：DisplayConfig 的输出接口类型（内置屏显示为 "内置显示器"）。
/// 返回字典：键 = GDI 设备名（\\.\DISPLAY1，与 Screen.DeviceName 一致），值 = 友好名称。
/// 任何环节失败都只是少一条名称，调用方回退到分辨率描述。
/// </summary>
internal static class MonitorInfo
{
    public static IReadOnlyDictionary<string, string> GetFriendlyNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var internalFlags = QueryInternalFlags();

            foreach (var (deviceName, hardwareId) in EnumerateActiveAdapters())
            {
                var name = ReadEdidFriendlyName(hardwareId);
                bool isInternal = internalFlags.TryGetValue(deviceName, out var f) && f;

                if (isInternal)
                    name = name.Length > 0 ? Strings.BuiltInDisplayFmt(name) : Strings.BuiltInDisplay;
                if (name.Length == 0) continue;

                result[deviceName] = name;
            }
        }
        catch (Exception ex)
        {
            SimpleLog.Write("查询显示器名称失败：" + ex.Message);
        }
        return result;
    }

    /// <summary>遍历活动的显示适配器，返回 (GDI 设备名, 显示器硬件 ID)。</summary>
    private static List<(string DeviceName, string HardwareId)> EnumerateActiveAdapters()
    {
        var list = new List<(string, string)>();
        int cb = Marshal.SizeOf<DISPLAY_DEVICE>();

        for (int i = 0; i < 32; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = cb };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & DISPLAY_DEVICE_ACTIVE) == 0) continue;

            var monitor = new DISPLAY_DEVICE { cb = cb };
            if (!EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0)) continue;

            // monitor.DeviceID 形如 "MONITOR\XMID005\{4d36e96e-...}\0003"
            var parts = monitor.DeviceID.Split('\\');
            if (parts.Length >= 2 && parts[1].Length > 0)
                list.Add((adapter.DeviceName, parts[1]));
        }
        return list;
    }

    /// <summary>从注册表读取 EDID 并解析 0xFC 描述符里的显示器型号名。</summary>
    private static string ReadEdidFriendlyName(string hardwareId)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{hardwareId}");
        if (key is null) return string.Empty;

        foreach (var instance in key.GetSubKeyNames())
        {
            using var parameters = key.OpenSubKey($@"{instance}\Device Parameters");
            if (parameters?.GetValue("EDID") is not byte[] edid || edid.Length < 128) continue;

            var name = ParseEdidName(edid);
            if (name.Length > 0) return name;
        }
        return string.Empty;
    }

    /// <summary>解析 EDID 四个描述符块中的 0xFC（显示器名称）块。</summary>
    private static string ParseEdidName(byte[] edid)
    {
        foreach (var offset in new[] { 54, 72, 90, 108 })
        {
            if (edid.Length < offset + 18) continue;
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0 || edid[offset + 3] != 0xFC)
                continue;

            var sb = new StringBuilder(13);
            for (int i = offset + 5; i < offset + 18; i++)
            {
                byte b = edid[i];
                if (b == 0x0A || b == 0x00) break;
                if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
            }
            return sb.ToString().Trim();
        }
        return string.Empty;
    }

    /// <summary>用 DisplayConfig 判断每个 GDI 设备是否为内置屏。</summary>
    private static Dictionary<string, bool> QueryInternalFlags()
    {
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        int pathCount = 0, modeCount = 0;
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, ref pathCount, ref modeCount) != 0 || pathCount <= 0)
            return flags;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        int p = pathCount, m = modeCount;
        IntPtr modeBuffer = Marshal.AllocHGlobal(Math.Max(1, modeCount) * DISPLAYCONFIG_MODE_INFO_SIZE);
        try
        {
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref p, paths, ref m, modeBuffer, IntPtr.Zero) != 0)
                return flags;
        }
        finally
        {
            Marshal.FreeHGlobal(modeBuffer);
        }

        for (int i = 0; i < p; i++)
        {
            var path = paths[i];

            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            source.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
            source.header.adapterId = path.sourceInfo.adapterId;
            source.header.id = path.sourceInfo.id;
            source.viewGdiDeviceName = string.Empty;
            if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

            flags[source.viewGdiDeviceName] = IsInternalTechnology(path.targetInfo.outputTechnology);
        }
        return flags;
    }

    private static bool IsInternalTechnology(int outputTechnology)
        => outputTechnology switch
        {
            unchecked((int)0x80000000) => true, // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
            6 => true,                          // LVDS
            11 => true,                         // DISPLAYPORT_EMBEDDED
            13 => true,                         // UDI_EMBEDDED
            _ => false,
        };

    #region Win32 定义

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const int DISPLAYCONFIG_MODE_INFO_SIZE = 64;
    private const uint DISPLAY_DEVICE_ACTIVE = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public int scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, int iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, ref int numPathArrayElements, ref int numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref int numPathArrayElements,
        [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref int numModeInfoArrayElements,
        IntPtr modeInfoArray, IntPtr topLevelId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    #endregion
}
