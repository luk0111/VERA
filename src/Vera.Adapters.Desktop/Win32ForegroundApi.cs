using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Vera.Adapters.Desktop;

internal sealed unsafe class Win32ForegroundApi : IForegroundApi
{
    public bool IsInFront(nint window)
    {
        var front = ProcessWindows.RootOwner(PInvoke.GetForegroundWindow());
        var ours = ProcessWindows.RootOwner(new HWND(window));
        _ = PInvoke.GetWindowThreadProcessId(front, out var frontProcess);
        _ = PInvoke.GetWindowThreadProcessId(ours, out var ourProcess);
        return Counts(front == ours, frontProcess == ourProcess, ProcessWindows.IsMainLike(front));
    }

    /// <summary>
    /// The app counts as in front when its own window, or a dialog it owns, is. Another window of the same process counts too (a popup, a menu),
    /// unless it is a main window of its own, such as a second Brave window: then the picked one must still be brought up (#44).
    /// </summary>
    internal static bool Counts(bool sameRootWindow, bool sameProcess, bool frontIsMainLike) =>
        sameRootWindow || (sameProcess && !frontIsMainLike);

    public void Request(nint window) => PInvoke.SetForegroundWindow(new HWND(window));

    // A mouse event with no movement and no button: nothing happens on screen.
    public void SendEmptyInput() => _ = PInvoke.SendInput([new INPUT { type = INPUT_TYPE.INPUT_MOUSE }], sizeof(INPUT));

    public void RequestAttached(nint window)
    {
        var inFront = PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), out _);
        var current = PInvoke.GetCurrentThreadId();

        // Attaching and detaching must happen on one thread, so nothing is awaited in between.
        var attached = inFront != 0 && inFront != current && PInvoke.AttachThreadInput(current, inFront, true);
        try
        {
            PInvoke.BringWindowToTop(new HWND(window));
            PInvoke.SetForegroundWindow(new HWND(window));
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(current, inFront, false);
            }
        }
    }
}
