using Vera.Core;

namespace Vera.Application;

/// <summary>
/// Writes a step for an element the tester found, e.g. <c>click button "Search"</c>, so it can be kept and run later.
/// The element is named as a recorded step names it; when that name wouldn't point back at this one element, the tester's own words are used.
/// </summary>
public static class FoundStep
{
    /// <param name="what">What the tester asked Find for, e.g. <c>the search button</c>.</param>
    /// <returns>Null when neither the element's name nor the tester's words can be written as a step.</returns>
    public static StepCommand? Click(FindResult found, string what) => Write(found, what, target => new ClickStep(target));

    /// <inheritdoc cref="Click"/>
    public static StepCommand? Expect(FindResult found, string what) => Write(found, what, target => new ExpectShownStep(target));

    /// <inheritdoc cref="Click"/>
    public static StepCommand? Type(FindResult found, string what, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Write(found, what, target => new TypeStep(text, target));
    }

    private static StepCommand? Write(FindResult found, string what, Func<string, StepCommand> step)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(what);
        if (found.Element is not { } element)
        {
            return null;
        }

        var useName = !string.IsNullOrWhiteSpace(element.Name) && !found.SameNameElsewhere;
        return (useName ? ReadsBack(step(ElementFinder.Describe(element))) : null) ?? ReadsBack(step(what.Trim()));
    }

    // A step is saved as one line of text: one that would read back as something else (a line break, or the word "contains" in a name) is no use.
    private static StepCommand? ReadsBack(StepCommand step)
    {
        var line = step.ToString();
        if (line.Contains('\n', StringComparison.Ordinal) || line.Contains('\r', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return step.Equals(StepParser.Parse(line)) ? step : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
