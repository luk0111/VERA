using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Vera.Adapters.Desktop;

internal sealed unsafe class Win32ForegroundApi : IForegroundApi
{
    public bool IsInFront(nint window)
    {
        _ = PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), out var foreground);
        _ = PInvoke.GetWindowThreadProcessId(new HWND(window), out var app);
        return foreground == app;
    }

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
