using Vera.Core;

namespace Vera.Adapters.Desktop;

/// <summary>
/// Turns a window's raw elements into the numbered list the model points at (Set-of-Marks).
/// Element 0 is the window itself: its bounds are the screenshot's area. The elements a tester could act on are numbered from 1.
/// </summary>
internal static class ElementNumbering
{
    /// <summary>
    /// Room for big windows such as YouTube or Gmail. Each model then gets only its own share, top to bottom
    /// (<c>MaxElements</c> in its profile: 80 for the small local model, more for Gemini), so the local model's prompt stays as short as before.
    /// </summary>
    public const int MaxElements = 300;

    private static readonly HashSet<string> ActionableRoles = new(StringComparer.Ordinal)
    {
        "Button", "Edit", "CheckBox", "RadioButton", "ComboBox", "Hyperlink", "MenuItem", "TabItem", "ListItem", "Slider", "SplitButton",

        // A text area, e.g. Windows 11 Notepad's page: there is no Edit to type into.
        "Document",
    };

    /// <summary>Whether a tester can act on an element with this role, so it can be numbered (or recorded as a step).</summary>
    public static bool IsActionable(string role) => ActionableRoles.Contains(role);

    /// <summary>
    /// A web page inside the window: <c>Document</c> on Windows (UI Automation), <c>WebArea</c> on macOS (<c>AXWebArea</c>).
    /// Pages built from tables report every row and cell as a nameless list item, which nobody can ask for by name.
    /// </summary>
    private static readonly HashSet<string> PageRoles = new(StringComparer.Ordinal) { "Document", "WebArea" };

    /// <summary>How close, in screen pixels, two same-named elements' centres must be to count as one element listed twice.</summary>
    private const int SamePlace = 8;

    public static IReadOnlyList<UiElement> Number(string windowTitle, ScreenRect window, IReadOnlyList<RawElement> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var pages = candidates.Where(c => PageRoles.Contains(c.Role) && c.Bounds.Width > 0 && c.Bounds.Height > 0).Select(c => c.Bounds).ToList();
        var ordered = candidates
            .Where(c => ActionableRoles.Contains(c.Role) && !c.IsOffscreen && c.Bounds.Width > 0 && c.Bounds.Height > 0 && CentreIsInside(c.Bounds, window))
            .Where(c => !IsNamelessPageRow(c, pages))
            .OrderBy(c => c.Bounds.Y)
            .ThenBy(c => c.Bounds.X);

        var kept = new List<RawElement>();
        foreach (var c in ordered)
        {
            if (kept.Count == MaxElements)
            {
                break;
            }

            // Browsers (Safari, Opera) report a link once for itself and again through a parent; a second number for it would waste a place.
            if (!kept.Exists(k => IsSameElement(k, c)))
            {
                kept.Add(c);
            }
        }

        var elements = new List<UiElement> { new(0, "Window", windowTitle, window, IsEnabled: true, Locator: "window") };
        foreach (var c in kept)
        {
            var locator = string.IsNullOrEmpty(c.AutomationId) ? $"{c.Role}:{c.Name}" : c.AutomationId;
            elements.Add(new UiElement(elements.Count, c.Role, c.Name, c.Bounds, c.IsEnabled, locator, c.Value));
        }

        return elements;
    }

    // Rows and cells of a web page's table. A nameless row in an app's own list (e.g. Finder) stays: it can still be clicked by its number.
    private static bool IsNamelessPageRow(RawElement c, List<ScreenRect> pages) =>
        c.Role == "ListItem" && string.IsNullOrWhiteSpace(c.Name) && pages.Exists(page => CentreIsInside(c.Bounds, page));

    private static bool IsSameElement(RawElement a, RawElement b) =>
        a.Role == b.Role
        && a.Name == b.Name
        && Math.Abs(Centre(a.Bounds).X - Centre(b.Bounds).X) <= SamePlace
        && Math.Abs(Centre(a.Bounds).Y - Centre(b.Bounds).Y) <= SamePlace;

    private static (int X, int Y) Centre(ScreenRect r) => (r.X + (r.Width / 2), r.Y + (r.Height / 2));

    private static bool CentreIsInside(ScreenRect r, ScreenRect window)
    {
        var x = r.X + (r.Width / 2);
        var y = r.Y + (r.Height / 2);
        return x >= window.X && x < window.X + window.Width && y >= window.Y && y < window.Y + window.Height;
    }
}
