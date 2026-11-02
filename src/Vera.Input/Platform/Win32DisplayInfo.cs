using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Vera.Input.Platform;

internal sealed unsafe class Win32DisplayInfo : IDisplayInfo
{
    private const int DefaultDpi = 96;

    public DisplaySnapshot Read()
    {
        var virtualDesktop = new ScreenRect(
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN),
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN),
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN),
            PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN));

        var handles = new List<HMONITOR>();
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return true;
        }, default);

        var monitors = handles.Select(ReadMonitor).ToList();
        var isRemote = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;
        return new DisplaySnapshot(virtualDesktop, monitors, isRemote);
    }

    private static MonitorInfo ReadMonitor(HMONITOR monitor, int index)
    {
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
        {
            throw new InvalidOperationException($"GetMonitorInfo failed for monitor {index}.");
        }

        var rect = info.monitorInfo.rcMonitor;
        var deviceName = info.szDevice.ToString();
        var dpi = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded ? (int)dpiX : DefaultDpi;

        var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        var refresh = PInvoke.EnumDisplaySettings(deviceName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode) ? (int)mode.dmDisplayFrequency : 0;

        return new MonitorInfo(
            index,
            deviceName,
            new ScreenRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top),
            (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0,
            dpi,
            (int)Math.Round(dpi * 100.0 / DefaultDpi),
            refresh);
    }
}