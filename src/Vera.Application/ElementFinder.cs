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

        // A field that already has the focus (e.g. Wikipedia's search field) looks clicked, so the model may answer "done" instead of pointing at it.
        if (action.Type is not (AgentActionType.Click or AgentActionType.Type or AgentActionType.Fail))
        {
            task = task with
            {
                Description = $"Click {element}, even if it is already selected or focused",
                ExpectedResult = $"{char.ToUpperInvariant(element[0])}{element[1..]} has been clicked one more time",
            };
            action = await llm.DecideNextActionAsync(task, screen, [], ct).ConfigureAwait(false);
        }

        var found = action.Type is AgentActionType.Click or AgentActionType.Type
            ? screen.Elements.FirstOrDefault(e => e.Id > 0 && e.Id == action.TargetId)
            : null;
        return (found, action.Reason);
    }

    /// <summary>Whether the description means the whole window, e.g. <c>the page</c>. The model can't point at it: it has no number. Only a scroll uses it.</summary>
    public static bool MeansWindow(string description) => WholeWindow().IsMatch(description);

    [GeneratedRegex(@"^\s*(the\s+)?(whole\s+|main\s+|app\s+|browser\s+)?(window|page|web\s?page|screen)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WholeWindow();

    /// <summary>
    /// The element found in an earlier reading, in a new one: same role, name and locator, the nearest to its old place. No model call.
    /// Null when it's gone.
    /// </summary>
    public static UiElement? Same(IReadOnlyList<UiElement> elements, UiElement found) =>
        elements
            .Where(e => e.Id >= 1 && e.Role == found.Role && e.Name == found.Name && e.Locator == found.Locator)
            .MinBy(e => DistanceSquared(e.Bounds, found.Bounds));

    /// <summary>What the model would see in a reading, without the text inside fields: when it's the same, the model would answer the same.</summary>
    public static string Seen(ScreenState screen) =>
        string.Join('\n', screen.Elements.Where(e => e.Id >= 1).Select(e => $"{e.Role}|{e.Name}|{e.Bounds}|{e.IsEnabled}"));

    // Between the centres of two rectangles; long, so huge screens can't overflow.
    private static long DistanceSquared(ScreenRect a, ScreenRect b)
    {
        long dx = (a.X + (a.Width / 2)) - (b.X + (b.Width / 2));
        long dy = (a.Y + (a.Height / 2)) - (b.Y + (b.Height / 2));
        return (dx * dx) + (dy * dy);
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
            "Document" => "text area",
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
