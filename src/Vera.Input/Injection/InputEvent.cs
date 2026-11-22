using Vera.Input.Coordinates;

namespace Vera.Input.Injection;

internal enum InputEventKind
{
    MouseMove,
    MouseDown,
    MouseUp,

    /// <summary>One turn of the wheel, at wherever the pointer is.</summary>
    MouseWheel,

    /// <summary>A text character (one UTF-16 unit), sent as itself, whatever the keyboard layout.</summary>
    CharDown,
    CharUp,

    /// <summary>A physical key, by its scancode (e.g. Enter).</summary>
    KeyDown,
    KeyUp,
}

/// <summary>
/// One input event, in our own terms rather than Win32's. The drivers build lists of these and
/// <see cref="IInputSink"/> sends them, so the drivers can be tested without touching the real mouse or keyboard.
/// </summary>
/// <param name="Point">Where to move to. Only used by <see cref="InputEventKind.MouseMove"/>.</param>
/// <param name="Button">Which button. Only used by <see cref="InputEventKind.MouseDown"/> and <see cref="InputEventKind.MouseUp"/>.</param>
/// <param name="Char">Which UTF-16 unit. Only used by <see cref="InputEventKind.CharDown"/> and <see cref="InputEventKind.CharUp"/>.</param>
/// <param name="ScanCode">Which key. Only used by <see cref="InputEventKind.KeyDown"/> and <see cref="InputEventKind.KeyUp"/>.</param>
/// <param name="Extended">The key is an extended one (arrows, Home, Win…). Only used with <paramref name="ScanCode"/>.</param>
/// <param name="Wheel">
/// How far the wheel turns, in Windows units (120 = one notch). Positive is up for the vertical wheel and right for the
/// horizontal one, as Windows counts it. Only used by <see cref="InputEventKind.MouseWheel"/>.
/// </param>
/// <param name="Horizontal">The horizontal wheel (tilt) instead of the vertical one. Only used with <paramref name="Wheel"/>.</param>
internal readonly record struct InputEvent(
    InputEventKind Kind,
    NormalizedPoint Point = default,
    MouseButton Button = MouseButton.Left,
    char Char = default,
    ushort ScanCode = 0,
    bool Extended = false,
    int Wheel = 0,
    bool Horizontal = false)
{
    /// <summary>One wheel notch, in Windows units.</summary>
    public const int WheelNotch = 120;

    public static InputEvent MouseMove(NormalizedPoint point) => new(InputEventKind.MouseMove, Point: point);

    public static InputEvent MouseDown(MouseButton button) => new(InputEventKind.MouseDown, Button: button);

    public static InputEvent MouseUp(MouseButton button) => new(InputEventKind.MouseUp, Button: button);

    public static InputEvent MouseWheel(int wheel, bool horizontal) => new(InputEventKind.MouseWheel, Wheel: wheel, Horizontal: horizontal);

    public static InputEvent CharDown(char c) => new(InputEventKind.CharDown, Char: c);

    public static InputEvent CharUp(char c) => new(InputEventKind.CharUp, Char: c);

    public static InputEvent KeyDown(ushort scanCode, bool extended = false) => new(InputEventKind.KeyDown, ScanCode: scanCode, Extended: extended);

    public static InputEvent KeyUp(ushort scanCode, bool extended = false) => new(InputEventKind.KeyUp, ScanCode: scanCode, Extended: extended);
}
