namespace Vera.Application;

/// <summary>
/// One step of a chain the tester wrote, e.g. <c>click the search button</c>. Read by <see cref="StepParser"/>.
/// The command decides the action; the model only finds the element the description means, as Find does.
/// <see cref="object.ToString"/> gives the step back as written, so secret placeholders stay placeholders.
/// </summary>
public abstract record StepCommand
{
    /// <summary>Quotes text the way a step writes it: <c>"</c> and <c>\</c> inside get a backslash.</summary>
    internal static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>Click the element the description means, e.g. <c>the search button</c>.</summary>
public sealed record ClickStep(string Target) : StepCommand
{
    public override string ToString() => $"click {Target}";
}

/// <summary>Replace the element's text with <see cref="Text"/>, which may hold <c>{{secret:name}}</c> placeholders.</summary>
public sealed record TypeStep(string Text, string Target) : StepCommand
{
    public override string ToString() => $"type {Quote(Text)} into {Target}";
}

/// <summary>Press a key or combo in the app, e.g. <c>enter</c> or <c>ctrl+s</c>.</summary>
public sealed record PressStep(string Keys) : StepCommand
{
    public override string ToString() => $"press {Keys}";
}

/// <summary>Which way a <see cref="ScrollStep"/> scrolls.</summary>
public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>Scroll inside the element by <see cref="Notches"/> wheel notches, e.g. <c>scroll down 3 in the results list</c>.</summary>
public sealed record ScrollStep(ScrollDirection Direction, int Notches, string Target) : StepCommand
{
    public override string ToString() => $"scroll {Direction.ToString().ToLowerInvariant()} {Notches} in {Target}";
}

/// <summary>Scroll the whole page a little at a time until the element shows, e.g. <c>scroll down until the orange cat</c>.</summary>
public sealed record ScrollUntilStep(ScrollDirection Direction, string Target) : StepCommand
{
    public override string ToString() => $"scroll {Direction.ToString().ToLowerInvariant()} until {Target}";
}

/// <summary>Drag the element <see cref="Source"/> means and drop it on the one <see cref="Destination"/> means.</summary>
public sealed record DragStep(string Source, string Destination) : StepCommand
{
    // The parser splits at the last " to ", so a destination with " to " in it is quoted to read back the same way.
    public override string ToString() =>
        $"drag {Source} to {(Destination.Contains(" to ", StringComparison.OrdinalIgnoreCase) ? Quote(Destination) : Destination)}";
}

/// <summary>Rest the pointer on the element so its tooltip or hover menu shows, e.g. <c>hover over the help icon</c>.</summary>
public sealed record HoverStep(string Target) : StepCommand
{
    public override string ToString() => $"hover over {Target}";
}

/// <summary>Check that the element the description means is on the screen.</summary>
public sealed record ExpectShownStep(string Target) : StepCommand
{
    public override string ToString() => $"expect {Target}";
}

/// <summary>Check that the element's text contains <see cref="Text"/>.</summary>
public sealed record ExpectContainsStep(string Target, string Text) : StepCommand
{
    public override string ToString() => $"expect {Target} contains {Quote(Text)}";
}

/// <summary>Check that the window title contains <see cref="Text"/>.</summary>
public sealed record ExpectTitleStep(string Text) : StepCommand
{
    public override string ToString() => $"expect title contains {Quote(Text)}";
}
