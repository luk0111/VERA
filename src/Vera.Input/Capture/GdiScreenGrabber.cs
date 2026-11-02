using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using Vera.Input.Platform;

namespace Vera.Input.Capture;

/// <summary>Copies the screen with GDI (<see cref="Graphics.CopyFromScreen(int, int, int, int, Size, CopyPixelOperation)"/>).</summary>
internal sealed class GdiScreenGrabber(TimeProvider timeProvider) : IScreenGrabber
{
    private const int ErrorInvalidHandle = 6;

    public RawFrame Grab(ScreenRect bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bounds.Width, 1, nameof(bounds));
        ArgumentOutOfRangeException.ThrowIfLessThan(bounds.Height, 1, nameof(bounds));

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            try
            {
                // CopyFromScreen rejects SourceCopy | CaptureBlt, so local layered windows (e.g. tooltips) may be missing.
                // Over RDP everything on the remote is drawn inside the mstsc window, which is captured normally.
                graphics.CopyFromScreen(
                    bounds.Left, bounds.Top, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorInvalidHandle)
            {
                // The screen went away between the session guard's check and the copy.
                throw new ScreenUnavailableException(
                    ScreenUnavailableReason.WorkstationLocked,
                    "Windows refused to copy the screen (the handle is invalid). The machine is probably locked or the session disconnected.",
                    e);
            }
        }

        var capturedAt = timeProvider.GetUtcNow();
        var pixels = new byte[bounds.Width * 4 * bounds.Height];
        var data = bitmap.LockBits(new Rectangle(0, 0, bounds.Width, bounds.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var rowBytes = bounds.Width * 4;
            for (var y = 0; y < bounds.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new RawFrame(bounds, pixels, capturedAt);
    }
}