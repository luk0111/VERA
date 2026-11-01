namespace Vera.Core;

/// <summary>What VERA sees at one moment: the window, its numbered elements, a screenshot, and any signals.</summary>
/// <param name="ScreenshotPng">The window as a PNG, without the numbers drawn on it.</param>
public sealed record ScreenState(
    string WindowTitle,
    IReadOnlyList<UiElement> Elements,
    ReadOnlyMemory<byte> ScreenshotPng,
    IReadOnlyList<AppSignal> Signals);