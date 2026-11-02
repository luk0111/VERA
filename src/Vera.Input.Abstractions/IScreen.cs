namespace Vera.Input;

// Coordinates are in the pixel space of the most recent ScreenFrame (or of the configured default scale
// before the first one). Coordinates outside it throw ArgumentOutOfRangeException; they are never clamped.

public interface IScreenshotSource
{
    /// <param name="scale">Image size relative to the screen, e.g. 0.5. <c>null</c> uses the configured default.</param>
    Task<ScreenFrame> ScreenshotAsync(double? scale = null, CancellationToken ct = default);
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
    Task TypeTextAsync(string text, TypeTextOptions? options = null, CancellationToken ct = default);

    /// <summary>Presses a key or combo, e.g. <c>enter</c>, <c>ctrl+s</c>, <c>alt+f4</c>, <paramref name="repeat"/> times.</summary>
    /// <exception cref="InvalidKeyComboException">The combo can't be parsed or isn't allowed.</exception>
    Task KeyAsync(string combo, int repeat = 1, CancellationToken ct = default);
}

/// <summary>Screenshots, mouse and keyboard together. Depend on the smaller interfaces when you only need one part.</summary>
public interface IScreen : IScreenshotSource, IPointer, IKeyboard
{
}