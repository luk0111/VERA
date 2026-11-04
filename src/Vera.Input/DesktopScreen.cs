using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Vera.Input.Capture;
using Vera.Input.Coordinates;
using Vera.Input.Options;
using Vera.Input.Platform;
using Vera.Input.Session;

namespace Vera.Input;

/// <summary>
/// The Windows <see cref="IScreen"/>: puts the building blocks together. On first use it checks the DPI awareness and
/// the display (<see cref="IDisplayCheck"/>) and remembers the capture monitor. Before every screenshot the
/// <see cref="ISessionGuard"/> checks the session and the RDP window.
/// Mouse and keyboard come in later milestones and throw <see cref="NotImplementedException"/> until then.
/// </summary>
internal sealed partial class DesktopScreen(
    IDisplayCheck displayCheck,
    ISessionGuard guard,
    IScreenGrabber grabber,
    IFrameValidator validator,
    IFrameEncoder encoder,
    IOptions<VeraInputOptions> options,
    ILogger<DesktopScreen> logger) : IScreen
{
    private readonly Lock _lock = new();
    private DisplayReport? _display;
    private FrameGeometry? _latest;
    private long _sequence;

    public Task<ScreenFrame> ScreenshotAsync(double? scale = null, CancellationToken ct = default)
    {
        var effectiveScale = scale ?? options.Value.Capture.DefaultScale;
        if (effectiveScale is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must be greater than 0 and at most 1.");
        }

        ct.ThrowIfCancellationRequested();
        return Task.Run(() => Capture(Display().CaptureMonitor.Bounds, effectiveScale), ct);
    }

    public Task<ScreenFrame> ScreenshotAreaAsync(int left, int top, int width, int height, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() => Capture(new ScreenRect(left, top, width, height), 1), ct);
    }

    public Task MoveAsync(int x, int y, CancellationToken ct = default) =>
        throw new NotImplementedException("Mouse input comes in Milestone 4.");

    public Task ClickAsync(int x, int y, MouseButton button = MouseButton.Left, int clicks = 1, CancellationToken ct = default) =>
        throw new NotImplementedException("Mouse input comes in Milestone 4.");

    public Task ScrollAsync(int x, int y, int deltaY, int deltaX = 0, CancellationToken ct = default) =>
        throw new NotImplementedException("Scrolling comes in Milestone 7.");

    public Task DragAsync(int fromX, int fromY, int toX, int toY, MouseButton button = MouseButton.Left, CancellationToken ct = default) =>
        throw new NotImplementedException("Dragging comes in Milestone 7.");

    public Task TypeTextAsync(string text, TypeTextOptions? options = null, CancellationToken ct = default) =>
        throw new NotImplementedException("Typing comes in Milestone 5.");

    public Task KeyAsync(string combo, int repeat = 1, CancellationToken ct = default) =>
        throw new NotImplementedException("Key combos come in Milestone 6.");

    /// <summary>
    /// The geometry clicks are mapped with (the coordinate rule): the latest screenshot's, or before the first one, a
    /// screenshot at <c>Capture.DefaultScale</c>.
    /// </summary>
    internal FrameGeometry CurrentGeometry()
    {
        var display = Display();
        lock (_lock)
        {
            return _latest ?? FrameGeometry.ForScale(
                display.CaptureMonitor.Bounds, display.Snapshot.VirtualDesktop, options.Value.Capture.DefaultScale);
        }
    }

    private ScreenFrame Capture(ScreenRect bounds, double scale)
    {
        var display = Display();

        var stopwatch = Stopwatch.StartNew();
        RawFrame raw;
        try
        {
            guard.EnsureReady(bounds);
            raw = grabber.Grab(bounds);
            validator.Validate(raw);
        }
        catch (ScreenUnavailableException e)
        {
            LogRejected(logger, e.Reason, e.Message);
            throw;
        }

        var grabMs = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var image = encoder.Encode(raw, scale);
        var encodeMs = stopwatch.ElapsedMilliseconds;

        var geometry = new FrameGeometry(image.Width, image.Height, bounds, display.Snapshot.VirtualDesktop);
        long sequence;
        lock (_lock)
        {
            sequence = ++_sequence;
            _latest = geometry;
        }

        LogCaptured(logger, sequence, image.Width, image.Height, grabMs, encodeMs, image.Png.Length);

        return new ScreenFrame(
            image.Width,
            image.Height,
            geometry.Scale,
            raw.Width,
            raw.Height,
            image.Png,
            raw.CapturedAt,
            sequence);
    }

    private DisplayReport Display()
    {
        lock (_lock)
        {
            return _display ??= displayCheck.Run();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Screenshot {Sequence}: {Width}×{Height}, grab {GrabMs} ms, encode {EncodeMs} ms, {Bytes} bytes")]
    private static partial void LogCaptured(ILogger logger, long sequence, int width, int height, long grabMs, long encodeMs, int bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Screenshot rejected ({Reason}): {Detail}")]
    private static partial void LogRejected(ILogger logger, ScreenUnavailableReason reason, string detail);
}