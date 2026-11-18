using System.Text;

using Microsoft.Extensions.Logging;

using Vera.Input;

namespace Vera.Adapters.Mac;

/// <summary>
/// Screenshots, mouse and keyboard on macOS. Coordinates are pixels in the most recent screenshot, as on Windows: on a Retina
/// screen each point is two pixels, so a click at pixel (200, 100) of a window screenshot lands 100 points right, 50 down.
/// Every action first checks the Accessibility permission, without which macOS drops the events silently.
/// </summary>
public sealed partial class MacScreen : IScreen
{
    /// <summary>The most clicks one call can make: a triple click selects a line.</summary>
    internal const int MaxClicks = 3;

    /// <summary>How many small moves a drag or a hover glide makes, <c>BetweenMoveSteps</c> apart. The same as on Windows.</summary>
    internal const int GlideSteps = 15;

    /// <summary>The most wheel notches one scroll can turn each way, so a wrong number can't scroll for minutes.</summary>
    internal const int MaxNotches = 50;

    private const string AccessibilityMissing =
        "VERA can't control the mouse and keyboard yet. In System Settings, open Privacy & Security > Accessibility, turn on the app you started VERA from (Terminal or Rider), then restart it.";

    private readonly IScreenshotSource _capture;
    private readonly IMacInputSink _sink;
    private readonly Func<bool> _canControl;
    private readonly MacDelays _delays;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();

    private FrameArea? _latest;

    public MacScreen(ILogger<MacScreen> logger)
        : this(new MacScreenshotSource(), new CoreGraphicsInputSink(), MacNative.AccessibilityAllowedOrPrompt, MacDelays.Default, TimeProvider.System, logger)
    {
    }

    internal MacScreen(IScreenshotSource capture, IMacInputSink sink, Func<bool> canControl, MacDelays delays, TimeProvider time, ILogger logger)
    {
        _delays = delays;
        _capture = capture;
        _sink = sink;
        _canControl = canControl;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// The pauses between input events. The defaults are the Windows ones (<c>VeraInput:Delays</c>), which work there; settings come
    /// if a Mac app needs others.
    /// </summary>
    internal sealed record MacDelays(
        TimeSpan AfterMove,
        TimeSpan MouseDownUp,
        TimeSpan BetweenClicks,
        TimeSpan AfterClick,
        TimeSpan BetweenKeys,
        TimeSpan AfterType,
        TimeSpan BetweenNotches,
        TimeSpan BetweenMoveSteps,
        TimeSpan HoverDwell)
    {
        public static readonly MacDelays Default = new(Ms(50), Ms(50), Ms(80), Ms(300), Ms(20), Ms(200), Ms(50), Ms(15), Ms(500));

        public static readonly MacDelays None = new(
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }

    /// <summary>Where one screenshot came from: its top-left corner in screen points, its size in pixels, and pixels per point.</summary>
    internal sealed record FrameArea(double Left, double Top, int Width, int Height, double Scale)
    {
        /// <exception cref="ArgumentOutOfRangeException">The point is outside the screenshot. It is never moved to the edge.</exception>
        public (double X, double Y) ToScreen(int x, int y)
        {
            if (x < 0 || x >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, $"x must be between 0 and {Width - 1}, the width of the latest screenshot.");
            }

            if (y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(y), y, $"y must be between 0 and {Height - 1}, the height of the latest screenshot.");
            }

            return (Left + (x / Scale), Top + (y / Scale));
        }
    }

    public async Task<ScreenFrame> ScreenshotAsync(double? scale = null, CancellationToken ct = default)
    {
        var frame = await _capture.ScreenshotAsync(scale, ct).ConfigureAwait(false);
        Remember(new FrameArea(0, 0, frame.Width, frame.Height, frame.Scale), frame);
        return frame;
    }

    /// <param name="left">The area's left edge in screen points, the unit macOS places windows in.</param>
    /// <param name="top">The area's top edge in screen points.</param>
    /// <param name="width">The area's width in points.</param>
    /// <param name="height">The area's height in points.</param>
    public async Task<ScreenFrame> ScreenshotAreaAsync(int left, int top, int width, int height, CancellationToken ct = default)
    {
        var frame = await _capture.ScreenshotAreaAsync(left, top, width, height, ct).ConfigureAwait(false);
        Remember(new FrameArea(left, top, frame.Width, frame.Height, frame.Scale), frame);
        return frame;
    }

