using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Vera.Adapters.Desktop;

/// <summary>
/// The visible top-level windows of a process. A browser such as Brave runs all its windows in one process, and
/// <see cref="System.Diagnostics.Process.MainWindowHandle"/> is just one of them, not always the one the tester picked (#44).
/// </summary>
internal static class ProcessWindows
{
    /// <summary>
    /// The process's windows the tester would pick: visible, with a title, owned by no other window (not a dialog or a tool window)
    /// and not cloaked (a suspended Store app, a window on another virtual desktop).
    /// </summary>
    internal static List<(nint Handle, string Title)> AppWindows(int processId) =>
        Of(processId).Where(w => RootOwner(new HWND(w.Handle)) == new HWND(w.Handle) && !IsCloaked(new HWND(w.Handle))).ToList();

    /// <summary>Whether <paramref name="window"/> still exists and belongs to the process. A handle can be reused once its window is closed.</summary>
    internal static bool Belongs(nint window, int processId)
    {
        if (window == 0 || !PInvoke.IsWindow(new HWND(window)))
        {
            return false;
        }

        _ = PInvoke.GetWindowThreadProcessId(new HWND(window), out var owner);
        return owner == (uint)processId;
    }

    /// <summary>The process's window titled <paramref name="title"/>, or <paramref name="main"/> when none is (e.g. the title changed since it was picked).</summary>
    internal static nint Pick(IReadOnlyList<(nint Handle, string Title)> windows, string title, nint main)
    {
        foreach (var (handle, windowTitle) in windows)
        {
            if (string.Equals(windowTitle, title, StringComparison.Ordinal))
            {
                return handle;
            }
        }

        return main;
    }

    /// <summary>The process's visible top-level windows that have a title, in the system's order (front first).</summary>
    internal static List<(nint Handle, string Title)> Of(int processId)
    {
        var windows = new List<(nint, string)>();
        PInvoke.EnumWindows(
            (window, lParam) =>
            {
                _ = PInvoke.GetWindowThreadProcessId(window, out var owner);
                if (owner == (uint)processId && PInvoke.IsWindowVisible(window) && TitleOf(window) is { Length: > 0 } title)
                {
                    windows.Add((window, title));
                }

                return true;
            },
            default);
        return windows;
    }

    /// <summary>True for a window the tester would call a window of its own: visible, with a title. Popups, menus and tooltips have none.</summary>
    internal static bool IsMainLike(HWND window) => PInvoke.IsWindowVisible(window) && PInvoke.GetWindowTextLength(window) > 0;

    /// <summary>The window that owns <paramref name="window"/> at the top: the app's main window for its dialogs, the window itself otherwise.</summary>
    internal static HWND RootOwner(HWND window) => PInvoke.GetAncestor(window, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);

    private static unsafe bool IsCloaked(HWND window)
    {
        int cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded && cloaked != 0;
    }

    private static string TitleOf(HWND window)
    {
        var length = PInvoke.GetWindowTextLength(window);
        if (length <= 0)
        {
            return "";
        }

        Span<char> buffer = stackalloc char[Math.Min(length, 1024) + 1];
        var copied = PInvoke.GetWindowText(window, buffer);
        return new string(buffer[..copied]);
    }
}
