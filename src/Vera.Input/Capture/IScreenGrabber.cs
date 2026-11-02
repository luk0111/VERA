using Vera.Input.Platform;

namespace Vera.Input.Capture;

/// <summary>
/// Uncompressed pixels of one capture, before scaling and PNG encoding.
/// Each pixel is 4 bytes in the order blue, green, red, unused; rows follow each other with no padding.
/// </summary>
/// <param name="Bounds">The captured screen area in physical pixels; its size is the image size.</param>
public sealed record RawFrame(ScreenRect Bounds, byte[] Bgra, DateTimeOffset CapturedAt)
{
    public int Width => Bounds.Width;

    public int Height => Bounds.Height;

    /// <summary>Bytes per row.</summary>
    public int Stride => Bounds.Width * 4;
}

/// <summary>Copies a part of the screen as it is right now.</summary>
public interface IScreenGrabber
{
    /// <param name="bounds">The area to copy, in physical pixels (e.g. the capture monitor's bounds).</param>
    RawFrame Grab(ScreenRect bounds);
}