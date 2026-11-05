using Microsoft.Extensions.Logging.Abstractions;

using Vera.Input.Capture;
using Vera.Input.Coordinates;
using Vera.Input.Injection;
using Vera.Input.Options;
using Vera.Input.Platform;
using Vera.Input.Session;
using Vera.Input.Tests.Injection;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace Vera.Input.Tests;

public sealed class DesktopScreenTests
{
    private static readonly MonitorInfo Monitor = new(1, @"\\.\DISPLAY2", new ScreenRect(1920, 0, 1920, 1080), false, 96, 100, 60);
    private static readonly DateTimeOffset Time = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly IDisplayCheck _displayCheck = Substitute.For<IDisplayCheck>();
    private readonly ISessionGuard _guard = Substitute.For<ISessionGuard>();
    private readonly IScreenGrabber _grabber = Substitute.For<IScreenGrabber>();
    private readonly IFrameValidator _validator = Substitute.For<IFrameValidator>();
    private readonly IFrameEncoder _encoder = Substitute.For<IFrameEncoder>();
    private readonly RecordingSink _sink = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DesktopScreenTests()
    {
        _displayCheck.Run().Returns(new DisplayReport(new DisplaySnapshot(Monitor.Bounds, [Monitor], false), Monitor));
        _grabber.Grab(Arg.Any<ScreenRect>()).Returns(call => new RawFrame(call.Arg<ScreenRect>(), [], Time));
        _encoder.Encode(Arg.Any<RawFrame>(), Arg.Any<double>()).Returns(new EncodedImage([1, 2, 3], 1920, 1080));
    }

