using System.Text.RegularExpressions;

namespace Vera.Core;

/// <summary>
/// Handles <c>{{secret:name}}</c> placeholders. The model only ever sees the placeholder;
/// <see cref="ResolveAsync"/> fills in the real value right before typing, and <see cref="Mask"/> hides values in logs.
/// </summary>
public static partial class SecretPlaceholder
{
    /// <summary>Replaces each secret value in logs and reports.</summary>
    public const string MaskText = "***";

    [GeneratedRegex(@"\{\{secret:([A-Za-z0-9_.\-]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>The secret names in <paramref name="text"/>, each once, in order of appearance.</summary>
    public static IReadOnlyList<string> FindNames(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Pattern().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Replaces every placeholder with its value from <paramref name="store"/>, in one pass:
    /// a value that itself looks like a placeholder is not resolved again.
    /// </summary>
    /// <exception cref="SecretNotFoundException">The store has no secret for one of the names.</exception>
    public static async Task<string> ResolveAsync(string text, ISecretStore store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in FindNames(text))
        {
            values[name] = await store.GetAsync(name, ct).ConfigureAwait(false) ?? throw new SecretNotFoundException(name);
        }

        return values.Count == 0 ? text : Pattern().Replace(text, m => values[m.Groups[1].Value]);
    }

    /// <summary>Replaces every occurrence of each non-empty value in <paramref name="text"/> with <see cref="MaskText"/>.</summary>
    public static string Mask(string text, IEnumerable<string> secretValues)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(secretValues);

        // Longest first, so a value that contains a shorter one is masked whole.
        foreach (var value in secretValues.Where(v => !string.IsNullOrEmpty(v)).OrderByDescending(v => v.Length))
        {
            text = text.Replace(value, MaskText, StringComparison.Ordinal);
        }

        return text;
    }
}