using SkiaSharp;

using Vera.Core;

namespace Vera.Llm;

/// <summary>
/// Draws each element's number on a copy of the screenshot (Set-of-Marks), so the model can answer with a number.
/// Part of what the model sees; the screenshot in <see cref="ScreenState"/> itself stays unmarked.
/// </summary>
internal static class SetOfMarks
{
    /// <summary>Wider images are scaled down: they cost the model more context and time without helping it.</summary>
    public const int MaxWidth = 1280;

    private static readonly SKColor MarkColor = new(255, 0, 170);

    /// <param name="elements">Element 0 is the window: its bounds are the screenshot's area. The others are marked.</param>
    public static byte[] Draw(ReadOnlyMemory<byte> png, IReadOnlyList<UiElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count == 0 || elements[0].Role != "Window")
        {
            throw new ArgumentException("The first element must be the window.", nameof(elements));
        }

        using var source = SKBitmap.Decode(png.ToArray());
        var scale = Math.Min(1f, (float)MaxWidth / source.Width);
        var info = new SKImageInfo((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale));

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Scale(scale);
        using (var sourceImage = SKImage.FromBitmap(source))
        {
            canvas.DrawImage(sourceImage, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        // Sizes are in screenshot pixels, so after scaling they shrink with the image.
        // With many marks (a big page for Gemini, #41) labels and outlines get smaller, so they don't cover each other and the buttons.
        var (fontSize, strokeWidth) = MarkSize(elements.Count - 1);
        using var outline = new SKPaint { Color = MarkColor, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, IsAntialias = false };
        using var label = new SKPaint { Color = MarkColor, Style = SKPaintStyle.Fill };
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var font = new SKFont { Size = fontSize };

        var window = elements[0].Bounds;
        foreach (var element in elements.Skip(1))
        {
            var x = element.Bounds.X - window.X;
            var y = element.Bounds.Y - window.Y;
            canvas.DrawRect(x, y, element.Bounds.Width, element.Bounds.Height, outline);

            var number = element.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var width = font.MeasureText(number) + 6;
            canvas.DrawRect(x, y, width, font.Size + 3, label);
            canvas.DrawText(number, x + 3, y + font.Size, SKTextAlign.Left, font, text);
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    /// <summary>Label size and outline width for <paramref name="marks"/> numbers: the usual size up to 80, then smaller.</summary>
    internal static (float FontSize, float StrokeWidth) MarkSize(int marks) => marks switch
    {
        <= 80 => (14, 2),
        <= 160 => (11, 1),
        _ => (9, 1),
    };
}
