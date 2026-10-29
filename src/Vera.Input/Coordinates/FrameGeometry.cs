using Vera.Input.Platform;

namespace Vera.Input.Coordinates;

/// <summary>A pixel in a screenshot: (0,0) is its top-left corner.</summary>
public readonly record struct ImagePoint(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>A physical pixel on the virtual desktop (all monitors). Can be negative on multi-monitor setups.</summary>
public readonly record struct ScreenPoint(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// What Windows expects for an absolute mouse move over all monitors (<c>MOUSEEVENTF_VIRTUALDESK</c>):
/// 0 is the left/top edge of the virtual desktop, 65535 the right/bottom edge.
/// </summary>
public readonly record struct NormalizedPoint(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>Where a screenshot came from and how big it is: everything needed to turn its pixels into screen pixels.</summary>
/// <param name="ImageWidth">The screenshot's width in pixels (after scaling).</param>
/// <param name="ImageHeight">The screenshot's height in pixels (after scaling).</param>
/// <param name="CaptureBounds">The part of the screen that was captured (today: the capture monitor).</param>
/// <param name="VirtualDesktop">The area covering all monitors, for <see cref="NormalizedPoint"/>.</param>
public sealed record FrameGeometry(int ImageWidth, int ImageHeight, ScreenRect CaptureBounds, ScreenRect VirtualDesktop)
{
    /// <summary>Image size relative to the captured area, e.g. 0.5.</summary>
    public double Scale => (double)ImageWidth / CaptureBounds.Width;

    /// <summary>The geometry a screenshot at <paramref name="scale"/> would have.</summary>
    public static FrameGeometry ForScale(ScreenRect captureBounds, ScreenRect virtualDesktop, double scale)
    {
        var (width, height) = ScaledSize(captureBounds.Width, captureBounds.Height, scale);
        return new FrameGeometry(width, height, captureBounds, virtualDesktop);
    }

    /// <summary>The image size for a scale: each side rounded, at least 1. The encoder uses the same rule.</summary>
    public static (int Width, int Height) ScaledSize(int width, int height, double scale) =>
        (Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)),
         Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)));
}
