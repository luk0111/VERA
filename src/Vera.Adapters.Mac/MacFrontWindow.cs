using Vera.Core;

namespace Vera.Adapters.Mac;

/// <summary>The app in front on macOS, e.g. Terminal or Rider. VERA's own process has no window, so that is the app it was started from.</summary>
public sealed class MacFrontWindow(IRunningAppSource runningApps) : IFrontWindow
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(1);

    /// <summary>How long one app that doesn't answer can hold this up.</summary>
    private const float AppTimeoutSeconds = 0.5f;

    // The list comes front to back, but a small helper window of another app can come first, so macOS is asked which app is in front.
    public FrontWindow? Current()
    {
        var apps = runningApps.List();
        var front = apps.FirstOrDefault(a => IsFrontmost(a.ProcessId)) ?? (apps.Count > 0 ? apps[0] : null);
        return front is null ? null : new FrontWindow(front.ProcessId);
    }

    public async Task<bool> BringBackAsync(FrontWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var app = MacNative.AXUIElementCreateApplication((int)window.Id);
        if (app == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            for (var waited = TimeSpan.Zero; waited < Wait; waited += CheckEvery)
            {
                if (MacNative.AXUIElementSetAttributeValue(app, MacNative.Key("AXFrontmost"), MacNative.CfTrue()) != MacNative.AxSuccess)
                {
                    return false;
                }

                await Task.Delay(CheckEvery, ct).ConfigureAwait(false);
                if (IsFrontmost((int)window.Id))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            MacNative.CFRelease(app);
        }
    }

    private static bool IsFrontmost(int processId)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var app = MacNative.AXUIElementCreateApplication(processId);
        if (app == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            _ = MacNative.AXUIElementSetMessagingTimeout(app, AppTimeoutSeconds);
            return MacAdapter.ReadBool(app, "AXFrontmost") == true;
        }
        finally
        {
            MacNative.CFRelease(app);
        }
    }
}
