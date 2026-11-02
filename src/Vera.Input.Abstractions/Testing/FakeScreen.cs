using System.Buffers.Binary;

namespace Vera.Input.Testing;

/// <summary>
/// An <see cref="IScreen"/> without a real screen: returns the given PNGs in order (starting over after the last)
/// and records every call in <see cref="Actions"/>. Coordinates are checked against the latest frame like the real
/// screen does; before the first screenshot, the first PNG's size is used.
/// Frames are returned as-is with <see cref="ScreenFrame.Scale"/> 1; a requested scale is only recorded.
/// </summary>
public sealed class FakeScreen : IScreen
{
    private readonly (byte[] Png, int Width, int Height)[] _frames;
    private readonly TimeProvider _timeProvider;
    private readonly List<RecordedAction> _actions = [];
    private readonly Lock _lock = new();
    private long _sequence;
    private int _width;
    private int _height;

    public FakeScreen(IEnumerable<byte[]> pngs, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(pngs);
        _frames = pngs.Select(png =>
        {
            var (width, height) = ReadSize(png);
            return (png, width, height);
        }).ToArray();
        if (_frames.Length == 0)
        {
            throw new ArgumentException("At least one PNG is needed.", nameof(pngs));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        (_, _width, _height) = _frames[0];
    }

    /// <summary>Plays the <c>*.png</c> files in <paramref name="folder"/>, sorted by file name.</summary>
    public static FakeScreen FromFolder(string folder, TimeProvider? timeProvider = null)
    {
        var files = Directory.GetFiles(folder, "*.png").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0)
        {
            throw new ArgumentException($"No .png files in '{folder}'.", nameof(folder));
        }

        return new FakeScreen(files.Select(File.ReadAllBytes), timeProvider);
    }

    /// <summary>Every call so far, in order. Calls that threw are not recorded.</summary>
    public IReadOnlyList<RecordedAction> Actions
    {
        get
        {
            lock (_lock)
            {
                return [.. _actions];
            }
        }
    }

    public Task<ScreenFrame> ScreenshotAsync(double? scale = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (scale is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must be greater than 0 and at most 1.");
        }

        lock (_lock)
        {
            var (png, width, height) = _frames[_sequence % _frames.Length];
            _sequence++;
            _width = width;
            _height = height;
            _actions.Add(new Screenshot(scale));
            return Task.FromResult(new ScreenFrame(width, height, 1, width, height, png, _timeProvider.GetUtcNow(), _sequence));
        }
    }

    public Task MoveAsync(int x, int y, CancellationToken ct = default) =>
        Record(new Moved(x, y), ct, (x, y));

    public Task ClickAsync(int x, int y, MouseButton button = MouseButton.Left, int clicks = 1, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(clicks, 1);
        return Record(new Clicked(x, y, button, clicks), ct, (x, y));
    }

    public Task ScrollAsync(int x, int y, int deltaY, int deltaX = 0, CancellationToken ct = default) =>
        Record(new Scrolled(x, y, deltaY, deltaX), ct, (x, y));

    public Task DragAsync(int fromX, int fromY, int toX, int toY, MouseButton button = MouseButton.Left, CancellationToken ct = default) =>
        Record(new Dragged(fromX, fromY, toX, toY, button), ct, (fromX, fromY), (toX, toY));

    public Task TypeTextAsync(string text, TypeTextOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Record(new TypedText(text, options), ct);
    }

    public Task KeyAsync(string combo, int repeat = 1, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(combo);
        if (string.IsNullOrWhiteSpace(combo))
        {
            throw new InvalidKeyComboException("The key combo is empty.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(repeat, 1);
        return Record(new KeyPressed(combo, repeat), ct);
    }

    private Task Record(RecordedAction action, CancellationToken ct, params ReadOnlySpan<(int X, int Y)> points)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            foreach (var (x, y) in points)
            {
                CheckInFrame(x, y);
            }

            _actions.Add(action);
        }

        return Task.CompletedTask;
    }

    private void CheckInFrame(int x, int y)
    {
        if (x < 0 || x >= _width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"x must be within the frame (0..{_width - 1}).");
        }

        if (y < 0 || y >= _height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"y must be within the frame (0..{_height - 1}).");
        }
    }

    // A PNG starts with an 8-byte signature, then the IHDR chunk: length (4), type (4), width (4), height (4), big-endian.
    private static (int Width, int Height) ReadSize(byte[] png)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (png is null || png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(signature) || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("Not a PNG image.");
        }

        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }
}