namespace Vera.Input;

/// <summary>
/// One screenshot. Its <see cref="Width"/> × <see cref="Height"/> is the coordinate space for the actions
/// that follow it, until the next screenshot.
/// </summary>
/// <param name="Width">PNG width in pixels.</param>
/// <param name="Height">PNG height in pixels.</param>
/// <param name="Scale"><see cref="Width"/> divided by <see cref="ScreenWidth"/>, e.g. 0.5 for a half-size image.</param>
/// <param name="ScreenWidth">Width of the captured screen area in real pixels.</param>
/// <param name="ScreenHeight">Height of the captured screen area in real pixels.</param>
/// <param name="Png">The encoded image.</param>
/// <param name="CapturedAt">When the screen was captured, in UTC.</param>
/// <param name="Sequence">Grows by 1 per capture, so logs and the journal can tie an action to its frame.</param>
public sealed record ScreenFrame(
    int Width,
    int Height,
    double Scale,
    int ScreenWidth,
    int ScreenHeight,
    byte[] Png,
    DateTimeOffset CapturedAt,
    long Sequence);

/// <param name="Sensitive">The text is a secret (e.g. a password) and is redacted in logs and the journal.</param>
public sealed record TypeTextOptions(bool Sensitive = false);