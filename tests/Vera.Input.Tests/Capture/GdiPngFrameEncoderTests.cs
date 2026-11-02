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

    [Theory]
    [InlineData(0.5, 960, 540)]
    [InlineData(0.75, 1440, 810)]
    [InlineData(1.0 / 3, 640, 360)]
    [InlineData(0.6, 1152, 648)]
    public void A_scaled_png_has_the_rounded_size(double scale, int width, int height)
    {
        var image = new GdiPngFrameEncoder().Encode(Solid(1920, 1080, Color.Gray), scale);

        (image.Width, image.Height).ShouldBe((width, height));
        using var decoded = new Bitmap(new MemoryStream(image.Png));
        (decoded.Width, decoded.Height).ShouldBe((width, height));
    }

    [Fact]
    public void A_tiny_scale_still_gives_at_least_one_pixel()
    {
        var image = new GdiPngFrameEncoder().Encode(Solid(10, 4, Color.Gray), 0.01);

        (image.Width, image.Height).ShouldBe((1, 1));
    }

    [Fact]
    public void Scaling_keeps_the_colour_up_to_the_edges()
    {
        var color = Color.FromArgb(200, 120, 40);

        var image = new GdiPngFrameEncoder().Encode(Solid(200, 100, color), 0.5);

        using var decoded = new Bitmap(new MemoryStream(image.Png));
        (int X, int Y)[] points = [(0, 0), (99, 0), (0, 49), (99, 49), (50, 0), (0, 25), (50, 25)];
        foreach (var (x, y) in points)
        {
            decoded.GetPixel(x, y).ToArgb().ShouldBe(color.ToArgb(), $"pixel ({x},{y})");
        }
    }

    private static RawFrame Solid(int width, int height, Color color)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 1], bgra[i + 2]) = (color.B, color.G, color.R);
        }

        return new RawFrame(new ScreenRect(0, 0, width, height), bgra, DateTimeOffset.UnixEpoch);
    }
}