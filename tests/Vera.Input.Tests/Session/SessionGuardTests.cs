using Vera.Input.Options;
using Vera.Input.Platform;
using Vera.Input.Session;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace Vera.Input.Tests.Session;

public sealed class SessionGuardTests
{
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1080);
    private static readonly WindowState FullScreenRdp = new(100, Monitor, IsMinimized: false);

    private readonly ISessionProbe _probe = Substitute.For<ISessionProbe>();

    public SessionGuardTests()
    {
        _probe.GetSessionState().Returns(SessionState.Active);
        _probe.GetInputDesktop().Returns(InputDesktop.Default);
        _probe.FindWindows("TscShellContainerClass").Returns([FullScreenRdp]);
        _probe.GetForegroundWindow().Returns(FullScreenRdp.Handle);
    }

    [Fact]
    public void A_full_screen_rdp_window_in_front_passes()
    {
        Should.NotThrow(() => Guard().EnsureReady(Monitor));
    }

    [Fact]
    public void A_disconnected_session_fails()
    {
        _probe.GetSessionState().Returns(SessionState.NotActive);

        Reason(() => Guard().EnsureReady(Monitor)).ShouldBe(ScreenUnavailableReason.NoInteractiveSession);
    }

    [Fact]
    public void A_locked_workstation_fails()
    {
        _probe.GetInputDesktop().Returns(InputDesktop.Other);

        Reason(() => Guard().EnsureReady(Monitor)).ShouldBe(ScreenUnavailableReason.WorkstationLocked);
    }

    [Fact]
    public void No_rdp_window_fails()
    {
        _probe.FindWindows("TscShellContainerClass").Returns([]);

        Reason(() => Guard().EnsureReady(Monitor)).ShouldBe(ScreenUnavailableReason.RdpWindowMissing);
    }

    [Fact]
    public void A_minimized_rdp_window_fails()
    {
        _probe.FindWindows("TscShellContainerClass").Returns([FullScreenRdp with { IsMinimized = true }]);
        _probe.GetForegroundWindow().Returns(999);

        Reason(() => Guard().EnsureReady(Monitor)).ShouldBe(ScreenUnavailableReason.RdpWindowMinimized);
    }

    [Fact]
    public void Another_window_in_front_fails()
    {
        _probe.GetForegroundWindow().Returns(999);

        Reason(() => Guard().EnsureReady(Monitor)).ShouldBe(ScreenUnavailableReason.RdpWindowNotForeground);
    }

    [Fact]
    public void Another_window_in_front_is_allowed_when_not_required()
    {
        _probe.GetForegroundWindow().Returns(999);

        Should.NotThrow(() => Guard(new RdpOptions { RequireForeground = false }).EnsureReady(Monitor));
    }

    [Fact]
    public void A_windowed_rdp_window_fails()
    {
        _probe.FindWindows("TscShellContainerClass").Returns([FullScreenRdp with { Bounds = new ScreenRect(100, 100, 1280, 720) }]);

        Reason(() => Guard().EnsureReady(Monitor)).ShouldBe(ScreenUnavailableReason.RdpWindowNotCoveringScreen);
    }

    [Fact]
    public void A_full_screen_window_on_the_other_monitor_does_not_cover_the_capture_monitor()
    {
        _probe.FindWindows("TscShellContainerClass").Returns([FullScreenRdp with { Bounds = new ScreenRect(1920, 0, 1920, 1080) }]);

        Reason(() => Guard().EnsureReady(Monitor)).ShouldBe(ScreenUnavailableReason.RdpWindowNotCoveringScreen);
    }

    [Fact]
    public void A_maximized_window_slightly_larger_than_the_monitor_covers_it()
    {
        // Maximized windows extend a few pixels past the monitor on each side.
        _probe.FindWindows("TscShellContainerClass").Returns([FullScreenRdp with { Bounds = new ScreenRect(-8, -8, 1936, 1096) }]);

        Should.NotThrow(() => Guard().EnsureReady(Monitor));
    }

    [Fact]
    public void With_several_rdp_windows_the_one_in_front_is_checked()
    {
        var hidden = new WindowState(50, Monitor, IsMinimized: true);
        _probe.FindWindows("TscShellContainerClass").Returns([hidden, FullScreenRdp]);

        Should.NotThrow(() => Guard().EnsureReady(Monitor));
    }

    [Fact]
    public void The_window_class_comes_from_the_options()
    {
        _probe.FindWindows("Notepad").Returns([FullScreenRdp]);

        Should.NotThrow(() => Guard(new RdpOptions { WindowClass = "Notepad" }).EnsureReady(Monitor));
    }

    [Fact]
    public void With_rdp_disabled_only_the_session_and_lock_are_checked()
    {
        _probe.FindWindows("TscShellContainerClass").Returns([]);
        var guard = Guard(new RdpOptions { Enabled = false });

        Should.NotThrow(() => guard.EnsureReady(Monitor));
        _probe.GetInputDesktop().Returns(InputDesktop.Other);
        Reason(() => guard.EnsureReady(Monitor)).ShouldBe(ScreenUnavailableReason.WorkstationLocked);
    }

    private SessionGuard Guard(RdpOptions? rdp = null) =>
        new(_probe, MsOptions.Create(new VeraInputOptions { Rdp = rdp ?? new RdpOptions() }));

    private static ScreenUnavailableReason Reason(Action action) => Should.Throw<ScreenUnavailableException>(action).Reason;
}