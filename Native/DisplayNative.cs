using System.Runtime.InteropServices;

namespace MonitorControl.Native;

internal static class DisplayNative
{
    // QueryDisplayConfig flags
    public const uint QdcAllPaths = 0x00000001;
    public const uint QdcOnlyActivePaths = 0x00000002;
    public const uint QdcVirtualModeAware = 0x00000010;
    public const uint QdcVirtualRefreshRateAware = 0x00000040;
    public const uint ConfigFlags = QdcAllPaths | QdcVirtualModeAware | QdcVirtualRefreshRateAware;

    // DISPLAYCONFIG_PATH_INFO flags
    public const uint PathActive = 0x00000001;
    public const uint PathSupportVirtualMode = 0x00000008;
    public const uint InvalidModeIndex = 0xFFFFFFFF;
    public const uint InvalidVirtualModeIndex = 0x0000FFFF;

    // SetDisplayConfig flags
    public const uint SdcTopologyExtend = 0x00000004;
    public const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    public const uint SdcApply = 0x00000080;
    public const uint SdcSaveToDatabase = 0x00000200;
    public const uint SdcAllowChanges = 0x00000400;
    public const uint SdcVirtualModeAware = 0x00008000;
    public const uint SdcVirtualRefreshRateAware = 0x00020000;
    public const uint ApplyFlags = SdcApply | SdcUseSuppliedDisplayConfig | SdcSaveToDatabase | SdcAllowChanges | SdcVirtualModeAware | SdcVirtualRefreshRateAware;
    public const uint ApplyBasicFlags = SdcApply | SdcUseSuppliedDisplayConfig | SdcSaveToDatabase | SdcAllowChanges;

    // DisplayConfigGetDeviceInfo types
    public const int GetSourceNameType = 1;
    public const int GetTargetNameType = 2;

    // Win32 & GDI constants
    public const int ErrorInsufficientBuffer = 122;
    public const uint EnumCurrentSettings = uint.MaxValue;

    // ChangeDisplaySettingsEx flags
    public const uint CdsUpdateRegistry = 0x00000001;
    public const uint CdsTest = 0x00000002;
    public const uint CdsSetPrimary = 0x00000010;
    public const uint CdsNoReset = 0x10000000;

    // DEVMODE field bits
    public const uint DmPosition = 0x00000020;
    public const uint DmDisplayOrientation = 0x00000080;
    public const uint DmPelsWidth = 0x00080000;
    public const uint DmPelsHeight = 0x00100000;
    public const uint DmDisplayFrequency = 0x00400000;

    // DISPLAYCONFIG_MODE_INFO offsets
    public const int SourceModeType = 1;
    public const int ModePositionXOffset = 28;
    public const int ModePositionYOffset = 32;

    // Window messages for display / hardware change notifications
    public const int WmDisplayChange = 0x007E;
    public const int WmDeviceChange = 0x0219;
    public const int DbtDevNodesChanged = 0x0007;
    public static readonly IntPtr HwndBroadcast = new(0xFFFF);

    public static readonly int WmActivateExistingInstance =
        RegisterWindowMessage("MonitorControl_ActivateExistingInstance_v1");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayPathInfo[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayPathInfo[]? pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayModeInfo[]? modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettings(
        string lpszDeviceName,
        uint iModeNum,
        ref DevMode lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        ref DevMode lpDevMode,
        IntPtr hwnd,
        uint dwFlags,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        IntPtr lpDevMode,
        IntPtr hwnd,
        uint dwFlags,
        IntPtr lParam);

    public static string DescribeConnector(uint outputTechnology)
    {
        if (outputTechnology == 0xFFFFFFFF) return "Saída externa";
        var isInternal = (outputTechnology & 0x80000000) != 0;
        var baseTech = outputTechnology & ~0x80000000u;

        return baseTech switch
        {
            0 => "VGA",
            1 => "S-Video",
            2 => "Vídeo Composto",
            3 => "Vídeo Componente",
            4 => "DVI",
            5 => "HDMI",
            6 => "Tela interna (LVDS)",
            9 => "SDI",
            10 => "DisplayPort",
            11 => "Tela interna (eDP)",
            12 => "UDI",
            13 => "Tela interna (UDI)",
            15 => "Sem fio (Miracast)",
            16 => "Adaptador USB",
            17 => "Tela virtual",
            18 => "USB-C (DisplayPort)",
            _ => isInternal ? "Tela interna" : "Monitor externo"
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayPathSourceInfo
    {
        public DisplayLuid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayPathTargetInfo
    {
        public DisplayLuid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayRational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayPathInfo
    {
        public DisplayPathSourceInfo Source;
        public DisplayPathTargetInfo Target;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    internal unsafe struct DisplayModeInfo
    {
        public uint InfoType;
        public uint Id;
        public DisplayLuid AdapterId;
        public fixed byte ModeInfo[48];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;

        public static DevMode Create() => new()
        {
            DeviceName = string.Empty,
            FormName = string.Empty,
            Size = (ushort)Marshal.SizeOf<DevMode>()
        };
    }
}
