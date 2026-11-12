using System.Text.RegularExpressions;

using Vera.Core;
using Vera.Llm;

namespace Vera.Application;

/// <summary>
/// Asks the model which element of a reading the tester means, e.g. <c>the search button</c>. Shared by Find and the steps runner,
/// so both find elements the same way. The model only points at an element; what to do with it is decided elsewhere.
/// </summary>
internal static partial class ElementFinder
{
    internal const string NoElementsReason = "VERA can't see any buttons or fields in this window. Is it minimized?";

    /// <summary>The element and the model's reason; the element is null when the model found none.</summary>
    /// <exception cref="UnreadableAnswerException">The model answered twice without a valid action.</exception>
    public static async Task<(UiElement? Element, string Reason)> FindAsync(ILlmClient llm, ScreenState screen, AppTarget target, string what, CancellationToken ct)
    {
        if (screen.Elements.Count <= 1)
        {
            return (null, NoElementsReason);
        }

        // "Has been clicked", not "is found": the model answers "done" once the expected result is on screen, and a visible element is already found.
        var element = Article().IsMatch(what) ? what : $"the {what}";
        var task = new TestTask($"Click {element}", $"{char.ToUpperInvariant(element[0])}{element[1..]} has been clicked", target);
        var action = await llm.DecideNextActionAsync(task, screen, [], ct).ConfigureAwait(false);

        var found = action.Type is AgentActionType.Click or AgentActionType.Type
            ? screen.Elements.FirstOrDefault(e => e.Id > 0 && e.Id == action.TargetId)
            : null;
        return (found, action.Reason);
    }

    // The description already starts with "the", "a" or "an", e.g. "the search button".
    [GeneratedRegex(@"^(the|a|an)\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Article();

    /// <summary>The element in a tester's words, e.g. <c>button "Search"</c> or <c>field "Email"</c>.</summary>
    public static string Describe(UiElement element)
    {
        var role = element.Role switch
        {
            "Edit" => "field",
            "Hyperlink" => "link",
            "MenuItem" => "menu",
            "TabItem" => "tab",
            "ListItem" => "item",
            "ComboBox" => "combo",
            "RadioButton" => "radio",
            "SplitButton" => "button",
            _ => element.Role.ToLowerInvariant(),
        };
        return $"{role} {StepCommand.Quote(element.Name)}";
    }
}