    /// <summary>Hovers: glides the pointer from where it is to the point, then waits <c>HoverDwell</c> so tooltips show.</summary>
    public async Task MoveAsync(int x, int y, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var to = Target(x, y);
        LogMove(_logger, x, y, to.X, to.Y);
        var from = _sink.Cursor() ?? to;
        await GlideAsync(from, to, p => new MacInputEvent.MouseMove(p.X, p.Y), ct).ConfigureAwait(false);
        await Delay(_delays.HoverDwell, ct).ConfigureAwait(false);
    }

    /// <summary>Moves there, then presses and releases <paramref name="button"/> <paramref name="clicks"/> times. A button pressed when the click stops is released.</summary>
    public async Task ClickAsync(int x, int y, MouseButton button = MouseButton.Left, int clicks = 1, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(clicks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clicks, MaxClicks);
        ct.ThrowIfCancellationRequested();
        var (screenX, screenY) = Target(x, y);
        LogClick(_logger, button, clicks, x, y, screenX, screenY);

        _sink.Send(new MacInputEvent.MouseMove(screenX, screenY));
        await Delay(_delays.AfterMove, ct).ConfigureAwait(false);
        for (var click = 1; click <= clicks; click++)
        {
            if (click > 1)
            {
                await Delay(_delays.BetweenClicks, ct).ConfigureAwait(false);
            }

            _sink.Send(new MacInputEvent.MouseButtonEvent(screenX, screenY, button, Down: true, click));
            try
            {
                await Delay(_delays.MouseDownUp, ct).ConfigureAwait(false);
            }
            finally
            {
                _sink.Send(new MacInputEvent.MouseButtonEvent(screenX, screenY, button, Down: false, click));
            }
        }

        await Delay(_delays.AfterClick, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves there, then turns the wheel one notch (a line) at a time, <c>BetweenNotches</c> apart: first <paramref name="deltaY"/>
    /// notches (positive scrolls down), then <paramref name="deltaX"/> (positive scrolls right).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The point is outside the screenshot, both deltas are 0, or one is beyond 50 notches. Nothing moves.</exception>
    public async Task ScrollAsync(int x, int y, int deltaY, int deltaX = 0, CancellationToken ct = default)
    {
        CheckScroll(deltaY, deltaX);
        ct.ThrowIfCancellationRequested();
        var (screenX, screenY) = Target(x, y);
        LogScroll(_logger, deltaY, deltaX, x, y, screenX, screenY);

        _sink.Send(new MacInputEvent.MouseMove(screenX, screenY));
        await Delay(_delays.AfterMove, ct).ConfigureAwait(false);

        // macOS counts up and left as positive, the other way round from VERA.
        var notches = Enumerable.Repeat(new MacInputEvent.Scroll(-Math.Sign(deltaY), 0), Math.Abs(deltaY))
            .Concat(Enumerable.Repeat(new MacInputEvent.Scroll(0, -Math.Sign(deltaX)), Math.Abs(deltaX)))
            .ToList();
        for (var i = 0; i < notches.Count; i++)
        {
            if (i > 0)
            {
                await Delay(_delays.BetweenNotches, ct).ConfigureAwait(false);
            }

            _sink.Send(notches[i]);
        }

        await Delay(_delays.AfterClick, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Presses <paramref name="button"/> at the start, glides to the end in small drag moves (so apps notice the drag), waits so the
    /// target sees the pointer, then releases. The button is released even if the drag stops halfway.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A point is outside the screenshot. Nothing moves.</exception>
    public async Task DragAsync(int fromX, int fromY, int toX, int toY, MouseButton button = MouseButton.Left, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var to = Area().ToScreen(toX, toY);
        var from = Target(fromX, fromY);
        LogDrag(_logger, button, fromX, fromY, toX, toY, from.X, from.Y, to.X, to.Y);

        _sink.Send(new MacInputEvent.MouseMove(from.X, from.Y));
        await Delay(_delays.AfterMove, ct).ConfigureAwait(false);
        _sink.Send(new MacInputEvent.MouseButtonEvent(from.X, from.Y, button, Down: true, 1));
        var last = from;
        try
        {
            await Delay(_delays.MouseDownUp, ct).ConfigureAwait(false);
            await GlideAsync(
                from,
                to,
                p =>
                {
                    last = p;
                    return new MacInputEvent.MouseDrag(p.X, p.Y, button);
                },
                ct).ConfigureAwait(false);
            await Delay(_delays.MouseDownUp, ct).ConfigureAwait(false);
        }
        finally
        {
            _sink.Send(new MacInputEvent.MouseButtonEvent(last.X, last.Y, button, Down: false, 1));
        }

        await Delay(_delays.AfterClick, ct).ConfigureAwait(false);
    }

    /// <summary>The points of a glide: <see cref="GlideSteps"/> moves, evenly spaced, the last one exactly on <paramref name="to"/>.</summary>
    internal static IEnumerable<(double X, double Y)> Glide((double X, double Y) from, (double X, double Y) to)
    {
        for (var i = 1; i <= GlideSteps; i++)
        {
            yield return i == GlideSteps
                ? to
                : (from.X + ((to.X - from.X) * i / GlideSteps), from.Y + ((to.Y - from.Y) * i / GlideSteps));
        }
    }

    /// <exception cref="ArgumentOutOfRangeException">Both are 0, or one is beyond <see cref="MaxNotches"/>.</exception>
    private static void CheckScroll(int deltaY, int deltaX)
    {
        if (deltaY == 0 && deltaX == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaY), "Scroll by at least one notch.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs(deltaY), MaxNotches, nameof(deltaY));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs(deltaX), MaxNotches, nameof(deltaX));
    }

    private async Task GlideAsync(
        (double X, double Y) from, (double X, double Y) to, Func<(double X, double Y), MacInputEvent> step, CancellationToken ct)
    {
        var first = true;
        foreach (var point in Glide(from, to))
        {
            if (!first)
            {
                await Delay(_delays.BetweenMoveSteps, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            _sink.Send(step(point));
            first = false;
        }
    }

    /// <summary>Types one character at a time as text, so any language or emoji arrives as written. <c>\n</c> presses Return, <c>\t</c> Tab, <c>\r</c> is skipped.</summary>
    public async Task TypeTextAsync(string text, TypeTextOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ct.ThrowIfCancellationRequested();

        // Checked first, so a bad character types nothing.
        var keystrokes = Keystrokes(text);
        if (keystrokes.Count == 0)
        {
            return;
        }

        EnsureCanControl();
        if (options?.Sensitive == true)
        {
            LogTypeSensitive(_logger, text.Length);
        }
        else
        {
            LogType(_logger, text.Length, text);
        }

        for (var i = 0; i < keystrokes.Count; i++)
        {
            if (i > 0)
            {
                await Delay(_delays.BetweenKeys, ct).ConfigureAwait(false);
            }

            // Down and up together, so stopping halfway never leaves a key pressed.
            _sink.Send(keystrokes[i].Down);
            _sink.Send(keystrokes[i].Up);
        }

        await Delay(_delays.AfterType, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Presses a combo such as <c>ctrl+s</c> (Command+S on a Mac) <paramref name="repeat"/> times: modifiers down, the main key down
    /// and up, then the modifiers up in reverse, <c>BetweenKeys</c> apart. Keys still down when the press stops are released.
    /// </summary>
    public async Task KeyAsync(string combo, int repeat = 1, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(repeat, 1);
        ct.ThrowIfCancellationRequested();

        // Parsed first, so a bad combo presses nothing.
        var parsed = MacKeyCombo.Parse(combo);
        EnsureCanControl();
        LogKey(_logger, parsed.Text, repeat);

        for (var r = 0; r < repeat; r++)
        {
            if (r > 0)
            {
                await Delay(_delays.BetweenKeys, ct).ConfigureAwait(false);
            }

            await PressOnceAsync(parsed, ct).ConfigureAwait(false);
        }

        await Delay(_delays.AfterType, ct).ConfigureAwait(false);
    }

    /// <summary>The keystrokes for <paramref name="text"/>: one per character, emoji included.</summary>
    /// <exception cref="ArgumentException">A control character other than <c>\n</c>, <c>\t</c>, <c>\r</c>, or a broken emoji (lone surrogate).</exception>
    internal static List<(MacInputEvent Down, MacInputEvent Up)> Keystrokes(string text)
    {
        var keystrokes = new List<(MacInputEvent, MacInputEvent)>(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var used) != System.Buffers.OperationStatus.Done)
            {
                throw new ArgumentException($"The text has a broken character at position {i}. Nothing was typed.", nameof(text));
            }

            switch (rune.Value)
            {
                case '\r':
                    break;
                case '\n':
                    keystrokes.Add(Press(MacKeyCombo.ReturnKey));
                    break;
                case '\t':
                    keystrokes.Add(Press(MacKeyCombo.TabKey));
                    break;
                default:
                    if (Rune.IsControl(rune))
                    {
                        throw new ArgumentException(
                            $"The text has a control character (U+{rune.Value:X4}) at position {i}. Only \\n, \\t and \\r are allowed. Nothing was typed.",
                            nameof(text));
                    }

                    var character = text.Substring(i, used);
                    keystrokes.Add((new MacInputEvent.Text(character, Down: true), new MacInputEvent.Text(character, Down: false)));
                    break;
            }

            i += used;
        }

        return keystrokes;
    }

    private static (MacInputEvent, MacInputEvent) Press(ushort keyCode) =>
        (new MacInputEvent.Key(keyCode, Down: true, 0), new MacInputEvent.Key(keyCode, Down: false, 0));

    private async Task PressOnceAsync(MacKeyCombo combo, CancellationToken ct)
    {
        var down = new Stack<MacKey>();
        ulong flags = 0;
        try
        {
            foreach (var key in combo.Keys)
            {
                if (down.Count > 0)
                {
                    await Delay(_delays.BetweenKeys, ct).ConfigureAwait(false);
                }

                flags |= key.Flag;
                _sink.Send(new MacInputEvent.Key(key.KeyCode, Down: true, flags));
                down.Push(key);
            }

            while (down.Count > 0)
            {
                await Delay(_delays.BetweenKeys, ct).ConfigureAwait(false);
                flags = Release(down, flags);
            }
        }
        finally
        {
            // Stopped halfway: let go of everything still pressed, so no Command or Shift stays held.
            while (down.Count > 0)
            {
                flags = Release(down, flags);
            }
        }
    }

    private ulong Release(Stack<MacKey> down, ulong flags)
    {
        var key = down.Pop();
        var main = key.Flag == 0;

        // The main key comes up with the modifiers still held; a modifier comes up without its own flag.
        var after = flags & ~key.Flag;
        _sink.Send(new MacInputEvent.Key(key.KeyCode, Down: false, main ? flags : after));
        return after;
    }

    private void Remember(FrameArea area, ScreenFrame frame)
    {
        lock (_lock)
        {
            _latest = area;
        }

        LogCaptured(_logger, frame.Sequence, frame.Width, frame.Height, area.Left, area.Top);
    }

    /// <summary>Maps a point in the latest screenshot to screen points, after checking that the mouse can be controlled.</summary>
    private (double X, double Y) Target(int x, int y)
    {
        var point = Area().ToScreen(x, y);
        EnsureCanControl();
        return point;
    }

    private FrameArea Area()
    {
        lock (_lock)
        {
            return _latest ?? throw new InvalidOperationException("Take a screenshot first: clicks are given in its pixels.");
        }
    }

    private void EnsureCanControl()
    {
        if (!_canControl())
        {
            LogRejected(_logger);
            throw new ScreenUnavailableException(ScreenUnavailableReason.NoInteractiveSession, AccessibilityMissing);
        }
    }

    private Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, _time, ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Screenshot {Sequence}: {Width}×{Height} at {Left},{Top} (points)")]
    private static partial void LogCaptured(ILogger logger, long sequence, int width, int height, double left, double top);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scroll {DeltaY} down, {DeltaX} right at {X},{Y} in the screenshot → {ScreenX},{ScreenY} on screen (points)")]
    private static partial void LogScroll(ILogger logger, int deltaY, int deltaX, int x, int y, double screenX, double screenY);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Drag {Button} from {FromX},{FromY} to {ToX},{ToY} in the screenshot → {ScreenFromX},{ScreenFromY} to {ScreenToX},{ScreenToY} on screen (points)")]
    private static partial void LogDrag(
        ILogger logger, MouseButton button, int fromX, int fromY, int toX, int toY, double screenFromX, double screenFromY, double screenToX, double screenToY);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hover at {X},{Y} in the screenshot → {ScreenX},{ScreenY} on screen (points)")]
    private static partial void LogMove(ILogger logger, int x, int y, double screenX, double screenY);

    [LoggerMessage(Level = LogLevel.Information, Message = "Click {Button} ×{Clicks} at {X},{Y} in the screenshot → {ScreenX},{ScreenY} on screen (points)")]
    private static partial void LogClick(ILogger logger, MouseButton button, int clicks, int x, int y, double screenX, double screenY);

    [LoggerMessage(Level = LogLevel.Information, Message = "Type {Length} characters: {Text}")]
    private static partial void LogType(ILogger logger, int length, string text);

    [LoggerMessage(Level = LogLevel.Information, Message = "Type {Length} characters (sensitive, not logged)")]
    private static partial void LogTypeSensitive(ILogger logger, int length);

    [LoggerMessage(Level = LogLevel.Information, Message = "Press {Combo} ×{Repeat}")]
    private static partial void LogKey(ILogger logger, string combo, int repeat);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't control the mouse and keyboard: no Accessibility permission")]
    private static partial void LogRejected(ILogger logger);
}
