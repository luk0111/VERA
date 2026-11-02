using Microsoft.Extensions.Logging;

using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace Vera.Input.Platform;

public enum DpiAwareness
{
    /// <summary>Windows scales the app's view of the screen, so pixels and coordinates are off at more than 100%.</summary>
    Unaware,

    SystemAware,

    /// <summary>Per-monitor (version 1): no scaling, but child windows and dialogs aren't handled.</summary>
    PerMonitor,

    /// <summary>What the input layer needs: real pixels on every monitor.</summary>
    PerMonitorV2,
}

/// <summary>The process isn't Per-Monitor V2 DPI aware and it can't be switched any more. The message says how to fix it.</summary>
public sealed class DpiAwarenessException : Exception
{
    public DpiAwarenessException()
    {
    }

    public DpiAwarenessException(string message)
        : base(message)
    {
    }

    public DpiAwarenessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Makes sure screenshots and clicks use real pixels. DPI awareness belongs to the process and is declared in the
/// exe's manifest; without it Windows scales coordinates at display scales above 100%.
/// </summary>
public interface IDpiAwarenessCheck
{
    /// <summary>The DPI awareness of the calling thread.</summary>
    DpiAwareness Current { get; }

    /// <summary>
    /// Returns if the process is Per-Monitor V2. Otherwise switches it, which only works before the process has
    /// created a window, and logs a warning that the manifest is missing.
    /// </summary>
    /// <exception cref="DpiAwarenessException">The process can't be switched.</exception>
    void EnsurePerMonitorV2();
}

internal sealed partial class DpiAwarenessCheck(ILogger<DpiAwarenessCheck> logger) : IDpiAwarenessCheck
{
    public DpiAwareness Current
    {
        get
        {
            var context = PInvoke.GetThreadDpiAwarenessContext();
            if (PInvoke.AreDpiAwarenessContextsEqual(context, DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            {
                return DpiAwareness.PerMonitorV2;
            }

            return PInvoke.GetAwarenessFromDpiAwarenessContext(context) switch
            {
                DPI_AWARENESS.DPI_AWARENESS_SYSTEM_AWARE => DpiAwareness.SystemAware,
                DPI_AWARENESS.DPI_AWARENESS_PER_MONITOR_AWARE => DpiAwareness.PerMonitor,
                _ => DpiAwareness.Unaware,
            };
        }
    }

    public void EnsurePerMonitorV2()
    {
        var before = Current;
        if (before == DpiAwareness.PerMonitorV2)
        {
            return;
        }

        if (!PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
        {
            throw new DpiAwarenessException(
                $"The process is DPI {before}, but the input layer needs Per-Monitor V2, and it can't be switched any more. " +
                "Add an app.manifest with <dpiAwareness>PerMonitorV2</dpiAwareness> to the exe project " +
                "(<ApplicationManifest>app.manifest</ApplicationManifest>), or <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode> for WinForms.");
        }

        LogSwitchedAtRuntime(logger, before);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "The process was DPI {Before} and was switched to Per-Monitor V2 at runtime. Declare it in the exe's manifest instead, so it can't fail after a window exists.")]
    private static partial void LogSwitchedAtRuntime(ILogger logger, DpiAwareness before);
}