    [Fact]
    public async Task A_screenshot_grabs_the_capture_monitor_and_returns_the_encoded_frame()
    {
        var frame = await Screen().ScreenshotAsync(ct: Ct);

        _grabber.Received(1).Grab(Monitor.Bounds);
        frame.ShouldBe(new ScreenFrame(1920, 1080, 1.0, 1920, 1080, frame.Png, Time, 1));
        frame.Png.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task An_area_screenshot_grabs_that_area_at_full_size_whatever_the_default_scale()
    {
        _encoder.Encode(Arg.Any<RawFrame>(), 1.0).Returns(new EncodedImage([1], 640, 480));

        var frame = await Screen(new CaptureOptions { DefaultScale = 0.5 }).ScreenshotAreaAsync(2000, 100, 640, 480, Ct);

        _grabber.Received(1).Grab(new ScreenRect(2000, 100, 640, 480));
        frame.ShouldBe(new ScreenFrame(640, 480, 1.0, 640, 480, frame.Png, Time, 1));
    }

    [Fact]
    public async Task An_area_screenshot_gets_the_same_checks_as_a_full_one()
    {
        var area = new ScreenRect(2000, 100, 640, 480);

        await Screen().ScreenshotAreaAsync(2000, 100, 640, 480, Ct);

        Received.InOrder(() =>
        {
            _guard.EnsureReady(area);
            _grabber.Grab(area);
            _validator.Validate(Arg.Is<RawFrame>(f => f.Bounds == area));
        });
    }

    [Fact]
    public async Task After_an_area_screenshot_clicks_are_mapped_with_that_area()
    {
        _encoder.Encode(Arg.Any<RawFrame>(), 1.0).Returns(new EncodedImage([1], 640, 480));
        var screen = Screen();

        await screen.ScreenshotAreaAsync(2000, 100, 640, 480, Ct);

        screen.CurrentGeometry().ShouldBe(new FrameGeometry(640, 480, new ScreenRect(2000, 100, 640, 480), Monitor.Bounds));
    }

    [Fact]
    public void Before_the_first_screenshot_clicks_are_mapped_at_the_configured_scale()
    {
        var geometry = Screen(new CaptureOptions { DefaultScale = 0.5 }).CurrentGeometry();

        geometry.ShouldBe(new FrameGeometry(960, 540, Monitor.Bounds, Monitor.Bounds));
    }

    [Fact]
    public async Task Clicks_are_mapped_with_the_latest_screenshot()
    {
        _encoder.Encode(Arg.Any<RawFrame>(), 0.5).Returns(new EncodedImage([1], 960, 540));
        var screen = Screen();

        await screen.ScreenshotAsync(0.5, Ct);
        screen.CurrentGeometry().ImageWidth.ShouldBe(960);

        await screen.ScreenshotAsync(1.0, Ct);
        screen.CurrentGeometry().ImageWidth.ShouldBe(1920);
    }

    [Fact]
    public async Task The_sequence_grows_by_one_per_screenshot()
    {
        var screen = Screen();

        var first = await screen.ScreenshotAsync(ct: Ct);
        var second = await screen.ScreenshotAsync(ct: Ct);

        (first.Sequence, second.Sequence).ShouldBe((1, 2));
    }

    [Fact]
    public async Task The_display_is_checked_once_on_first_use()
    {
        var screen = Screen();
        _displayCheck.DidNotReceive().Run();

        await screen.ScreenshotAsync(ct: Ct);
        await screen.ScreenshotAsync(ct: Ct);

        _displayCheck.Received(1).Run();
    }

    [Fact]
    public async Task Without_a_scale_the_configured_default_is_used()
    {
        await Screen(new CaptureOptions { DefaultScale = 0.5 }).ScreenshotAsync(ct: Ct);

        _encoder.Received(1).Encode(Arg.Any<RawFrame>(), 0.5);
    }

    [Fact]
    public async Task A_scale_passed_by_the_caller_wins()
    {
        await Screen(new CaptureOptions { DefaultScale = 0.5 }).ScreenshotAsync(1.0, Ct);

        _encoder.Received(1).Encode(Arg.Any<RawFrame>(), 1.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    public async Task An_invalid_scale_throws(double scale)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Screen().ScreenshotAsync(scale, Ct));
    }

    [Fact]
    public async Task A_failing_display_check_is_retried_on_the_next_call()
    {
        var report = _displayCheck.Run();
        _displayCheck.Run().Returns(_ => throw new DisplayMismatchException("no monitor"), _ => report);
        var screen = Screen();

        await Should.ThrowAsync<DisplayMismatchException>(() => screen.ScreenshotAsync(ct: Ct));
        (await screen.ScreenshotAsync(ct: Ct)).Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task A_rejected_frame_is_not_encoded_and_uses_no_sequence_number()
    {
        var black = true;
        _validator.When(v => v.Validate(Arg.Any<RawFrame>())).Do(_ =>
        {
            if (black)
            {
                throw new ScreenUnavailableException(ScreenUnavailableReason.BlackFrame, "black");
            }
        });
        var screen = Screen();

        var e = await Should.ThrowAsync<ScreenUnavailableException>(() => screen.ScreenshotAsync(ct: Ct));

        e.Reason.ShouldBe(ScreenUnavailableReason.BlackFrame);
        _encoder.DidNotReceive().Encode(Arg.Any<RawFrame>(), Arg.Any<double>());
        black = false;
        (await screen.ScreenshotAsync(ct: Ct)).Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task The_session_guard_runs_before_the_grab_with_the_capture_monitor()
    {
        await Screen().ScreenshotAsync(ct: Ct);

        Received.InOrder(() =>
        {
            _guard.EnsureReady(Monitor.Bounds);
            _grabber.Grab(Monitor.Bounds);
        });
    }

    [Fact]
    public async Task When_the_guard_refuses_nothing_is_grabbed()
    {
        _guard.When(g => g.EnsureReady(Arg.Any<ScreenRect>()))
            .Do(_ => throw new ScreenUnavailableException(ScreenUnavailableReason.RdpWindowMinimized, "minimized"));

        var e = await Should.ThrowAsync<ScreenUnavailableException>(() => Screen().ScreenshotAsync(ct: Ct));

        e.Reason.ShouldBe(ScreenUnavailableReason.RdpWindowMinimized);
        _grabber.DidNotReceive().Grab(Arg.Any<ScreenRect>());
    }

    // The monitor is the whole virtual desktop here, so its centre (2880,540) is (32768,32768) in mouse units.
    [Fact]
    public async Task A_click_lands_on_the_screen_pixel_of_the_screenshot_point()
    {
        await Screen(new CaptureOptions { DefaultScale = 0.5 }).ClickAsync(480, 270, MouseButton.Right, 2, Ct);

        _sink.Events.ShouldBe([
            InputEvent.MouseMove(new NormalizedPoint(32768, 32768)),
            InputEvent.MouseDown(MouseButton.Right),
            InputEvent.MouseUp(MouseButton.Right),
            InputEvent.MouseDown(MouseButton.Right),
            InputEvent.MouseUp(MouseButton.Right),
        ]);
    }

    [Fact]
    public async Task A_move_goes_to_the_screen_pixel_of_the_screenshot_point()
    {
        await Screen().MoveAsync(0, 0, Ct);

        _sink.Events.ShouldBe([InputEvent.MouseMove(new NormalizedPoint(0, 0))]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1920, 0)]
    [InlineData(0, 1080)]
    public async Task A_point_outside_the_screenshot_throws_and_sends_nothing(int x, int y)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Screen().ClickAsync(x, y, ct: Ct));

        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_guard_refuses_no_input_is_sent()
    {
        _guard.When(g => g.EnsureReady(Arg.Any<ScreenRect>()))
            .Do(_ => throw new ScreenUnavailableException(ScreenUnavailableReason.WorkstationLocked, "locked"));

        await Should.ThrowAsync<ScreenUnavailableException>(() => Screen().ClickAsync(10, 10, ct: Ct));
        await Should.ThrowAsync<ScreenUnavailableException>(() => Screen().MoveAsync(10, 10, Ct));

        _sink.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task A_click_count_outside_1_to_3_throws(int clicks)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Screen().ClickAsync(10, 10, clicks: clicks, ct: Ct));

        _guard.DidNotReceive().EnsureReady(Arg.Any<ScreenRect>());
    }

    private DesktopScreen Screen(CaptureOptions? capture = null)
    {
        var options = MsOptions.Create(new VeraInputOptions
        {
            Capture = capture ?? new CaptureOptions(),
            Delays = new DelayOptions { AfterMove = 0, MouseDownUp = 0, BetweenClicks = 0, AfterClick = 0 },
        });
        return new(
            _displayCheck,
            _guard,
            _grabber,
            _validator,
            _encoder,
            new MouseDriver(_sink, options, TimeProvider.System),
            options,
            NullLogger<DesktopScreen>.Instance);
    }
}