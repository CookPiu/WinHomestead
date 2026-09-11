using System;
using System.Runtime.InteropServices;

namespace WinHomestead.Native;

/// <summary>主显示器当前的分辨率与刷新率，以及同一分辨率下驱动报告的最高刷新率。</summary>
public sealed record DisplayMode(int Width, int Height, int Hz, int MaxHz);

/// <summary>
/// 走 EnumDisplaySettings 而不是 Win32_VideoController：后者的 MaxRefreshRate 是显卡能力，
/// 与当前分辨率下面板实际支持的模式不是一回事，用来判断“刷新率有没有拉满”会误报。
/// </summary>
public static class DisplayInfo
{
    private const int EnumCurrentSettings = -1;

    public static DisplayMode? Primary()
    {
        try
        {
            var current = new Devmode { dmSize = (short)Marshal.SizeOf(typeof(Devmode)) };
            if (!EnumDisplaySettings(null, EnumCurrentSettings, ref current)) return null;
            if (current.dmPelsWidth <= 0 || current.dmDisplayFrequency <= 1) return null;

            var max = current.dmDisplayFrequency;
            for (var i = 0; ; i++)
            {
                var mode = new Devmode { dmSize = (short)Marshal.SizeOf(typeof(Devmode)) };
                if (!EnumDisplaySettings(null, i, ref mode)) break;
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight
                    && mode.dmBitsPerPel == current.dmBitsPerPel && mode.dmDisplayFrequency > max)
                    max = mode.dmDisplayFrequency;
            }
            return new DisplayMode(current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency, max);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref Devmode devMode);

    /// <summary>DEVMODE 的显示设置分支（dmPosition/dmDisplayOrientation 占用 dmOrientation 起的联合体）。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct Devmode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
