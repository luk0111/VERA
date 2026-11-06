using Vera.Core;

namespace Vera.Adapters.Mac;

/// <summary>One window, as macOS's window list reports it.</summary>
internal sealed record MacWindowInfo(int ProcessId, string OwnerName, string Title, long Layer);

/// <summary>Lists the running apps with a window. It needs no permission, but window titles stay empty until VERA has macOS's screen recording permission.</summary>
public sealed class MacRunningApps : IRunningAppSource
{
    /// <summary>The layer of normal app windows. The menu bar, Dock and system overlays sit on others.</summary>
    private const long NormalWindowLayer = 0;

    public IReadOnlyList<RunningAppInfo> List()
    {
        // Builds that aren't for Windows also run on Linux, which has no adapter yet.
        if (!OperatingSystem.IsMacOS())
        {
            return [];
        }

        var windows = new List<MacWindowInfo>();
        var list = MacNative.CGWindowListCopyWindowInfo(MacNative.WindowListOnScreenOnly | MacNative.WindowListExcludeDesktopElements, 0);
        if (list == IntPtr.Zero)
        {
            return [];
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
                        l));
                }
            }
        }
        finally
        {
            MacNative.CFRelease(list);
        }

        return Pick(windows, Environment.ProcessId);
    }

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
