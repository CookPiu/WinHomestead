using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Native;

/// <summary>
/// 主显示器的刷新率。读用 EnumDisplaySettings 枚举全部模式，而不是 Win32_VideoController.MaxRefreshRate——
/// 后者是显卡能力，与当前分辨率下面板实际支持的模式不是一回事，拿来判断"刷新率有没有拉满"会误报。
/// 写用 ChangeDisplaySettingsEx，只带 DM_DISPLAYFREQUENCY 字段，分辨率与色深保持不动。
/// </summary>
public sealed class WindowsDisplay : IDisplay
{
    private const int EnumCurrentSettings = -1;
    private const int DmDisplayFrequency = 0x400000;
    private const int CdsUpdateRegistry = 0x00000001;
    private const int DispChangeSuccessful = 0;
    private const int DispChangeRestart = 1;

    private readonly ILogger _log;

    public WindowsDisplay(ILogger log) { _log = log; }

    public DisplayMode? Primary()
    {
        try
        {
            var current = New();
            if (!EnumDisplaySettings(null, EnumCurrentSettings, ref current)) return null;
            if (current.dmPelsWidth <= 0 || current.dmDisplayFrequency <= 1) return null;

            var max = current.dmDisplayFrequency;
            for (var i = 0; ; i++)
            {
                var mode = New();
                if (!EnumDisplaySettings(null, i, ref mode)) break;
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight
                    && mode.dmBitsPerPel == current.dmBitsPerPel && mode.dmDisplayFrequency > max)
                    max = mode.dmDisplayFrequency;
            }
            return new DisplayMode(current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency, max);
        }
        catch (Exception ex)
        {
            _log.Warn("读取显示模式失败: " + ex.Message);
            return null;
        }
    }

    public void SetRefreshRate(int hz)
    {
        var mode = New();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
            throw new InvalidOperationException("读不到当前显示模式");

        mode.dmDisplayFrequency = hz;
        mode.dmFields = DmDisplayFrequency;
        var result = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        if (result != DispChangeSuccessful && result != DispChangeRestart)
            throw new Win32Exception(result, $"切换刷新率到 {hz} Hz 失败，ChangeDisplaySettingsEx 返回 {result}");
        _log.Info($"刷新率切到 {hz} Hz（返回码 {result}）");
    }

    private static Devmode New() => new() { dmSize = (short)Marshal.SizeOf(typeof(Devmode)) };

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref Devmode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref Devmode devMode, IntPtr hwnd, int flags, IntPtr param);

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
