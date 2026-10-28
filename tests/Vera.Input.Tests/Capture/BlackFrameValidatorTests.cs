using Vera.Input.Capture;
using Vera.Input.Options;
using Vera.Input.Platform;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace Vera.Input.Tests.Capture;

public sealed class BlackFrameValidatorTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    [Fact]
    public void An_all_black_frame_is_rejected()
    {
        var e = Should.Throw<ScreenUnavailableException>(() => Validate(Frame(0, 0, 0)));

        e.Reason.ShouldBe(ScreenUnavailableReason.BlackFrame);
        e.Message.ShouldContain("black");
    }

    [Fact]
    public void A_black_frame_with_only_a_mouse_cursor_is_rejected()
    {
        var frame = Frame(0, 0, 0);
        Fill(frame, x: 900, y: 500, width: 32, height: 32, 255, 255, 255);

        Should.Throw<ScreenUnavailableException>(() => Validate(frame));
    }

    [Fact]
    public void A_dark_theme_is_not_black()
    {
        // VS Code / Windows dark backgrounds are dark grey, e.g. #1E1E1E.
        Should.NotThrow(() => Validate(Frame(30, 30, 30)));
    }

    [Fact]
    public void A_black_background_with_some_text_is_not_black()
    {
        // A black console with a few lines of text: about 2% light pixels.
        var frame = Frame(0, 0, 0);
        for (var line = 0; line < 10; line++)
        {
            Fill(frame, x: 20, y: 40 + (line * 30), width: 800, height: 6, 200, 200, 200);
        }

        Should.NotThrow(() => Validate(frame));
    }

    [Fact]
    public void A_white_frame_is_not_black()
    {
        Should.NotThrow(() => Validate(Frame(255, 255, 255)));
    }

    [Fact]
    public void Pure_blue_counts_by_luminance_not_by_channel()
    {
        // Blue (0,0,255) has a luminance of 29, so it's not black even though red and green are 0.
        Should.NotThrow(() => Validate(Frame(0, 0, 255)));
    }

    [Fact]
    public void The_threshold_and_ratio_come_from_the_options()
    {
        var frame = Frame(30, 30, 30);

        Should.Throw<ScreenUnavailableException>(() => Validate(frame, new CaptureOptions { BlackLuminanceThreshold = 31 }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void BlackRatio_counts_the_sampled_black_pixels(int stride)
    {
        // Left half black, right half white.
        var frame = Frame(255, 255, 255);
        Fill(frame, x: 0, y: 0, width: Width / 2, height: Height, 0, 0, 0);

        BlackFrameValidator.BlackRatio(frame, stride, threshold: 10).ShouldBe(0.5, tolerance: 0.01);
    }

    private static void Validate(RawFrame frame, CaptureOptions? capture = null) =>
        new BlackFrameValidator(MsOptions.Create(new VeraInputOptions { Capture = capture ?? new CaptureOptions() })).Validate(frame);

    private static RawFrame Frame(byte red, byte green, byte blue)
    {
        var frame = new RawFrame(new ScreenRect(0, 0, Width, Height), new byte[Width * Height * 4], DateTimeOffset.UnixEpoch);
        Fill(frame, 0, 0, Width, Height, red, green, blue);
        return frame;
    }

    private static void Fill(RawFrame frame, int x, int y, int width, int height, byte red, byte green, byte blue)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                var i = (row * frame.Stride) + (column * 4);
                frame.Bgra[i] = blue;
                frame.Bgra[i + 1] = green;
                frame.Bgra[i + 2] = red;
            }
        }
    }
}