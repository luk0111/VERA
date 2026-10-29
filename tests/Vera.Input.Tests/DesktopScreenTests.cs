using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Vera.Input.Capture;
using Vera.Input.Coordinates;
using Vera.Input.Options;
using Vera.Input.Platform;
using Vera.Input.Session;

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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DesktopScreenTests()
    {
        _displayCheck.Run().Returns(new DisplayReport(new DisplaySnapshot(Monitor.Bounds, [Monitor], false), Monitor, []));
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

    [Fact]
    public async Task Identical_frames_warn_once_when_the_limit_is_reached()
    {
        var logger = new RecordingLogger();
        var screen = Screen(new CaptureOptions { StaleWarningAfterIdenticalFrames = 2 }, logger);

        for (var i = 0; i < 5; i++)
        {
            await screen.ScreenshotAsync(ct: Ct);
        }

        logger.Warnings.ShouldHaveSingleItem().ShouldContain("last 3 screenshots (up to 3) are identical");
    }

    [Fact]
    public async Task A_changed_frame_resets_the_identical_count()
    {
        var logger = new RecordingLogger();
        var screen = Screen(new CaptureOptions { StaleWarningAfterIdenticalFrames = 2 }, logger);
        var calls = 0;
        _grabber.Grab(Arg.Any<ScreenRect>()).Returns(call => new RawFrame(call.Arg<ScreenRect>(), [(byte)(calls++ / 2)], Time));

        for (var i = 0; i < 6; i++)
        {
            await screen.ScreenshotAsync(ct: Ct);
        }

        logger.Warnings.ShouldBeEmpty();
    }

    private DesktopScreen Screen(CaptureOptions? capture = null, ILogger<DesktopScreen>? logger = null) => new(
        _displayCheck,
        _guard,
        _grabber,
        _validator,
        _encoder,
        MsOptions.Create(new VeraInputOptions { Capture = capture ?? new CaptureOptions() }),
        logger ?? NullLogger<DesktopScreen>.Instance);

    private sealed class RecordingLogger : ILogger<DesktopScreen>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}