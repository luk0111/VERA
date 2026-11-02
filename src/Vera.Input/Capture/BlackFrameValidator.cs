namespace Vera.Input.Capture;

/// <summary>Checks a frame before it's handed out.</summary>
public interface IFrameValidator
{
    /// <exception cref="ScreenUnavailableException">The frame can't be what's really on screen.</exception>
    void Validate(RawFrame frame);
}

/// <summary>
/// Rejects frames that are (almost) entirely black. A locked, sleeping or disconnected screen often gives a black
/// picture instead of an error. Samples every 4th pixel in both directions; a sample is black when its luminance is
/// below 10, and the frame is black when more than 99.5% of the samples are. A small cursor on black still counts as
/// black; a dark theme (grey, not black) does not.
/// </summary>
internal sealed class BlackFrameValidator : IFrameValidator
{
    private const int SampleStride = 4;
    private const int LuminanceThreshold = 10;
    private const double BlackPixelRatio = 0.995;

    public void Validate(RawFrame frame)
    {
        var ratio = BlackRatio(frame, SampleStride, LuminanceThreshold);
        if (ratio > BlackPixelRatio)
        {
            throw new ScreenUnavailableException(
                ScreenUnavailableReason.BlackFrame,
                $"The screenshot is black ({ratio:P1} of the sampled pixels are darker than {LuminanceThreshold}). " +
                "The screen is probably locked, asleep or disconnected.");
        }
    }

    /// <summary>The share of sampled pixels whose luminance (0–255) is below <paramref name="threshold"/>.</summary>
    internal static double BlackRatio(RawFrame frame, int stride, int threshold)
    {
        var pixels = frame.Bgra;
        long samples = 0;
        long black = 0;
        for (var y = 0; y < frame.Height; y += stride)
        {
            var row = y * frame.Stride;
            for (var x = 0; x < frame.Width; x += stride)
            {
                var i = row + (x * 4);

                // Rec. 601 luma from blue, green, red.
                var luminance = ((114 * pixels[i]) + (587 * pixels[i + 1]) + (299 * pixels[i + 2])) / 1000;
                samples++;
                if (luminance < threshold)
                {
                    black++;
                }
            }
        }

        return samples == 0 ? 0 : (double)black / samples;
    }
}