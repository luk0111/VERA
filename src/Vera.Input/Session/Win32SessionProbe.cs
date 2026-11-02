using Vera.Input.Platform;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RemoteDesktop;
using Windows.Win32.System.StationsAndDesktops;

namespace Vera.Input.Session;

internal sealed unsafe class Win32SessionProbe : ISessionProbe
{
    public SessionState GetSessionState()
    {
        if (!PInvoke.WTSQuerySessionInformation(
                HANDLE.Null, PInvoke.WTS_CURRENT_SESSION, WTS_INFO_CLASS.WTSConnectState, out var buffer, out _))
        {
            // Without Terminal Services info we can't tell; let the other checks decide.
            return SessionState.Active;
        }

        try
        {
            return *(WTS_CONNECTSTATE_CLASS*)buffer.Value == WTS_CONNECTSTATE_CLASS.WTSActive ? SessionState.Active : SessionState.NotActive;
        }
        finally
        {
            PInvoke.WTSFreeMemory(buffer.Value);
        }
    }

    public InputDesktop GetInputDesktop()
    {
        // On the lock screen the input desktop is "Winlogon", which a normal process isn't allowed to open.
        var desktop = PInvoke.OpenInputDesktop(0, false, DESKTOP_ACCESS_FLAGS.DESKTOP_READOBJECTS);
        if (desktop.IsNull)
        {
            return InputDesktop.Other;
        }

        try
        {
            Span<char> name = stackalloc char[64];
            name.Clear();
            fixed (char* pName = name)
            {
                uint needed = 0;
                if (!PInvoke.GetUserObjectInformation((HANDLE)desktop.Value, USER_OBJECT_INFORMATION_INDEX.UOI_NAME, pName, (uint)(name.Length * sizeof(char)), &needed))
                {
                    return InputDesktop.Other;
                }
            }

            var end = name.IndexOf('\0');
            return name[..(end < 0 ? name.Length : end)].SequenceEqual("Default") ? InputDesktop.Default : InputDesktop.Other;
        }
        finally
        {
            PInvoke.CloseDesktop(desktop);
        }
    }

    public IReadOnlyList<WindowState> FindWindows(string windowClass)
    {
        var found = new List<WindowState>();
        var buffer = new char[256];
        PInvoke.EnumWindows((hwnd, _) =>
        {
            if (!PInvoke.IsWindowVisible(hwnd))
            {
                return true;
            }

            int length;
            fixed (char* pBuffer = buffer)
            {
                length = PInvoke.GetClassName(hwnd, pBuffer, buffer.Length);
            }

            if (length > 0 && buffer.AsSpan(0, length).SequenceEqual(windowClass))
            {
                PInvoke.GetWindowRect(hwnd, out var rect);
                found.Add(new WindowState(
                    (nint)hwnd.Value,
                    new ScreenRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top),
                    PInvoke.IsIconic(hwnd)));
            }

            return true;
        }, default);

        return found;
    }

    public nint GetForegroundWindow() => (nint)PInvoke.GetForegroundWindow().Value;
}