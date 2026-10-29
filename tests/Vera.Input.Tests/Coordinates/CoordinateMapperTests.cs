using Vera.Input.Coordinates;
using Vera.Input.Platform;

namespace Vera.Input.Tests.Coordinates;

public sealed class CoordinateMapperTests
{
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1080);

    // One 1920×1080 monitor; the scale is given as a fraction (num/den) so 1/3 stays exact in the test data.
    [Theory]
    [InlineData(1, 1, 0, 0, 0, 0)]
    [InlineData(1, 1, 1919, 1079, 1919, 1079)]
    [InlineData(1, 2, 0, 0, 0, 0)]
    [InlineData(1, 2, 959, 539, 1918, 1078)]
    [InlineData(1, 3, 0, 0, 1, 1)]
    [InlineData(1, 3, 639, 359, 1918, 1078)]
    [InlineData(3, 4, 0, 0, 0, 0)]
    [InlineData(3, 4, 1439, 809, 1919, 1079)]
    [InlineData(1, 2, 480, 270, 960, 540)]
    [InlineData(1, 3, 320, 180, 961, 541)]
    public void An_image_pixel_maps_to_the_screen_pixel_under_its_centre(int num, int den, int x, int y, int screenX, int screenY)
    {
        var frame = FrameGeometry.ForScale(Monitor, Monitor, (double)num / den);

        CoordinateMapper.ImageToScreen(frame, new ImagePoint(x, y)).ShouldBe(new ScreenPoint(screenX, screenY));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.75)]
    [InlineData(0.6)]
    [InlineData(0.5)]
    [InlineData(1.0 / 3)]
    [InlineData(0.1)]
    public void Every_image_pixel_survives_a_round_trip(double scale)
    {
        var frame = FrameGeometry.ForScale(Monitor, Monitor, scale);

        for (var x = 0; x < frame.ImageWidth; x++)
        {
            var screen = CoordinateMapper.ImageToScreen(frame, new ImagePoint(x, frame.ImageHeight / 2));
            CoordinateMapper.ScreenToImage(frame, screen).ShouldBe(new ImagePoint(x, frame.ImageHeight / 2), $"x {x}");
        }

        for (var y = 0; y < frame.ImageHeight; y++)
        {
            var screen = CoordinateMapper.ImageToScreen(frame, new ImagePoint(0, y));
            CoordinateMapper.ScreenToImage(frame, screen).ShouldBe(new ImagePoint(0, y), $"y {y}");
        }
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0 / 3)]
    [InlineData(0.75)]
    public void Every_screen_pixel_maps_into_the_image_and_back_within_one_image_pixel(double scale)
    {
        var frame = FrameGeometry.ForScale(Monitor, Monitor, scale);

        for (var x = 0; x < Monitor.Width; x++)
        {
            var image = CoordinateMapper.ScreenToImage(frame, new ScreenPoint(x, 0));
            image.X.ShouldBeInRange(0, frame.ImageWidth - 1);
            var back = CoordinateMapper.ImageToScreen(frame, image);
            Math.Abs(back.X - x).ShouldBeLessThan((int)Math.Ceiling(1 / scale), $"x {x}");
        }
    }

    [Fact]
    public void The_capture_monitor_position_is_added()
    {
        var second = new ScreenRect(1920, 0, 1920, 1080);
        var frame = FrameGeometry.ForScale(second, new ScreenRect(0, 0, 3840, 1080), 0.5);

        CoordinateMapper.ImageToScreen(frame, new ImagePoint(480, 270)).ShouldBe(new ScreenPoint(2880, 540));
        CoordinateMapper.ScreenToImage(frame, new ScreenPoint(2880, 540)).ShouldBe(new ImagePoint(480, 270));
    }

    [Fact]
    public void A_monitor_left_of_the_primary_has_negative_coordinates()
    {
        var left = new ScreenRect(-1920, 0, 1920, 1080);
        var desktop = new ScreenRect(-1920, 0, 3840, 1080);
        var frame = FrameGeometry.ForScale(left, desktop, 1.0);

        var screen = CoordinateMapper.ImageToScreen(frame, new ImagePoint(0, 0));

        screen.ShouldBe(new ScreenPoint(-1920, 0));
        CoordinateMapper.ScreenToNormalized(desktop, screen).ShouldBe(new NormalizedPoint(0, 0));
        CoordinateMapper.ScreenToNormalized(desktop, new ScreenPoint(0, 540)).ShouldBe(new NormalizedPoint(32768, 32768));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(960, 540, 32768, 32768)]
    [InlineData(1919, 1079, 65502, 65476)]
    public void Screen_pixels_become_mouse_units(int x, int y, int normalizedX, int normalizedY)
    {
        CoordinateMapper.ScreenToNormalized(Monitor, new ScreenPoint(x, y)).ShouldBe(new NormalizedPoint(normalizedX, normalizedY));
    }

    // Windows turns mouse units back into a pixel with value × size / 65536, dropping or rounding the fraction.
    // Both must land on the pixel we started from, on a single monitor and on a wide two-monitor desktop.
    [Theory]
    [InlineData(1920)]
    [InlineData(3840)]
    [InlineData(1080)]
    [InlineData(5760)]
    public void Mouse_units_lead_back_to_the_same_pixel(int size)
    {
        var desktop = new ScreenRect(0, 0, size, 1);

        for (var x = 0; x < size; x++)
        {
            long units = CoordinateMapper.ScreenToNormalized(desktop, new ScreenPoint(x, 0)).X;
            (units * size / 65536).ShouldBe(x, $"x {x}, dropped");
            Math.Round(units * size / 65536.0, MidpointRounding.ToEven).ShouldBe(x, $"x {x}, rounded");
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(960, 0)]
    [InlineData(0, 540)]
    public void A_point_outside_the_image_is_refused(int x, int y)
    {
        var frame = FrameGeometry.ForScale(Monitor, Monitor, 0.5);

        Should.Throw<ArgumentOutOfRangeException>(() => CoordinateMapper.ImageToScreen(frame, new ImagePoint(x, y)));
    }

    [Fact]
    public void A_screen_point_outside_the_capture_area_or_desktop_is_refused()
    {
        var frame = FrameGeometry.ForScale(Monitor, Monitor, 1.0);

        Should.Throw<ArgumentOutOfRangeException>(() => CoordinateMapper.ScreenToImage(frame, new ScreenPoint(1920, 0)));
        Should.Throw<ArgumentOutOfRangeException>(() => CoordinateMapper.ScreenToNormalized(Monitor, new ScreenPoint(-1, 0)));
    }

    [Theory]
    [InlineData(1.0, 1920, 1080)]
    [InlineData(0.5, 960, 540)]
    [InlineData(1.0 / 3, 640, 360)]
    [InlineData(0.0001, 1, 1)]
    public void The_image_size_is_rounded_and_at_least_one(double scale, int width, int height)
    {
        var frame = FrameGeometry.ForScale(Monitor, Monitor, scale);

        (frame.ImageWidth, frame.ImageHeight).ShouldBe((width, height));
    }
}
