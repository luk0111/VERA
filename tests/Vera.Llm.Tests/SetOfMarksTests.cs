using SkiaSharp;

using Vera.Core;

namespace Vera.Llm.Tests;

public sealed class SetOfMarksTests
{
    private static readonly UiElement Window = new(0, "Window", "App", new ScreenRect(100, 100, 800, 600), true, "window");
    private static readonly UiElement Button = new(1, "Button", "OK", new ScreenRect(300, 300, 100, 40), true, "ok");

    [Fact]
    public void A_small_image_keeps_its_size()
    {
        using var image = Decode(SetOfMarks.Draw(WhitePng(800, 600), [Window, Button]));

        (image.Width, image.Height).ShouldBe((800, 600));
    }

    [Fact]
    public void A_wide_image_is_scaled_to_1280()
    {
        var window = Window with { Bounds = new ScreenRect(0, 0, 2560, 1440) };

        using var image = Decode(SetOfMarks.Draw(WhitePng(2560, 1440), [window, Button]));

        (image.Width, image.Height).ShouldBe((1280, 720));
    }

    [Fact]
    public void The_mark_is_drawn_where_the_element_is()
    {
        using var image = Decode(SetOfMarks.Draw(WhitePng(800, 600), [Window, Button]));

        // The button's top-left corner, relative to the window.
        image.GetPixel(200, 200).ShouldNotBe(SKColors.White);
    }

    [Fact]
    public void The_window_itself_gets_no_mark()
    {
        using var image = Decode(SetOfMarks.Draw(WhitePng(800, 600), [Window]));

        image.Pixels.ShouldAllBe(p => p == SKColors.White);
    }

    [Fact]
    public void A_list_without_the_window_first_is_rejected()
    {
        Should.Throw<ArgumentException>(() => SetOfMarks.Draw(WhitePng(800, 600), [Button]));
    }

    internal static byte[] WhitePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static SKBitmap Decode(byte[] png) => SKBitmap.Decode(png);
}
