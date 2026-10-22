using System.Runtime.InteropServices;

namespace KResetExplode;

/// <summary>Monitor ativo: resolução e taxa de atualização atuais, a maior taxa disponível nessa resolução e a placa onde está ligado.</summary>
public sealed record MonitorMode(string Device, string MonitorName, string Adapter, int Width, int Height, int Hz, int MaxHz, int Bpp);

/// <summary>
/// Lê e troca a taxa de atualização dos monitores (EnumDisplaySettings / ChangeDisplaySettingsEx). Um erro muito comum é o monitor de
/// 144 Hz rodando a 60 Hz porque o Windows nunca foi ajustado. A troca é a mesma do "Configurações de vídeo avançadas" do Windows.
/// </summary>
public static class DisplayModes
{
    public static List<MonitorMode> ActiveMonitors()
    {
        var list = new List<MonitorMode>();
        for (uint i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var cur = NewMode();
            if (!EnumDisplaySettingsW(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref cur)) continue;
            int max = (int)cur.dmDisplayFrequency;
            for (int k = 0; ; k++)
            {
                var m = NewMode();
                if (!EnumDisplaySettingsW(dd.DeviceName, k, ref m)) break;
                if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmBitsPerPel == cur.dmBitsPerPel
                    && (m.dmDisplayFlags & DM_INTERLACED) == 0 && m.dmDisplayFrequency > max)
                    max = (int)m.dmDisplayFrequency;
            }

            string monitor = "";
            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevicesW(dd.DeviceName, 0, ref mon, 0)) monitor = mon.DeviceString;
            list.Add(new MonitorMode(dd.DeviceName, monitor, dd.DeviceString, (int)cur.dmPelsWidth, (int)cur.dmPelsHeight,
                                     (int)cur.dmDisplayFrequency, max, (int)cur.dmBitsPerPel));
        }
        return list;
    }

    /// <summary>Muda a taxa de atualização mantendo a resolução. Com <paramref name="testOnly"/> só pergunta ao driver se aceitaria.</summary>
    public static string? SetRefreshRate(MonitorMode m, int hz, bool testOnly = false)
    {
        var dm = NewMode();
        dm.dmPelsWidth = (uint)m.Width;
        dm.dmPelsHeight = (uint)m.Height;
        dm.dmDisplayFrequency = (uint)hz;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        int r = ChangeDisplaySettingsExW(m.Device, ref dm, IntPtr.Zero, testOnly ? CDS_TEST : CDS_UPDATEREGISTRY, IntPtr.Zero);
        return r switch
        {
            DISP_CHANGE_SUCCESSFUL => null,
            DISP_CHANGE_RESTART => "o Windows pede para reiniciar o PC para aplicar",
            DISP_CHANGE_BADMODE => "o monitor ou a placa não aceita essa taxa nesta resolução",
            _ => $"o Windows recusou a mudança (código {r})",
        };
    }

    private static DEVMODE NewMode() => new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1, DM_INTERLACED = 0x2;
    private const uint DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    private const uint CDS_UPDATEREGISTRY = 0x1, CDS_TEST = 0x2;
    private const int DISP_CHANGE_SUCCESSFUL = 0, DISP_CHANGE_RESTART = 1, DISP_CHANGE_BADMODE = -2;

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

    // DEVMODEW, variante de vídeo (dmPosition / dmDisplayOrientation / dmDisplayFixedOutput no lugar dos campos de impressora).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(string device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);
}
