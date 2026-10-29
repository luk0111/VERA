using Vera.Input.Platform;

namespace Vera.Input.Coordinates;

/// <summary>
/// Turns screenshot pixels into screen pixels and back, and screen pixels into the units Windows wants for mouse moves.
/// Pure maths: no screen access. X and Y are mapped separately, because rounding can make their scales differ slightly.
/// </summary>
public static class CoordinateMapper
{
    /// <summary>
    /// The screen pixel under the centre of an image pixel (on a boundary between two, the left/top one). At scale 0.5,
    /// image (480,270) covers screen pixels 960–961 × 540–541 and maps to (960,540).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The point is outside the image.</exception>
    public static ScreenPoint ImageToScreen(FrameGeometry frame, ImagePoint point)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (point.X < 0 || point.X >= frame.ImageWidth || point.Y < 0 || point.Y >= frame.ImageHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(point), point,
                $"The point is outside the {frame.ImageWidth}×{frame.ImageHeight} image (x 0–{frame.ImageWidth - 1}, y 0–{frame.ImageHeight - 1}).");
        }

        var bounds = frame.CaptureBounds;
        return new ScreenPoint(
            bounds.Left + ImageToScreen(point.X, frame.ImageWidth, bounds.Width),
            bounds.Top + ImageToScreen(point.Y, frame.ImageHeight, bounds.Height));
    }

    /// <summary>The image pixel that shows a screen pixel. <see cref="ImageToScreen(FrameGeometry, ImagePoint)"/> then returns the same image pixel.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The point is outside the captured area.</exception>
    public static ImagePoint ScreenToImage(FrameGeometry frame, ScreenPoint point)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var bounds = frame.CaptureBounds;
        if (!bounds.Contains(point.X, point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point), point, $"The point is outside the captured area {bounds}.");
        }

        return new ImagePoint(
            ScreenToImage(point.X - bounds.Left, bounds.Width, frame.ImageWidth),
            ScreenToImage(point.Y - bounds.Top, bounds.Height, frame.ImageHeight));
    }

    /// <summary>
    /// The smallest normalized value at or past the pixel's left/top edge. Windows turns it back into a pixel with roughly
    /// value × size / 65536, and this lands on the same pixel whether it drops or rounds the fraction. M4.3 checks it on
    /// the real screen.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The point is outside the virtual desktop.</exception>
    public static NormalizedPoint ScreenToNormalized(ScreenRect virtualDesktop, ScreenPoint point)
    {
        if (!virtualDesktop.Contains(point.X, point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point), point, $"The point is outside the virtual desktop {virtualDesktop}.");
        }

        return new NormalizedPoint(
            ToNormalized(point.X - virtualDesktop.Left, virtualDesktop.Width),
            ToNormalized(point.Y - virtualDesktop.Top, virtualDesktop.Height));
    }

    // Image pixel i spans screen [i·S/I, (i+1)·S/I); its centre c = (2i+1)·S / 2I. Screen pixel s spans [s, s+1),
    // so the one containing c is ceil(c − 1) when c falls on a boundary between two pixels, and floor(c) otherwise.
    private static int ImageToScreen(int image, int imageSize, int screenSize) =>
        CeilDiv(((2L * image) + 1) * screenSize - (2L * imageSize), 2L * imageSize);

    // The image pixel containing the screen pixel's centre (s + 0.5).
    private static int ScreenToImage(int screen, int screenSize, int imageSize) =>
        (int)(((2L * screen) + 1) * imageSize / (2L * screenSize));

    private static int ToNormalized(int offset, int size) =>
        (int)Math.Min(65535, CeilDiv(offset * 65536L, size));

    private static int CeilDiv(long numerator, long denominator) =>
        (int)(numerator >= 0 ? (numerator + denominator - 1) / denominator : -(-numerator / denominator));
}
