namespace Vera.Files;

internal static class TextCutter
{
    /// <summary>
    /// Cuts <paramref name="text"/> to at most <paramref name="maxChars"/> characters, at a line break when one is in the second half of the kept text.
    /// Never splits a surrogate pair.
    /// </summary>
    public static (string Text, bool Cut) Cut(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return (text, false);
        }

        var kept = text[..maxChars];
        var lineBreak = kept.LastIndexOf('\n');
        if (lineBreak > maxChars / 2)
        {
            kept = kept[..lineBreak].TrimEnd('\r');
        }
        else if (kept.Length > 0 && char.IsHighSurrogate(kept[^1]))
        {
            kept = kept[..^1];
        }

        return (kept, true);
    }
}
