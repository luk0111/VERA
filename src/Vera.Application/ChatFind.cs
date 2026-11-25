using System.Text.RegularExpressions;

namespace Vera.Application;

/// <summary>
/// Tells a short "find the search button" in the chat, which shows where it is, from a message for the model:
/// a test ("find the cat and click it", "find the cat, scroll if you have to"), a question, or a search for bugs or in a file.
/// </summary>
public static partial class ChatFind
{
    /// <returns>What to show, e.g. <c>the search button</c>; null when the message is for the model.</returns>
    public static string? Asked(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var find = Find().Match(message);
        return find.Success && !More().IsMatch(message) && !NotAnElement().IsMatch(message)
            ? find.Groups["what"].Value.Trim().TrimEnd('.', '!').Trim('"').Trim()
            : null;
    }

    [GeneratedRegex(@"^\s*find\s+(?<what>(the|a|an)\s+\S.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Find();

    // More than showing it: something to do or check, or a condition.
    [GeneratedRegex(
        @"\?\s*$|\b(scroll|until|even\s+if|even\s+when|if\s+(you|it|there|we|i)|make\s+sure|check\s+(that|if|whether))\b"
        + @"|(\band\b|\bthen\b|,)\s*(then\s+|also\s+|please\s+)*(click|press|type|write|enter|open|check|verify|see|look|tell|show|hover|drag|select|choose|go|delete|close|search|expect|test|use)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex More();

    // "find a bug", "find the problem with logging in", "find the price in the file", "find a way to log in".
    [GeneratedRegex(
        @"^\s*find\s+(the|a|an)\s+(\w+\s+)?(bugs?|problems?|issues?)\s*($|[.!,]|(in|with|on|about|when)\b)|\bin\s+(the|this|my)\s+(\w+\s+)?(file|pdf|document|text)\b|^\s*find\s+(the|a)\s+way\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotAnElement();
}
