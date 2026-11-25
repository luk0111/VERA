using Vera.Core;

namespace Vera.Application;

/// <summary>
/// How far to scroll next. One notch moves a different distance in every app and system, so it is measured from how far the elements moved.
/// </summary>
internal static class ScrollDistance
{
    // The rest of the window is seen twice, so an element at the edge isn't jumped over.
    private const double ShareOfWindow = 0.6;

    /// <summary>The notches that move about 60% of the window; when it can't be measured, the same again, or half of it after a jump.</summary>
    public static int Next(ScreenState before, ScreenState after, bool vertical, int notches)
    {
        var old = Places(before, vertical);
        var now = Places(after, vertical);
        var moved = now
            .Where(p => old.ContainsKey(p.Key))
            .Select(p => Math.Abs(p.Value - old[p.Key]))
            .Where(d => d > 0)
            .Order()
            .ToList();
        if (moved.Count == 0)
        {
            // Other elements, but none that moved: probably jumped a whole window, perhaps past the element. A browser's toolbar stays, so it says nothing.
            return old.Keys.ToHashSet().SetEquals(now.Keys) ? notches : Math.Max(1, notches / 2);
        }

        // What doesn't move, e.g. a toolbar, was left out; the middle one ignores the odd element that moved by itself.
        var perNotch = (double)moved[moved.Count / 2] / notches;
        var window = before.Elements[0].Bounds;
        return (int)Math.Clamp((vertical ? window.Height : window.Width) * ShareOfWindow / perNotch, 1, StepParser.MaxNotches);
    }

    // Only elements that can't be mixed up: a page may have many links called "edit".
    private static Dictionary<(string, string, string), int> Places(ScreenState screen, bool vertical) =>
        screen.Elements
            .Where(e => e.Id >= 1)
            .GroupBy(e => (e.Role, e.Name, e.Locator))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => vertical ? g.First().Bounds.Y : g.First().Bounds.X);
}
