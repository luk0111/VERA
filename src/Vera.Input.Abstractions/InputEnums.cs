namespace Vera.Input;

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>How <see cref="IKeyboard.TypeTextAsync"/> gets the text to the target.</summary>
public enum TextInputMethod
{
    /// <summary>Uses the configured default (Unicode key events, with clipboard paste for characters that need it).</summary>
    Auto,

    /// <summary>Types each character as a Unicode key event.</summary>
    Unicode,

    /// <summary>Puts the text on the clipboard and presses Ctrl+V.</summary>
    Clipboard,
}