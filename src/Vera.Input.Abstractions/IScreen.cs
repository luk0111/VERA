namespace Vera.Input;

// Coordinates are in the pixel space of the most recent ScreenFrame (or of the configured default scale
// before the first one). Coordinates outside it throw ArgumentOutOfRangeException; they are never clamped.

public interface IScreenshotSource
{
    /// <param name="scale">Image size relative to the screen, e.g. 0.5. <c>null</c> uses the configured default.</param>
    Task<ScreenFrame> ScreenshotAsync(double? scale = null, CancellationToken ct = default);

    /// <summary>Takes one area of the screen at full size, e.g. an app's window. Gets the same checks as <see cref="ScreenshotAsync"/>.</summary>
    /// <param name="left">The area's left edge in physical screen pixels. It can be negative on multi-monitor setups.</param>
    /// <param name="top">The area's top edge in physical screen pixels.</param>
    /// <param name="width">The area's width in pixels.</param>
    /// <param name="height">The area's height in pixels.</param>
    Task<ScreenFrame> ScreenshotAreaAsync(int left, int top, int width, int height, CancellationToken ct = default);
}

public interface IPointer
{
    /// <summary>Moves the mouse there. Also used to hover.</summary>
    Task MoveAsync(int x, int y, CancellationToken ct = default);

    Task ClickAsync(int x, int y, MouseButton button = MouseButton.Left, int clicks = 1, CancellationToken ct = default);

    /// <summary>Scrolls the window under (x, y) by wheel notches. Positive <paramref name="deltaY"/> scrolls down, positive <paramref name="deltaX"/> scrolls right.</summary>
    Task ScrollAsync(int x, int y, int deltaY, int deltaX = 0, CancellationToken ct = default);

    Task DragAsync(int fromX, int fromY, int toX, int toY, MouseButton button = MouseButton.Left, CancellationToken ct = default);
}

public interface IKeyboard
{
    /// <summary>
    /// Types <paramref name="text"/> into the focused window. <c>\n</c> presses Enter, <c>\t</c> presses Tab and
    /// <c>\r</c> is skipped.
    /// </summary>
    /// <exception cref="ArgumentException">The text has another control character. Nothing is typed.</exception>
    Task TypeTextAsync(string text, TypeTextOptions? options = null, CancellationToken ct = default);

    /// <summary>Presses a key or combo, e.g. <c>enter</c>, <c>ctrl+s</c>, <c>alt+f4</c>, <paramref name="repeat"/> times.</summary>
    /// <exception cref="InvalidKeyComboException">The combo can't be parsed or isn't allowed.</exception>
    Task KeyAsync(string combo, int repeat = 1, CancellationToken ct = default);
}

/// <summary>Screenshots, mouse and keyboard together. Depend on the smaller interfaces when you only need one part.</summary>
public interface IScreen : IScreenshotSource, IPointer, IKeyboard
{
}