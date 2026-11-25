using Vera.Core;

namespace Vera.Application;

/// <summary>
/// How far to scroll next. One notch moves a different distance in every app and system, so it is measured from how far the elements moved.
/// </summary>
internal static class ScrollDistance
{
    // The rest of the window is seen twice, so an element at the edge isn't jumped over.
    private const double ShareOfWindow = 0.6;

    /// <summary>
    /// The notches that move about 60% of the window, at most twice as many as now: one wrong measurement, e.g. a page still gliding, can't become a big jump.
    /// The same again when nothing moved that can be measured, and half after a jump.
    /// </summary>
    /// <param name="forward">Scrolled down or right, so what is on the screen moves up or left.</param>
    public static int Next(ScreenState before, ScreenState after, bool vertical, bool forward, int notches)
    {
        // The page itself moves the most. A header that shrinks, or anything that moves the other way, moves less or not like the page.
        var moved = Moved(before, after, vertical).Select(d => forward ? -d : d).Where(d => d > 0).DefaultIfEmpty(0).Max();
        if (moved == 0)
        {
            return Jumped(before, after, vertical) ? Math.Max(1, notches / 2) : notches;
        }

        var window = before.Elements[0].Bounds;
        var wanted = (vertical ? window.Height : window.Width) * ShareOfWindow / ((double)moved / notches);
        return (int)Math.Clamp(wanted, 1, Math.Min(notches * 2, StepParser.MaxNotches));
    }

    /// <summary>
    /// Whether a scroll went past everything seen before, so something in between may never have been on the screen:
    /// there are other elements now and none that moved. A browser's toolbar stays, so it says nothing.
    /// </summary>
    public static bool Jumped(ScreenState before, ScreenState after, bool vertical)
    {
        var old = Places(before, vertical).Keys.ToHashSet();
        return !Moved(before, after, vertical).Any(d => d != 0) && !old.SetEquals(Places(after, vertical).Keys);
    }

    private static IEnumerable<int> Moved(ScreenState before, ScreenState after, bool vertical)
    {
        var old = Places(before, vertical);
        return Places(after, vertical).Where(p => old.ContainsKey(p.Key)).Select(p => p.Value - old[p.Key]);
    }

    // Only elements that can't be mixed up: a page may have many links called "edit".
    private static Dictionary<(string, string, string), int> Places(ScreenState screen, bool vertical) =>
        screen.Elements
            .Where(e => e.Id >= 1)
            .GroupBy(e => (e.Role, e.Name, e.Locator))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => vertical ? g.First().Bounds.Y : g.First().Bounds.X);
}
