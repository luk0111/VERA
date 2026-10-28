namespace Vera.Input.Platform;

/// <summary>A rectangle in physical screen pixels. <see cref="Left"/> and <see cref="Top"/> can be negative on multi-monitor setups.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height} at ({Left},{Top})";
}

/// <param name="Index">Position in the order Windows lists the monitors; <c>Display.MonitorIndex</c> refers to it.</param>
/// <param name="DeviceName">E.g. <c>\\.\DISPLAY1</c>.</param>
/// <param name="Bounds">The monitor's area in physical pixels.</param>
/// <param name="ScalePercent">The Windows display scale, e.g. 100 or 125 (effective DPI × 100 / 96).</param>
/// <param name="RefreshHz">0 if Windows doesn't report it.</param>
public sealed record MonitorInfo(int Index, string DeviceName, ScreenRect Bounds, bool IsPrimary, int Dpi, int ScalePercent, int RefreshHz);

/// <param name="VirtualDesktop">The area covering all monitors.</param>
/// <param name="IsRemoteSession">Someone is connected to this machine over RDP, which takes over its screen.</param>
public sealed record DisplaySnapshot(ScreenRect VirtualDesktop, IReadOnlyList<MonitorInfo> Monitors, bool IsRemoteSession);

/// <summary>Reads the current monitor setup. The process must be Per-Monitor V2 DPI aware, or the sizes are scaled.</summary>
public interface IDisplayInfo
{
    DisplaySnapshot Read();
}