using System.Diagnostics.CodeAnalysis;

namespace Vera.Application;

/// <summary>A <c>/file</c> message from the chat, split into the path, the PDF pages and the question that follows.</summary>
/// <param name="Path">The path, without its quotes.</param>
/// <param name="Pages">The text after <c>pages:</c>, like <c>"4-6"</c>; not set means none were asked for. The file reader checks it.</param>
/// <param name="Question">What the tester wants to ask about the file; not set means they only attached it.</param>
public sealed record FileCommand(string Path, string? Pages, string? Question)
{
    public const string Name = "/file";

    /// <summary>The syntax, for help text.</summary>
    public const string Usage = "/file <path> [pages:N or pages:N-M] [question]";

    private const string PagesPrefix = "pages:";

    /// <summary>True for <c>/file</c> alone or followed by a space; <c>/files</c> is not the command.</summary>
    public static bool IsFileCommand(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.StartsWith(Name, StringComparison.OrdinalIgnoreCase)
            && (message.Length == Name.Length || char.IsWhiteSpace(message[Name.Length]));
    }

    /// <summary>
    /// Reads <c>/file &lt;path&gt; [pages:N|pages:N-M] [question]</c>. The path may be in double or single quotes, which a path with spaces needs;
    /// without quotes it ends at the first space. <c>pages:</c> is only recognised right after the path.
    /// </summary>
    /// <param name="error">Why the message could not be read, in plain words.</param>
    public static bool TryParse(string message, [NotNullWhen(true)] out FileCommand? command, out string error)
    {
        ArgumentNullException.ThrowIfNull(message);
        command = null;
        error = "";

        if (!IsFileCommand(message))
        {
            error = $"This is not a {Name} message.";
            return false;
        }

        var rest = message[Name.Length..].Trim();
        string path;
        if (rest.Length > 0 && rest[0] is '"' or '\'')
        {
            var close = rest.IndexOf(rest[0], 1);
            if (close < 0)
            {
                error = "The closing quote of the path is missing.";
                return false;
            }

            path = rest[1..close].Trim();
            rest = rest[(close + 1)..].TrimStart();
        }
        else
        {
            (path, rest) = SplitFirstWord(rest);
        }

        if (path.Length == 0)
        {
            error = "Say which file to attach.";
            return false;
        }

        string? pages = null;
        var (word, afterWord) = SplitFirstWord(rest);
        if (word.StartsWith(PagesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            pages = word[PagesPrefix.Length..];
            if (pages.Length == 0)
            {
                error = $"Give the pages right after '{PagesPrefix}', like {PagesPrefix}2-4.";
                return false;
            }

            rest = afterWord;
        }

        command = new FileCommand(path, pages, rest.Length > 0 ? rest : null);
        return true;
    }

    private static (string Word, string Remainder) SplitFirstWord(string text)
    {
        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return (text[..end], text[end..].TrimStart());
    }
}
