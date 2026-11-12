namespace Vera.Application;

/// <summary>
/// One step of a chain the tester wrote, e.g. <c>click button "Search"</c>. Read by <see cref="StepParser"/>.
/// <see cref="object.ToString"/> gives the step back as written, so secret placeholders stay placeholders.
/// </summary>
public abstract record StepCommand
{
    /// <summary>Quotes text the way a step writes it: <c>"</c> and <c>\</c> inside get a backslash.</summary>
    internal static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>Click the element.</summary>
public sealed record ClickStep(ElementQuery Element) : StepCommand
{
    public override string ToString() => $"click {Element}";
}

/// <summary>Replace the element's text with <see cref="Text"/>, which may hold <c>{{secret:name}}</c> placeholders.</summary>
public sealed record TypeStep(string Text, ElementQuery Element) : StepCommand
{
    public override string ToString() => $"type {Quote(Text)} into {Element}";
}

/// <summary>Press a key or combo in the app, e.g. <c>enter</c> or <c>ctrl+s</c>.</summary>
public sealed record PressStep(string Keys) : StepCommand
{
    public override string ToString() => $"press {Keys}";
}

/// <summary>Check that an element with that name is on the screen.</summary>
public sealed record ExpectShownStep(ElementQuery Element) : StepCommand
{
    public override string ToString() => $"expect {Element}";
}

/// <summary>Check that the element's text contains <see cref="Text"/>.</summary>
public sealed record ExpectContainsStep(ElementQuery Element, string Text) : StepCommand
{
    public override string ToString() => $"expect {Element} contains {Quote(Text)}";
}

/// <summary>Check that the window title contains <see cref="Text"/>.</summary>
public sealed record ExpectTitleStep(string Text) : StepCommand
{
    public override string ToString() => $"expect title contains {Quote(Text)}";
}

/// <summary>Which element a step means: its name, and optionally a role word such as <c>button</c>.</summary>
/// <param name="Name">The element's name as the tester wrote it.</param>
/// <param name="RoleWord">A word from <see cref="StepRoles"/>, or null for any role.</param>
public sealed record ElementQuery(string Name, string? RoleWord = null)
{
    /// <summary>The UI Automation roles the role word stands for; empty for any role.</summary>
    public IReadOnlyList<string> Roles => RoleWord is null ? [] : StepRoles.Words[RoleWord];

    public override string ToString() => RoleWord is null ? StepCommand.Quote(Name) : $"{RoleWord} {StepCommand.Quote(Name)}";
}

/// <summary>The role words a step can use, and the UI Automation roles each stands for.</summary>
public static class StepRoles
{
    public static IReadOnlyDictionary<string, string[]> Words { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["button"] = ["Button", "SplitButton"],
        ["field"] = ["Edit"],
        ["link"] = ["Hyperlink"],
        ["checkbox"] = ["CheckBox"],
        ["radio"] = ["RadioButton"],
        ["combo"] = ["ComboBox"],
        ["menu"] = ["MenuItem"],
        ["tab"] = ["TabItem"],
        ["item"] = ["ListItem"],
        ["slider"] = ["Slider"],
    };
}
