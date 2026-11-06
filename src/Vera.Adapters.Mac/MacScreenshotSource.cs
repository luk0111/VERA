using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using Vera.Input;

namespace Vera.Adapters.Mac;

/// <summary>
/// Takes screenshots with macOS's own <c>screencapture</c> tool. Areas are given in points, the unit macOS positions windows in,
/// and <see cref="ScreenFrame.Scale"/> says how many image pixels each point became: 2 on a Retina screen.
/// </summary>
public sealed class MacScreenshotSource : IScreenshotSource
{
    private const string Tool = "/usr/sbin/screencapture";

    /// <summary>Where a PNG stores its width: bytes 16-19, then its height in bytes 20-23.</summary>
    private const int PngWidthOffset = 16;
    private const int PngHeightOffset = 20;

    private long _sequence;

    /// <exception cref="NotImplementedException"><paramref name="scale"/> asks for a smaller image, which nothing on macOS uses yet.</exception>
    public async Task<ScreenFrame> ScreenshotAsync(double? scale = null, CancellationToken ct = default)
    {
        if (scale is { } s && Math.Abs(s - 1) > double.Epsilon)
        {
            throw new NotImplementedException("Scaled screenshots are not built on macOS yet.");
        }

        var png = await CaptureAsync(area: null, ct).ConfigureAwait(false);
        var (width, height) = ReadPngSize(png);
        return NewFrame(png, width, height, width, height);
    }

    public async Task<ScreenFrame> ScreenshotAreaAsync(int left, int top, int width, int height, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var png = await CaptureAsync(FormatArea(left, top, width, height), ct).ConfigureAwait(false);
        var (imageWidth, imageHeight) = ReadPngSize(png);
        return NewFrame(png, imageWidth, imageHeight, width, height);
    }

    internal static string FormatArea(int left, int top, int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"-R{left},{top},{width},{height}");

    /// <exception cref="InvalidDataException">The bytes are not a PNG.</exception>
    internal static (int Width, int Height) ReadPngSize(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47];
        if (png.Length < PngHeightOffset + sizeof(int) || !png.StartsWith(signature))
        {
            throw new InvalidDataException("screencapture did not produce a PNG.");
        }

        return (
            BinaryPrimitives.ReadInt32BigEndian(png[PngWidthOffset..]),
            BinaryPrimitives.ReadInt32BigEndian(png[PngHeightOffset..]));
    }

    private ScreenFrame NewFrame(byte[] png, int width, int height, int screenWidth, int screenHeight) => new(
        width,
        height,
        (double)width / screenWidth,
        screenWidth,
        screenHeight,
        png,
        DateTimeOffset.UtcNow,
        Interlocked.Increment(ref _sequence));

    private static async Task<byte[]> CaptureAsync(string? area, CancellationToken ct)
    {
        // Without permission the tool still "works", but shows only the wallpaper, so the model would be given the wrong picture.
        if (!MacNative.CGPreflightScreenCaptureAccess())
        {
            MacNative.CGRequestScreenCaptureAccess();
            throw new ScreenUnavailableException(
                ScreenUnavailableReason.NoInteractiveSession,
                "VERA can't see the screen yet. In System Settings, open Privacy & Security > Screen & System Audio Recording, turn on the app you started VERA from (Terminal or Rider), then restart it.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"vera-{Guid.NewGuid():N}.png");
        try
        {
            var start = new ProcessStartInfo(Tool) { UseShellExecute = false, RedirectStandardError = true };
            start.ArgumentList.Add("-x"); // No shutter sound.
            start.ArgumentList.Add("-t");
            start.ArgumentList.Add("png");
            if (area is not null)
            {
                start.ArgumentList.Add(area);
            }

            start.ArgumentList.Add(path);

            using var process = Process.Start(start)
                ?? throw new ScreenUnavailableException(ScreenUnavailableReason.BlackFrame, "screencapture did not start.");
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(path))
            {
                throw new ScreenUnavailableException(ScreenUnavailableReason.BlackFrame, $"screencapture failed: {error.Trim()}");
            }

            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (Win32Exception e)
        {
            throw new ScreenUnavailableException(ScreenUnavailableReason.BlackFrame, "screencapture could not be run.", e);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
