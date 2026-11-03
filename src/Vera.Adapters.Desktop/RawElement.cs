using Vera.Core;

namespace Vera.Adapters.Desktop;

/// <summary>One element as UI Automation reports it, before VERA filters and numbers it.</summary>
/// <param name="Role">The control type as text, e.g. <c>Button</c>.</param>
/// <param name="Bounds">Physical screen pixels.</param>
internal sealed record RawElement(string Role, string Name, ScreenRect Bounds, bool IsEnabled, bool IsOffscreen, string AutomationId, string? Value);
