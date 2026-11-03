using SkiaSharp;

using Vera.Core;

namespace Vera.Reporting.Tests;

public sealed class ElementHighlightTests
{
    private static readonly ScreenRect Window = new(100, 100, 800, 600);
    private static readonly ScreenRect Element = new(300, 300, 100, 40);

    [Fact]
    public void The_image_keeps_its_size()
    {
        using var image = Highlight();

        (image.Width, image.Height).ShouldBe((800, 600));
    }

    [Fact]
    public void The_box_edge_is_red()
    {
        using var image = Highlight();

        // The left edge of the element, relative to the window.
        image.GetPixel(200, 220).ShouldBe(SKColors.Red);
    }

    [Fact]
    public void The_inside_of_the_box_is_untouched()
    {
        using var image = Highlight();

        image.GetPixel(250, 220).ShouldBe(SKColors.White);
    }

    private static SKBitmap Highlight()
    {
        using var bitmap = new SKBitmap(800, 600);
        bitmap.Erase(SKColors.White);
        using var white = SKImage.FromBitmap(bitmap);
        using var png = white.Encode(SKEncodedImageFormat.Png, 100);

        return SKBitmap.Decode(ElementHighlight.DrawBox(png.ToArray(), Window, Element));
    }
}
