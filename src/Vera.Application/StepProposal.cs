using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Vera.Core;

namespace Vera.Application;

/// <summary>
/// The steps in a chat reply, between <c>&lt;steps&gt;</c> and <c>&lt;/steps&gt;</c>. A model wrote them, perhaps after reading text from the app, so they are only
/// a proposal: every line must be a step VERA knows (<see cref="StepParser"/>), and nothing runs until the tester has seen them and says so.
/// </summary>
/// <param name="Text">The reply without the steps, as the tester sees it.</param>
/// <param name="Steps">Empty when the reply proposes none, or when they can't be used.</param>
/// <param name="Problem">Why the proposed steps can't be used; null when they can, or when there are none.</param>
public sealed partial record StepProposal(string Text, IReadOnlyList<StepCommand> Steps, string? Problem = null)
{
    /// <summary>More than this is a plan the tester can't check at a glance.</summary>
    public const int MaxSteps = 20;

    private const string Open = "<steps>";

    /// <summary>Reads a finished reply. One bad line drops them all: a test with a step missing would check something else.</summary>
    /// <param name="apiKeyNames">Saved secrets that are VERA's own keys. A step that would type one into the app is refused.</param>
    public static StepProposal Read(string reply, IReadOnlyCollection<string> apiKeyNames)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(apiKeyNames);
        var blocks = Block().Matches(reply);
        var text = Visible(reply);
        if (blocks.Count == 0)
        {
            return new StepProposal(text, [], reply.Contains(Open, StringComparison.OrdinalIgnoreCase) ? "the list of steps was cut off." : null);
        }

        if (blocks.Count > 1)
        {
            return new StepProposal(text, [], "there was more than one list of steps.");
        }

        var steps = new List<StepCommand>();
        foreach (var raw in blocks[0].Groups["lines"].Value.Split('\n'))
        {
            var line = ListMark().Replace(raw.Trim(), "");
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            if (HiddenValue().IsMatch(line))
            {
                return new StepProposal(text, [], $"\"{line}\" uses a value that is hidden for privacy.");
            }

            if (line.EnumerateRunes().Any(IsInvisible))
            {
                return new StepProposal(text, [], "a step had invisible characters in it.");
            }

            // Text in the app could talk the model into this; the tester would see only the secret's name.
            if (SecretPlaceholder.FindNames(line).Any(n => apiKeyNames.Contains(n, StringComparer.OrdinalIgnoreCase)))
            {
                return new StepProposal(text, [], "a step would type VERA's own API key into the app.");
            }

            try
            {
                if (StepParser.Parse(line) is { } step)
                {
                    // The model means a button; a tester's typo is only told when it runs.
                    if (step is PressStep press && !StepParser.IsCombo(press.Keys) && !StepParser.IsKeyName(press.Keys))
                    {
                        return new StepProposal(text, [], $"\"{line}\" presses something that isn't a key. For a button or link write click, e.g. click {press.Keys}.");
                    }

                    steps.Add(step);
                }
            }
            catch (FormatException e)
            {
                return new StepProposal(text, [], $"\"{line}\" isn't a step VERA knows. {e.Message}");
            }
        }

        return steps.Count switch
        {
            0 => new StepProposal(text, [], "the list of steps was empty."),
            > MaxSteps => new StepProposal(text, [], $"there were {steps.Count} steps, more than {MaxSteps}."),
            _ => new StepProposal(text, steps),
        };
    }

    /// <summary>Whether a step checks anything. Without one, "passed" only means that VERA could do the steps.</summary>
    public bool ChecksSomething => Steps.Any(s => s is ExpectShownStep or ExpectContainsStep or ExpectTitleStep);

    /// <summary>What of a reply to show, also while it is still arriving: everything before the steps, without a tag that has only partly arrived.</summary>
    public static string Visible(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var start = reply.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            var after = Block().Match(reply) is { Success: true } block ? reply[(block.Index + block.Length)..] : "";
            return (reply[..start].TrimEnd() + (string.IsNullOrWhiteSpace(after) || after.Contains(Open, StringComparison.OrdinalIgnoreCase) ? "" : "\n" + after.Trim())).Trim();
        }

        for (var length = Math.Min(Open.Length - 1, reply.Length); length > 0; length--)
        {
            if (reply.EndsWith(Open[..length], StringComparison.OrdinalIgnoreCase))
            {
                return reply[..^length].TrimEnd();
            }
        }

        return reply;
    }

    /// <summary>
    /// Whether a step may change or send something, so the tester looks at it before running. Wider than what a run asks about:
    /// a run asks only before clicks and drops on risky names, and nothing at all before keys.
    /// </summary>
    public static bool DeservesALook(StepCommand step) => step switch
    {
        PressStep p => RiskyKeys().IsMatch(p.Keys),
        ClickStep c => RiskyWords.IsRisky(c.Target),
        DragStep => true,
        TypeStep t => SecretPlaceholder.FindNames(t.Text).Count > 0,
        _ => false,
    };

    // Characters the tester can't see, or that hide or reorder text (zero width, right to left, tags): they couldn't check such a step by reading it.
    // Whole characters, not halves: some are two chars long.
    private static bool IsInvisible(Rune c) => c.Value != '\t' && Rune.GetUnicodeCategory(c) is
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
        or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate
        || c.Value is 0x115F or 0x1160 or 0x3164 or 0xFFA0 or 0x2800;

    [GeneratedRegex(@"<steps>(?<lines>.*?)</steps>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Block();

    // "1. ", "2) ", "- " in front of a step.
    [GeneratedRegex(@"^(\d+[.)]|[-*•])\s+")]
    private static partial Regex ListMark();

    [GeneratedRegex(@"\[(email|phone|card number|code|hidden)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiddenValue();

    // Enter sends, Delete removes, and a combo can do anything (ctrl+s, alt+f4).
    [GeneratedRegex(@"\+|\b(enter|return|delete|del|backspace)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RiskyKeys();
}
