namespace Vera.Adapters.Mac;

/// <summary>
/// Which window of each app the tester picked, so VERA stays with it when the app has several (#44).
/// Remembers the window itself, by macOS's window number: its title changes with every page or tab, and two windows can have the same one.
/// </summary>
public sealed class MacPickedWindows
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, (long Window, HashSet<long> Others)> _picked = [];

    /// <param name="window">The window the app picker showed.</param>
    /// <param name="open">All of the app's windows at that moment, on any desktop.</param>
    internal void Remember(int processId, long window, IEnumerable<long> open)
    {
        lock (_lock)
        {
            _picked[processId] = (window, open.Where(w => w != window).ToHashSet());
        }
    }

    /// <summary>
    /// The window to read and act in. The picked one, unless the app has since opened another and made it its main window: a dialog, its settings,
    /// a new window from a click. Another window that was already open is not what the tester picked, even when it is the main one now.
    /// </summary>
    /// <param name="main">The app's main window now; null when macOS names none.</param>
    /// <param name="open">The app's windows now.</param>
    /// <returns><paramref name="main"/> when nothing was picked or the picked window was closed.</returns>
    internal long? Choose(int processId, long? main, IReadOnlyCollection<long> open)
    {
        lock (_lock)
        {
            return _picked.TryGetValue(processId, out var picked) && open.Contains(picked.Window) && (main is not { } now || picked.Others.Contains(now))
                ? picked.Window
                : main;
        }
    }

    /// <summary>Whether the window was already open, beside the picked one, when the app was picked. False once the picked window was closed.</summary>
    internal bool IsAnother(int processId, long window, IReadOnlyCollection<long> open)
    {
        lock (_lock)
        {
            return _picked.TryGetValue(processId, out var picked) && open.Contains(picked.Window) && picked.Others.Contains(window);
        }
    }
}
