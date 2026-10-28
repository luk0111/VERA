using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Vera.Input.Capture;

/// <summary>A PNG and its size in pixels.</summary>
public sealed record EncodedImage(byte[] Png, int Width, int Height);

/// <summary>Turns raw pixels into a PNG.</summary>
public interface IFrameEncoder
{
    /// <param name="scale">Image size relative to the raw frame. Only 1.0 until scaling is added (step 3.1).</param>
    EncodedImage Encode(RawFrame frame, double scale);
}

/// <summary>Encodes with GDI+ (<see cref="Bitmap.Save(Stream, ImageFormat)"/>).</summary>
internal sealed class GdiPngFrameEncoder : IFrameEncoder
{
    public EncodedImage Encode(RawFrame frame, double scale)
    {
        if (scale != 1.0)
        {
            throw new NotSupportedException($"Scale {scale} isn't supported yet; scaling comes in step 3.1. Use 1.0.");
        }

        var handle = GCHandle.Alloc(frame.Bgra, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(frame.Width, frame.Height, frame.Stride, PixelFormat.Format32bppRgb, handle.AddrOfPinnedObject());
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return new EncodedImage(stream.ToArray(), frame.Width, frame.Height);
        }
        finally
        {
            handle.Free();
        }
    }
}