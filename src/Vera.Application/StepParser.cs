using System.Text;

namespace Vera.Application;

/// <summary>
/// Reads one line of a chain into a <see cref="StepCommand"/>. Keywords and role words ignore case; names and text go in double quotes,
/// with <c>\"</c> for a quote and <c>\\</c> for a backslash inside. Commands:
/// <c>click [role] "name"</c>, <c>type "text" into [role] "name"</c>, <c>press keys</c>,
/// <c>expect [role] "name"</c>, <c>expect [role] "name" contains "text"</c>, <c>expect title contains "text"</c>.
/// </summary>
public static class StepParser
{
    private static readonly string RoleList = string.Join(", ", StepRoles.Words.Keys);

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

        var tokens = new Tokens(Tokenize(text));
        var command = tokens.Next();
        if (command is not { Quoted: false })
        {
            throw new FormatException("A step starts with click, type, press or expect.");
        }

        StepCommand step = command.Value.ToLowerInvariant() switch
        {
            "click" => new ClickStep(ReadElement(tokens, "click \"Search\"")),
            "type" => ReadType(tokens),
            "press" => ReadPress(text, command),
            "expect" => ReadExpect(tokens),
            _ => throw new FormatException($"Unknown command \"{command.Value}\". A step starts with click, type, press or expect."),
        };

        if (step is not PressStep && tokens.Next() is { } extra)
        {
            throw new FormatException($"Unexpected \"{extra.Value}\" at the end of the step.");
        }

        return step;
    }

    private static TypeStep ReadType(Tokens tokens)
    {
        const string Example = "type \"cats\" into \"Search\"";
        var value = tokens.Next() is { Quoted: true } quoted
            ? quoted.Value
            : throw new FormatException($"Put the text in quotes, e.g. {Example}.");
        if (tokens.Next() is not { Quoted: false } into || !into.Value.Equals("into", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Say where to type with \"into\", e.g. {Example}.");
        }

        return new TypeStep(value, ReadElement(tokens, Example));
    }

    // The keys are the rest of the line as written, e.g. "ctrl+s"; the input layer checks them when the step runs.
    private static PressStep ReadPress(string line, Token command)
    {
        var keys = line[(command.End)..].Trim().Trim('"').Trim();
        return keys.Length == 0
            ? throw new FormatException("Say which keys to press, e.g. press enter or press ctrl+s.")
            : new PressStep(keys);
    }

    private static StepCommand ReadExpect(Tokens tokens)
    {
        if (tokens.Peek() is { Quoted: false } word && word.Value.Equals("title", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Next();
            return new ExpectTitleStep(ReadContains(tokens, "expect title contains \"cats\"")
                ?? throw new FormatException("Say what the title should contain, e.g. expect title contains \"cats\"."));
        }

        var element = ReadElement(tokens, "expect \"Results\"");
        return ReadContains(tokens, "expect field \"Search\" contains \"cats\"") is { } contained
            ? new ExpectContainsStep(element, contained)
            : new ExpectShownStep(element);
    }

    // "contains" followed by quoted text, or null when the step ends here.
    private static string? ReadContains(Tokens tokens, string example)
    {
        if (tokens.Peek() is null)
        {
            return null;
        }

        if (tokens.Next() is not { Quoted: false } contains || !contains.Value.Equals("contains", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Expected \"contains\", e.g. {example}.");
        }

        return tokens.Next() is { Quoted: true } quoted
            ? quoted.Value
            : throw new FormatException($"Put the text in quotes, e.g. {example}.");
    }

    // An optional role word, then the quoted name.
    private static ElementQuery ReadElement(Tokens tokens, string example)
    {
        string? role = null;
        if (tokens.Peek() is { Quoted: false } word)
        {
            tokens.Next();
            role = word.Value.ToLowerInvariant();
            if (!StepRoles.Words.ContainsKey(role))
            {
                throw new FormatException($"Unknown role \"{word.Value}\". Use one of: {RoleList}. Or put the name in quotes, e.g. {example}.");
            }
        }

        var name = tokens.Next() is { Quoted: true } quoted
            ? quoted.Value.Trim()
            : throw new FormatException($"Put the name in quotes, e.g. {example}.");
        return name.Length == 0
            ? throw new FormatException("The name in quotes can't be empty.")
            : new ElementQuery(name, role);
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            if (text[i] == '"')
            {
                var value = new StringBuilder();
                var j = i + 1;
                for (; j < text.Length && text[j] != '"'; j++)
                {
                    if (text[j] == '\\' && j + 1 < text.Length && text[j + 1] is '"' or '\\')
                    {
                        j++;
                    }

                    value.Append(text[j]);
                }

                if (j >= text.Length)
                {
                    throw new FormatException("A quote is not closed. Names and text go between two \" marks.");
                }

                tokens.Add(new Token(value.ToString(), Quoted: true, End: j + 1));
                i = j + 1;
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"')
            {
                i++;
            }

            tokens.Add(new Token(text[start..i], Quoted: false, End: i));
        }

        return tokens;
    }

    private sealed record Token(string Value, bool Quoted, int End);

    private sealed class Tokens(List<Token> items)
    {
        private int _next;

        public Token? Peek() => _next < items.Count ? items[_next] : null;

        public Token? Next() => _next < items.Count ? items[_next++] : null;
    }
}
