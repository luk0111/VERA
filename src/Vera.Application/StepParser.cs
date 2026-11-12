using System.Text;
using System.Text.RegularExpressions;

namespace Vera.Application;

/// <summary>
/// Reads one line of a chain into a <see cref="StepCommand"/>. The first word is the command (any case); elements are described in plain words,
/// and text to type or check goes in double quotes, with <c>\"</c> for a quote and <c>\\</c> for a backslash inside. Commands:
/// <c>click the search button</c>, <c>type "cats" into the search box</c>, <c>press enter</c>,
/// <c>expect the results list</c>, <c>expect the search box contains "cats"</c>, <c>expect title contains "cats"</c>.
/// </summary>
public static partial class StepParser
{
    private const string TypeExample = "type \"cats\" into the search box";
    private const string ContainsExample = "expect the search box contains \"cats\"";

    /// <summary>The step on the line, or null for a blank line or a comment (starting with <c>#</c>).</summary>
    /// <exception cref="FormatException">The line is not a step. The message says what's wrong, with an example.</exception>
    public static StepCommand? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#'))
        {
            return null;
        }

        var space = text.IndexOfAny([' ', '\t']);
        var command = space < 0 ? text : text[..space];
        var rest = space < 0 ? "" : text[space..].Trim();
        return command.ToLowerInvariant() switch
        {
            "click" => new ClickStep(Describe(rest) ?? throw new FormatException("Say what to click, e.g. click the search button.")),
            "type" => ReadType(rest),
            "press" => ReadPress(rest),
            "expect" => ReadExpect(rest),
            _ when command.StartsWith('"') => throw new FormatException("A step starts with click, type, press or expect."),
            _ => throw new FormatException($"Unknown command \"{command}\". A step starts with click, type, press or expect."),
        };
    }

    private static TypeStep ReadType(string rest)
    {
        if (!rest.StartsWith('"'))
        {
            throw new FormatException($"Put the text in quotes, e.g. {TypeExample}.");
        }

        var (value, end) = ReadQuoted(rest, 0);
        var after = rest[end..].Trim();
        var into = IntoWord().Match(after);
        if (!into.Success)
        {
            throw new FormatException($"Say where to type with \"into\", e.g. {TypeExample}.");
        }

        return new TypeStep(value, Describe(after[into.Length..]) ?? throw new FormatException($"Say what to type into, e.g. {TypeExample}."));
    }

    // The keys are the rest of the line as written, e.g. "ctrl+s"; the input layer checks them when the step runs.
    private static PressStep ReadPress(string rest)
    {
        var keys = rest.Trim('"').Trim();
        return keys.Length == 0
            ? throw new FormatException("Say which keys to press, e.g. press enter or press ctrl+s.")
            : new PressStep(keys);
    }

    private static StepCommand ReadExpect(string rest)
    {
        var contains = ContainsAtEnd().Match(rest);
        if (contains.Success)
        {
            var target = contains.Groups["target"].Value.Trim();
            var (text, _) = ReadQuoted(contains.Groups["text"].Value, 0);
            return target.Equals("title", StringComparison.OrdinalIgnoreCase)
                ? new ExpectTitleStep(text)
                : new ExpectContainsStep(Describe(target) ?? throw new FormatException($"Say what should contain the text, e.g. {ContainsExample}."), text);
        }

        // "contains" with text that isn't quoted, or nothing after it.
        if (ContainsWord().IsMatch(rest))
        {
            throw new FormatException($"Put the text in quotes, e.g. {ContainsExample}.");
        }

        return new ExpectShownStep(Describe(rest) ?? throw new FormatException("Say what should be on the screen, e.g. expect the results list."));
    }

    // The element description, trimmed; a description in quotes loses them. Null when empty.
    private static string? Describe(string text)
    {
        var description = text.Trim();
        if (description.StartsWith('"'))
        {
            var (value, end) = ReadQuoted(description, 0);
            if (end == description.Length)
            {
                description = value.Trim();
            }
        }

        return description.Length == 0 ? null : description;
    }

    // The quoted text starting at text[start], and the index just after its closing quote.
    private static (string Value, int End) ReadQuoted(string text, int start)
    {
        var value = new StringBuilder();
        var i = start + 1;
        for (; i < text.Length && text[i] != '"'; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
            {
                i++;
            }

            value.Append(text[i]);
        }

        return i < text.Length
            ? (value.ToString(), i + 1)
            : throw new FormatException("A quote is not closed. Text goes between two \" marks.");
    }

    [GeneratedRegex(@"^into(\s+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntoWord();

    // "<target> contains "<text>"" with the quoted text at the very end of the line.
    [GeneratedRegex(@"^(?<target>.*?)\s*\bcontains\s+(?<text>""(?:[^""\\]|\\.)*"")\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ContainsAtEnd();

    [GeneratedRegex(@"\bcontains\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContainsWord();
}
