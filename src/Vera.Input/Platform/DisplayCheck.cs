using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Vera.Input.Options;

namespace Vera.Input.Platform;

/// <param name="CaptureMonitor">The monitor screenshots will be taken from (<c>Display.MonitorIndex</c>).</param>
public sealed record DisplayReport(DisplaySnapshot Snapshot, MonitorInfo CaptureMonitor);

/// <summary>The screen isn't usable as configured: the capture monitor doesn't exist.</summary>
public sealed class DisplayMismatchException : Exception
{
    public DisplayMismatchException()
    {
    }

    public DisplayMismatchException(string message)
        : base(message)
    {
    }

    public DisplayMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Reads the monitor setup, logs it, and picks the capture monitor.</summary>
public interface IDisplayCheck
{
    /// <summary>Makes the process Per-Monitor V2 first, so the sizes are real pixels.</summary>
    /// <exception cref="DisplayMismatchException">The capture monitor doesn't exist.</exception>
    DisplayReport Run();
}

internal sealed partial class DisplayCheck(
    IDpiAwarenessCheck dpi,
    IDisplayInfo displayInfo,
    IOptions<VeraInputOptions> options,
    ILogger<DisplayCheck> logger) : IDisplayCheck
{
    public DisplayReport Run()
    {
        dpi.EnsurePerMonitorV2();
        var snapshot = displayInfo.Read();

        LogVirtualDesktop(logger, snapshot.VirtualDesktop, snapshot.Monitors.Count);
        foreach (var monitor in snapshot.Monitors)
        {
            LogMonitor(logger, monitor.Index, monitor.DeviceName, monitor.Bounds, monitor.ScalePercent, monitor.RefreshHz, monitor.IsPrimary);
        }

        if (snapshot.IsRemoteSession)
        {
            LogRemoteSession(logger);
        }

        return Evaluate(snapshot, options.Value.Display);
    }

    /// <summary>Picks the capture monitor. Pure, so it can be tested.</summary>
    /// <exception cref="DisplayMismatchException">The configured capture monitor doesn't exist.</exception>
    internal static DisplayReport Evaluate(DisplaySnapshot snapshot, DisplayOptions display)
    {
        var capture = display.MonitorIndex is { } index
            ? snapshot.Monitors.FirstOrDefault(m => m.Index == index)
                ?? throw new DisplayMismatchException(
                    $"Display.MonitorIndex is {index}, but there are only {snapshot.Monitors.Count} monitor(s) (0..{snapshot.Monitors.Count - 1}).")
            : snapshot.Monitors.FirstOrDefault(m => m.IsPrimary)
                ?? throw new DisplayMismatchException("Windows reports no primary monitor.");

        return new DisplayReport(snapshot, capture);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Virtual desktop {Bounds}, {Count} monitor(s)")]
    private static partial void LogVirtualDesktop(ILogger logger, ScreenRect bounds, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitor {Index} {Device}: {Bounds}, scale {ScalePercent}%, {RefreshHz} Hz, primary {IsPrimary}")]
    private static partial void LogMonitor(ILogger logger, int index, string device, ScreenRect bounds, int scalePercent, int refreshHz, bool isPrimary);

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Someone is connected to this machine over RDP. That takes over its screen, so screenshots and input won't work as expected.")]
    private static partial void LogRemoteSession(ILogger logger);
}