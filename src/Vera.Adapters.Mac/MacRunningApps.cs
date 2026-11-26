using Vera.Core;

namespace Vera.Adapters.Mac;

/// <summary>One window, as macOS's window list reports it.</summary>
/// <param name="Number">Names the window as long as it is open, whatever its title.</param>
internal sealed record MacWindowInfo(int ProcessId, string OwnerName, string Title, long Layer, long Number = 0);

/// <summary>Lists the running apps with a window. It needs no permission, but window titles stay empty until VERA has macOS's screen recording permission.</summary>
public sealed class MacRunningApps(MacPickedWindows? picked = null) : IRunningAppSource, IPickedWindows
{
    /// <summary>The layer of normal app windows. The menu bar, Dock and system overlays sit on others.</summary>
    private const long NormalWindowLayer = 0;

    // The window each app's row showed in the last list.
    private Dictionary<int, long> _shown = [];

    public IReadOnlyList<RunningAppInfo> List()
    {
        var windows = Windows(MacNative.WindowListOnScreenOnly);
        _shown = Shown(windows);
        return Pick(windows, Environment.ProcessId);
    }

    public void Picked(int processId)
    {
        if (picked is not null && _shown.TryGetValue(processId, out var window))
        {
            // Also minimized windows and those on other desktops: they were there before, so they are not something the app opened later.
            picked.Remember(processId, window, Windows(MacNative.WindowListAll).Where(w => w.ProcessId == processId).Select(w => w.Number));
        }
    }

    public string? TitleOf(int processId) => TitleOf(processId, Windows(MacNative.WindowListOnScreenOnly), picked);

    /// <summary>The first title of the app's windows, front to back, without those the tester didn't pick.</summary>
    internal static string? TitleOf(int processId, IEnumerable<MacWindowInfo> windows, MacPickedWindows? picked)
    {
        var own = windows.Where(w => w.ProcessId == processId && w.Layer == NormalWindowLayer).ToList();
        var open = own.Select(w => w.Number).ToList();
        return own.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.Title) && picked?.IsAnother(processId, w.Number, open) != true)?.Title;
    }

    private static List<MacWindowInfo> Windows(uint which)
    {
        var windows = new List<MacWindowInfo>();

        // Builds that aren't for Windows also run on Linux, which has no adapter yet.
        if (!OperatingSystem.IsMacOS())
        {
            return windows;
        }

        var list = MacNative.CGWindowListCopyWindowInfo(which | MacNative.WindowListExcludeDesktopElements, 0);
        if (list == IntPtr.Zero)
        {
            return windows;
        }

        try
        {
            var count = MacNative.CFArrayGetCount(list);
            for (nint i = 0; i < count; i++)
            {
                var window = MacNative.CFArrayGetValueAtIndex(list, i);
                var processId = MacNative.ReadNumber(Get(window, "kCGWindowOwnerPID"));
                var layer = MacNative.ReadNumber(Get(window, "kCGWindowLayer"));
                if (processId is { } id && layer is { } l)
                {
                    windows.Add(new MacWindowInfo(
                        (int)id,
                        MacNative.ReadString(Get(window, "kCGWindowOwnerName")) ?? "",
                        MacNative.ReadString(Get(window, "kCGWindowName")) ?? "",
                        l,
                        MacNative.ReadNumber(Get(window, "kCGWindowNumber")) ?? 0));
                }
            }
        }
        finally
        {
            MacNative.CFRelease(list);
        }

        return windows;
    }

    /// <summary>The window whose title an app's row shows: its first with a title, else its first. The list comes front to back.</summary>
    internal static Dictionary<int, long> Shown(IEnumerable<MacWindowInfo> windows) =>
        windows
            .Where(w => w.Layer == NormalWindowLayer && w.Number != 0)
            .GroupBy(w => w.ProcessId)
            .ToDictionary(app => app.Key, app => (app.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.Title)) ?? app.First()).Number);

    /// <summary>Keeps one row per app, with the first window that has a title. The list comes front to back, so that is the app's front window.</summary>
    internal static IReadOnlyList<RunningAppInfo> Pick(IEnumerable<MacWindowInfo> windows, int ownProcessId) =>
        windows
            .Where(w => w.Layer == NormalWindowLayer && w.ProcessId != ownProcessId && !string.IsNullOrWhiteSpace(w.OwnerName))
            .GroupBy(w => w.ProcessId)
            .Select(app => new RunningAppInfo(
                app.Key,
                app.First().OwnerName,
                app.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.Title))?.Title ?? ""))
            .ToList();

    private static IntPtr Get(IntPtr window, string key) => MacNative.CFDictionaryGetValue(window, MacNative.Key(key));
}
