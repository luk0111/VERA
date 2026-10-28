using Microsoft.Extensions.Logging.Abstractions;

using Vera.Input.Capture;
using Vera.Input.Options;
using Vera.Input.Platform;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace Vera.Input.Tests;

public sealed class DesktopScreenTests
{
    private static readonly MonitorInfo Monitor = new(1, @"\\.\DISPLAY2", new ScreenRect(1920, 0, 1920, 1080), false, 96, 100, 60);
    private static readonly DateTimeOffset Time = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly IDisplayCheck _displayCheck = Substitute.For<IDisplayCheck>();
    private readonly IScreenGrabber _grabber = Substitute.For<IScreenGrabber>();
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

    private DesktopScreen Screen(CaptureOptions? capture = null) => new(
        _displayCheck,
        _grabber,
        _encoder,
        MsOptions.Create(new VeraInputOptions { Capture = capture ?? new CaptureOptions() }),
        NullLogger<DesktopScreen>.Instance);
}