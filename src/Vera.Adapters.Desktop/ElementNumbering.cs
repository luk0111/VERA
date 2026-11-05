using Vera.Core;

namespace Vera.Adapters.Desktop;

/// <summary>
/// Turns a window's raw elements into the numbered list the model points at (Set-of-Marks).
/// Element 0 is the window itself: its bounds are the screenshot's area. The elements a tester could act on are numbered from 1.
/// </summary>
internal static class ElementNumbering
{
    /// <summary>Room for big windows such as Calculator's scientific mode; more would make the model's prompt too long for its context window.</summary>
    public const int MaxElements = 80;

    private static readonly HashSet<string> ActionableRoles = new(StringComparer.Ordinal)
    {
        "Button", "Edit", "CheckBox", "RadioButton", "ComboBox", "Hyperlink", "MenuItem", "TabItem", "ListItem", "Slider", "SplitButton",
    };

    public static IReadOnlyList<UiElement> Number(string windowTitle, ScreenRect window, IReadOnlyList<RawElement> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var elements = new List<UiElement> { new(0, "Window", windowTitle, window, IsEnabled: true, Locator: "window") };
        var kept = candidates
            .Where(c => ActionableRoles.Contains(c.Role) && !c.IsOffscreen && c.Bounds.Width > 0 && c.Bounds.Height > 0 && CentreIsInside(c.Bounds, window))
            .OrderBy(c => c.Bounds.Y)
            .ThenBy(c => c.Bounds.X)
            .Take(MaxElements);

        foreach (var c in kept)
        {
            var locator = string.IsNullOrEmpty(c.AutomationId) ? $"{c.Role}:{c.Name}" : c.AutomationId;
            elements.Add(new UiElement(elements.Count, c.Role, c.Name, c.Bounds, c.IsEnabled, locator, c.Value));
        }

        return elements;
    }

    private static bool CentreIsInside(ScreenRect r, ScreenRect window)
    {
        var x = r.X + (r.Width / 2);
        var y = r.Y + (r.Height / 2);
        return x >= window.X && x < window.X + window.Width && y >= window.Y && y < window.Y + window.Height;
    }
}
