using System.Drawing;

using Vera.Input.Capture;
using Vera.Input.Platform;

namespace Vera.Input.Tests.Capture;

public sealed class GdiPngFrameEncoderTests
{
    [Fact]
    public void The_png_has_the_frame_size_and_pixels()
    {
        // 2×2: red, green / blue, white. Each pixel is blue, green, red, unused.
        byte[] bgra =
        [
            0, 0, 255, 0, 0, 255, 0, 0,
            255, 0, 0, 0, 255, 255, 255, 0,
        ];
        var frame = new RawFrame(new ScreenRect(100, 200, 2, 2), bgra, DateTimeOffset.UnixEpoch);

        var image = new GdiPngFrameEncoder().Encode(frame, 1.0);

        (image.Width, image.Height).ShouldBe((2, 2));
        using var decoded = new Bitmap(new MemoryStream(image.Png));
        (decoded.Width, decoded.Height).ShouldBe((2, 2));
        decoded.GetPixel(0, 0).ToArgb().ShouldBe(Color.FromArgb(255, 0, 0).ToArgb());
        decoded.GetPixel(1, 0).ToArgb().ShouldBe(Color.FromArgb(0, 255, 0).ToArgb());
        decoded.GetPixel(0, 1).ToArgb().ShouldBe(Color.FromArgb(0, 0, 255).ToArgb());
        decoded.GetPixel(1, 1).ToArgb().ShouldBe(Color.FromArgb(255, 255, 255).ToArgb());
    }

    [Fact]
    public void Scales_other_than_1_are_not_supported_yet()
    {
        var frame = new RawFrame(new ScreenRect(0, 0, 2, 2), new byte[16], DateTimeOffset.UnixEpoch);

        Should.Throw<NotSupportedException>(() => new GdiPngFrameEncoder().Encode(frame, 0.5));
    }
}