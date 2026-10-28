using Microsoft.Extensions.Options;

using Vera.Input.Options;
using Vera.Input.Platform;

namespace Vera.Input.Session;

/// <summary>Runs before every screenshot and every input action, so nothing is captured from or sent to the wrong place.</summary>
public interface ISessionGuard
{
    /// <param name="captureBounds">The monitor screenshots come from; the RDP window has to cover it.</param>
    /// <exception cref="ScreenUnavailableException">The screen can't be used right now; the reason says why.</exception>
    void EnsureReady(ScreenRect captureBounds);
}

/// <summary>
/// Always checks that this session is shown on a screen and that the normal desktop (not the lock screen) has the input.
/// With <c>Rdp.Enabled</c> it also checks that an RDP window exists, isn't minimized, is in front (<c>Rdp.RequireForeground</c>)
/// and covers the capture monitor. A minimized RDP window doesn't give a black frame (you see the local desktop), so the
/// window itself has to be checked.
/// </summary>
internal sealed class SessionGuard(ISessionProbe probe, IOptions<VeraInputOptions> options) : ISessionGuard
{
    public void EnsureReady(ScreenRect captureBounds)
    {
        if (probe.GetSessionState() != SessionState.Active)
        {
            throw new ScreenUnavailableException(
                ScreenUnavailableReason.NoInteractiveSession,
                "This Windows session isn't shown on a screen (it's disconnected), so there is nothing to capture or click.");
        }

        if (probe.GetInputDesktop() != InputDesktop.Default)
        {
            throw new ScreenUnavailableException(
                ScreenUnavailableReason.WorkstationLocked,
                "The VERA machine is locked, or the screen saver, a UAC prompt or Ctrl+Alt+Del is showing.");
        }

        var rdp = options.Value.Rdp;
        if (!rdp.Enabled)
        {
            return;
        }

        var windows = probe.FindWindows(rdp.WindowClass);
        if (windows.Count == 0)
        {
            throw new ScreenUnavailableException(
                ScreenUnavailableReason.RdpWindowMissing,
                $"No RDP window is open (window class '{rdp.WindowClass}'). Start the RDP connection in full screen.");
        }

        var foreground = probe.GetForegroundWindow();
        var window = windows.FirstOrDefault(w => w.Handle == foreground)
            ?? windows.FirstOrDefault(w => !w.IsMinimized)
            ?? windows[0];

        if (window.IsMinimized)
        {
            throw new ScreenUnavailableException(
                ScreenUnavailableReason.RdpWindowMinimized,
                "The RDP window is minimized. Screenshots would show the local desktop and clicks would land there.");
        }

        if (rdp.RequireForeground && window.Handle != foreground)
        {
            throw new ScreenUnavailableException(
                ScreenUnavailableReason.RdpWindowNotForeground,
                "Another window is in front of the RDP window, so input would go there. Click into the RDP window first.");
        }

        if (!Covers(window.Bounds, captureBounds))
        {
            throw new ScreenUnavailableException(
                ScreenUnavailableReason.RdpWindowNotCoveringScreen,
                $"The RDP window ({window.Bounds}) doesn't fill the capture monitor ({captureBounds}). Switch it to full screen on that monitor.");
        }
    }

    private static bool Covers(ScreenRect outer, ScreenRect inner) =>
        outer.Left <= inner.Left
        && outer.Top <= inner.Top
        && outer.Left + outer.Width >= inner.Left + inner.Width
        && outer.Top + outer.Height >= inner.Top + inner.Height;
}