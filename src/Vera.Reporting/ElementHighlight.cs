using SkiaSharp;

using Vera.Core;

namespace Vera.Reporting;

/// <summary>Marks an element on a screenshot for the tester, at full size.</summary>
public static class ElementHighlight
{
    private const float StrokeWidth = 4;

    /// <param name="png">A screenshot of exactly <paramref name="window"/>.</param>
    /// <param name="window">The screenshot's area in screen pixels.</param>
    /// <param name="element">The element's bounds in screen pixels.</param>
    /// <returns>A PNG of the same size with a red box around the element.</returns>
    public static byte[] DrawBox(ReadOnlyMemory<byte> png, ScreenRect window, ScreenRect element)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(element);

        using var bitmap = SKBitmap.Decode(png.ToArray());
        using (var canvas = new SKCanvas(bitmap))
        using (var red = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = StrokeWidth })
        {
            canvas.DrawRect(element.X - window.X, element.Y - window.Y, element.Width, element.Height, red);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
