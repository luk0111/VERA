using System.Text.RegularExpressions;

namespace Vera.Application;

/// <summary>
/// Tells whether clicking an element could change or send something the tester may not want, from words in its name.
/// Used when Settings' "Ask before risky actions" is on. Whole words only, any case: "Save as" is risky, "Saved" is not.
/// </summary>
internal static partial class RiskyWords
{
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "remove", "erase", "discard", "save", "submit", "send", "pay", "buy", "purchase", "order",
        "confirm", "publish", "post", "uninstall", "reset", "format",

        // Romanian, with and without diacritics.
        "șterge", "sterge", "salvează", "salveaza", "trimite", "plătește", "plateste", "cumpără", "cumpara", "confirmă", "confirma",
    };

    public static bool IsRisky(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return NotALetter().Split(name).Any(Words.Contains);
    }

    [GeneratedRegex(@"[^\p{L}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotALetter();
}
