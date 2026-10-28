using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Vera.Input.Options;

namespace Vera.Input.Platform;

/// <param name="CaptureMonitor">The monitor screenshots will be taken from (<c>Display.CaptureTarget</c>).</param>
/// <param name="Mismatches">Differences from <c>Display.Expected*</c>, written for the tester. Empty when all is as expected.</param>
public sealed record DisplayReport(DisplaySnapshot Snapshot, MonitorInfo CaptureMonitor, IReadOnlyList<string> Mismatches);

/// <summary>The screen isn't usable as configured: the capture monitor doesn't exist, or it doesn't match with <c>Display.StrictMismatch</c> on.</summary>
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

/// <summary>Reads the monitor setup, logs it, and compares the capture monitor with <c>Display.Expected*</c>.</summary>
public interface IDisplayCheck
{
    /// <summary>Makes the process Per-Monitor V2 first, so the sizes are real pixels. Mismatches are logged as warnings.</summary>
    /// <exception cref="DisplayMismatchException">The capture monitor doesn't exist, or there are mismatches and <c>Display.StrictMismatch</c> is on.</exception>
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
        var display = options.Value.Display;

        LogVirtualDesktop(logger, snapshot.VirtualDesktop, snapshot.Monitors.Count);
        foreach (var monitor in snapshot.Monitors)
        {
            LogMonitor(logger, monitor.Index, monitor.DeviceName, monitor.Bounds, monitor.ScalePercent, monitor.RefreshHz, monitor.IsPrimary);
        }

        if (snapshot.IsRemoteSession)
        {
            LogRemoteSession(logger);
        }

        var report = Evaluate(snapshot, display);
        foreach (var mismatch in report.Mismatches)
        {
            LogMismatch(logger, mismatch);
        }

        if (display.StrictMismatch && report.Mismatches.Count > 0)
        {
            throw new DisplayMismatchException(
                $"The screen doesn't match the configuration (Display.StrictMismatch is on): {string.Join(" ", report.Mismatches)}");
        }

        return report;
    }

    /// <summary>Picks the capture monitor and lists how it differs from the expected size and scale. Pure, so it can be tested.</summary>
    /// <exception cref="DisplayMismatchException">The configured capture monitor doesn't exist.</exception>
    internal static DisplayReport Evaluate(DisplaySnapshot snapshot, DisplayOptions display)
    {
        var capture = display.CaptureTarget switch
        {
            CaptureTarget.MonitorIndex => snapshot.Monitors.FirstOrDefault(m => m.Index == display.MonitorIndex)
                ?? throw new DisplayMismatchException(
                    $"Display.MonitorIndex is {display.MonitorIndex}, but there are only {snapshot.Monitors.Count} monitor(s) (0..{snapshot.Monitors.Count - 1})."),
            _ => snapshot.Monitors.FirstOrDefault(m => m.IsPrimary)
                ?? throw new DisplayMismatchException("Windows reports no primary monitor."),
        };

        var mismatches = new List<string>();
        if (capture.Bounds.Width != display.ExpectedWidth || capture.Bounds.Height != display.ExpectedHeight)
        {
            mismatches.Add(
                $"Monitor {capture.Index} is {capture.Bounds.Width}×{capture.Bounds.Height}, expected {display.ExpectedWidth}×{display.ExpectedHeight}.");
        }

        if (capture.ScalePercent != display.ExpectedScalePercent)
        {
            mismatches.Add($"Monitor {capture.Index} is scaled {capture.ScalePercent}%, expected {display.ExpectedScalePercent}%.");
        }

        return new DisplayReport(snapshot, capture, mismatches);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Virtual desktop {Bounds}, {Count} monitor(s)")]
    private static partial void LogVirtualDesktop(ILogger logger, ScreenRect bounds, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitor {Index} {Device}: {Bounds}, scale {ScalePercent}%, {RefreshHz} Hz, primary {IsPrimary}")]
    private static partial void LogMonitor(ILogger logger, int index, string device, ScreenRect bounds, int scalePercent, int refreshHz, bool isPrimary);

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Someone is connected to this machine over RDP. That takes over its screen, so screenshots and input won't work as expected.")]
    private static partial void LogRemoteSession(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display mismatch: {Mismatch}")]
    private static partial void LogMismatch(ILogger logger, string mismatch);
}