namespace Vera.Core;

/// <summary>One element on the screen, numbered so the model can point at it (Set-of-Marks).</summary>
/// <param name="Id">The number drawn on the screenshot. Valid only for the <see cref="ScreenState"/> it came from.</param>
/// <param name="Role">The kind of element as text, e.g. <c>Button</c> or <c>Edit</c>, so desktop and web roles both fit.</param>
/// <param name="Name">The visible label or accessible name.</param>
/// <param name="Locator">A stable path to the element (e.g. AutomationId) that still finds it on a later screen. Replay uses it.</param>
/// <param name="Value">The text inside the element, e.g. what is typed in a field. Null when it has none.</param>
public sealed record UiElement(
    int Id,
    string Role,
    string Name,
    ScreenRect Bounds,
    bool IsEnabled,
    string Locator,
    string? Value = null);