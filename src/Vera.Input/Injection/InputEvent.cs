using Vera.Input.Coordinates;

namespace Vera.Input.Injection;

internal enum InputEventKind
{
    MouseMove,
    MouseDown,
    MouseUp,
}

/// <summary>
/// One input event, in our own terms rather than Win32's. The drivers build lists of these and
/// <see cref="IInputSink"/> sends them, so the drivers can be tested without touching the real mouse.
/// </summary>
/// <param name="Point">Where to move to. Only used by <see cref="InputEventKind.MouseMove"/>.</param>
/// <param name="Button">Which button. Only used by <see cref="InputEventKind.MouseDown"/> and <see cref="InputEventKind.MouseUp"/>.</param>
internal readonly record struct InputEvent(InputEventKind Kind, NormalizedPoint Point = default, MouseButton Button = MouseButton.Left)
{
    public static InputEvent MouseMove(NormalizedPoint point) => new(InputEventKind.MouseMove, Point: point);

    public static InputEvent MouseDown(MouseButton button) => new(InputEventKind.MouseDown, Button: button);

    public static InputEvent MouseUp(MouseButton button) => new(InputEventKind.MouseUp, Button: button);
}
