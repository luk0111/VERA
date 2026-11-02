using Vera.Input.Platform;

namespace Vera.Input.Session;

internal enum SessionState
{
    /// <summary>Someone is logged in and the session is connected to a screen.</summary>
    Active,

    /// <summary>The session exists but isn't shown anywhere (e.g. an RDP client into this machine disconnected).</summary>
    NotActive,
}

internal enum InputDesktop
{
    /// <summary>The normal desktop: input goes to applications.</summary>
    Default,

    /// <summary>The lock screen, a UAC prompt, Ctrl+Alt+Del or the screen saver. Applications can't receive input.</summary>
    Other,
}

/// <param name="Handle">Identifies the window; only compared, never stored.</param>
internal sealed record WindowState(nint Handle, ScreenRect Bounds, bool IsMinimized);

/// <summary>Asks Windows the facts the session guard decides on. Kept apart so the decisions can be tested.</summary>
internal interface ISessionProbe
{
    SessionState GetSessionState();

    InputDesktop GetInputDesktop();

    /// <summary>Visible top-level windows with this window class, e.g. <c>TscShellContainerClass</c> for mstsc.</summary>
    IReadOnlyList<WindowState> FindWindows(string windowClass);

    nint GetForegroundWindow();
}