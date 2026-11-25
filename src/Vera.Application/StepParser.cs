using System.Text;
using System.Text.RegularExpressions;

namespace Vera.Application;

/// <summary>
/// Reads one line of a chain into a <see cref="StepCommand"/>. The first word is the command (any case); elements are described in plain words,
/// and text to type or check goes in double quotes, with <c>\"</c> for a quote and <c>\\</c> for a backslash inside. Commands:
/// <c>click the search button</c>, <c>type "cats" into the search box</c>, <c>press enter</c>,
/// <c>scroll down 3 in the results list</c>, <c>scroll down until the orange cat</c>, <c>drag the report file to the Documents folder</c>, <c>hover over the help icon</c>,
/// <c>expect the results list</c>, <c>expect the search box contains "cats"</c>, <c>expect title contains "cats"</c>.
/// </summary>
public static partial class StepParser
{
    /// <summary>The most notches one scroll step can turn, the same limit as the input layer.</summary>
    public const int MaxNotches = 50;

    private const string TypeExample = "type \"cats\" into the search box";
    private const string ContainsExample = "expect the search box contains \"cats\"";
    private const string ScrollExample = "scroll down 3 in the results list";
    private const string UntilExample = "scroll down until the orange cat";
    private const string DragExample = "drag the report file to the Documents folder";
    private const string HoverExample = "hover over the help icon";
    private const string Commands = "A step starts with click, type, press, scroll, drag, hover or expect.";

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
            "scroll" => ReadScroll(rest),
            "drag" => ReadDrag(rest),
            "hover" => ReadHover(rest),
            "expect" => ReadExpect(rest),
            _ when command.StartsWith('"') => throw new FormatException(Commands),
            _ => throw new FormatException($"Unknown command \"{command}\". {Commands}"),
        };
    }

    // scroll <up|down|left|right> <notches> in <element>: every part is needed. Or scroll <direction> until <element>.
    private static StepCommand ReadScroll(string rest)
    {
        var until = UntilParts().Match(rest);
        if (until.Success)
        {
            return new ScrollUntilStep(
                Enum.Parse<ScrollDirection>(until.Groups["direction"].Value, ignoreCase: true),
                Describe(until.Groups["target"].Value) ?? throw new FormatException($"Say what to scroll to, e.g. {UntilExample}."));
        }

        var match = ScrollParts().Match(rest);
        if (!match.Success)
        {
            throw new FormatException($"Write scroll, a direction (up, down, left or right), how many notches, then \"in\" and the element, e.g. {ScrollExample}.");
        }

        if (!int.TryParse(match.Groups["notches"].Value, out var notches) || notches is < 1 or > MaxNotches)
        {
            throw new FormatException($"Scroll between 1 and {MaxNotches} notches, e.g. {ScrollExample}.");
        }

        var direction = Enum.Parse<ScrollDirection>(match.Groups["direction"].Value, ignoreCase: true);
        return new ScrollStep(
            direction,
            notches,
            Describe(match.Groups["target"].Value) ?? throw new FormatException($"Say what to scroll in, e.g. {ScrollExample}."));
    }

    // drag <element> to <element>: split at the last " to " outside quotes, so "drag the Go to button to the toolbar" works as written.
    // A destination with " to " in it goes in quotes: drag the file to "the Go to button".
    private static DragStep ReadDrag(string rest)
    {
        var to = ToBetween().Matches(rest).LastOrDefault(m => !InsideQuotes(rest, m.Index))
            ?? throw new FormatException($"Say what to drag and where to drop it with \"to\", e.g. {DragExample}.");

        return new DragStep(
            Describe(rest[..to.Index]) ?? throw new FormatException($"Say what to drag, e.g. {DragExample}."),
            Describe(rest[(to.Index + to.Length)..]) ?? throw new FormatException($"Say where to drop it, e.g. {DragExample}."));
    }

    // True when text[index] is between an opening and a closing quote (\" doesn't count as one).
    private static bool InsideQuotes(string text, int index)
    {
        var inside = false;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\\' && inside)
            {
                i++;
            }
            else if (text[i] == '"')
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static HoverStep ReadHover(string rest)
    {
        var over = OverWord().Match(rest);
        if (!over.Success)
        {
            throw new FormatException($"Write hover over and the element, e.g. {HoverExample}.");
        }

        return new HoverStep(Describe(rest[over.Length..]) ?? throw new FormatException($"Say what to hover over, e.g. {HoverExample}."));
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

    [GeneratedRegex(@"^(?<direction>up|down|left|right)\s+(?<notches>\d+)\s+in\s+(?<target>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ScrollParts();

    // "until you see the cat" means "until the cat".
    [GeneratedRegex(@"^(?<direction>up|down|left|right)\s+until(\s+(you|i|we)\s+(can\s+)?(see|find))?(\s+(?<target>.+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex UntilParts();

    [GeneratedRegex(@"\s+to\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToBetween();

    [GeneratedRegex(@"^over(\s+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OverWord();

    // "<target> contains "<text>"" with the quoted text at the very end of the line.
    [GeneratedRegex(@"^(?<target>.*?)\s*\bcontains\s+(?<text>""(?:[^""\\]|\\.)*"")\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ContainsAtEnd();

    [GeneratedRegex(@"\bcontains\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContainsWord();
}
