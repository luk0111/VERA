using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using Vera.Input.Coordinates;

namespace Vera.Input.Capture;

/// <summary>A PNG and its size in pixels.</summary>
public sealed record EncodedImage(byte[] Png, int Width, int Height);

/// <summary>Turns raw pixels into a PNG.</summary>
public interface IFrameEncoder
{
    /// <param name="scale">Image size relative to the raw frame, greater than 0 and at most 1.</param>
    EncodedImage Encode(RawFrame frame, double scale);
}

/// <summary>Encodes with GDI+ (<see cref="Bitmap.Save(Stream, ImageFormat)"/>), shrinking first with high-quality bicubic.</summary>
internal sealed class GdiPngFrameEncoder : IFrameEncoder
{
    public EncodedImage Encode(RawFrame frame, double scale)
    {
        var handle = GCHandle.Alloc(frame.Bgra, GCHandleType.Pinned);
        try
        {
            using var source = new Bitmap(frame.Width, frame.Height, frame.Stride, PixelFormat.Format32bppRgb, handle.AddrOfPinnedObject());
            if (scale == 1.0)
            {
                return new EncodedImage(ToPng(source), frame.Width, frame.Height);
            }

            var (width, height) = FrameGeometry.ScaledSize(frame.Width, frame.Height, scale);
            using var scaled = Resize(source, width, height);
            return new EncodedImage(ToPng(scaled), width, height);
        }
        finally
        {
            handle.Free();
        }
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingMode = CompositingMode.SourceCopy;

        // Without TileFlipXY the filter reads "outside" the image as transparent black, which darkens the edges.
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return scaled;
    }

    private static byte[] ToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
