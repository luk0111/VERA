using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Vera.Application;
using Vera.Core;

namespace Vera.Cli.Screens;

/// <summary>
/// Find, in the chat and while writing steps: ask where something is in the app. VERA answers in one line and opens a screenshot with the element boxed.
/// What was found can be added as a step, which only saves it, or used at once: clicked, or typed into when it is a text field.
/// </summary>
internal static class FindInSteps
{
    public const string Command = "find";
    private const string ExitCommand = "/back";

    /// <summary>What to look for when the line is a find, e.g. <c>the search button</c> in <c>find the search button</c>; empty for <c>find</c> alone. Null for any other line.</summary>
    public static string? Asked(string line)
    {
        var space = line.IndexOfAny([' ', '\t']);
        var word = space < 0 ? line : line[..space];
        return word.Equals(Command, StringComparison.OrdinalIgnoreCase) ? (space < 0 ? "" : line[space..].Trim().Trim('"').Trim()) : null;
    }

    /// <summary>The same in the chat, where "find" is also an ordinary word: <c>/find …</c>, or a short find (<see cref="ChatFind"/>). Null for anything else.</summary>
    public static string? AskedInChat(string message) => message.StartsWith('/') ? Asked(message[1..]) : ChatFind.Asked(message);

    /// <returns>The step the tester chose to add, null when they added none; and whether VERA clicked or typed in the app.</returns>
    public static async Task<(StepCommand? Step, bool Acted)> RunAsync(AppSettings settings, RunningApp app, FindElementService service, string what)
    {
        if (what.Length == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Say what to find, e.g. {Command} the search button.[/]");
            return (null, false);
        }

        FindResult result;
        try
        {
            result = await AnsiConsole.Status().StartAsync("VERA is looking…", _ => service.FindAsync(app.Id, app.WindowTitle, what, CancellationToken.None));
        }
#pragma warning disable CA1031 // Any failure of one search is shown on screen; the steps written so far must stay.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Ui.ShowError(settings, e);
            return (null, false);
        }

        if (result.Element is { } element)
        {
            AnsiConsole.MarkupLine($"[green]Found {Markup.Escape(ElementFinder.Describe(element))}[/] [grey](element {element.Id})[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]I can't find \"{Markup.Escape(what)}\" on this screen.[/]");
        }

        if (result.Reason.Length > 0)
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(result.Reason)}[/]");
        }

        AnsiConsole.MarkupLine($"[grey]Saved: {Markup.Escape(result.ImagePath)}[/]");
        Open(result.ImagePath);
        return result.Element is { } found ? await ChooseAsync(settings, app, service, result, found, what) : (null, false);
    }

    private static async Task<(StepCommand? Step, bool Acted)> ChooseAsync(
        AppSettings settings, RunningApp app, FindElementService service, FindResult result, UiElement found, string what)
    {
        var acceptsText = FindElementService.AcceptsText(found);
        List<FoundAction> choices = [FoundAction.AddClick];
        if (acceptsText)
        {
            choices.Add(FoundAction.AddType);
        }

        choices.AddRange([FoundAction.AddExpect, FoundAction.ClickNow]);
        if (acceptsText)
        {
            choices.Add(FoundAction.TypeNow);
        }

        choices.Add(FoundAction.Nothing);
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<FoundAction>()
                .Title("What should VERA do with it?")
                .HighlightStyle(Ui.Highlight)
                .AddChoices(choices)
                .UseConverter(a => a switch
                {
                    FoundAction.AddClick => "Add a step: click it",
                    FoundAction.AddType => "Add a step: type into it",
                    FoundAction.AddExpect => "Add a step: expect it on the screen",
                    FoundAction.ClickNow => "Click it now",
                    FoundAction.TypeNow => "Type into it now",
                    _ => "Nothing",
                }));

        switch (action)
        {
            case FoundAction.AddClick:
                return (Added(FoundStep.Click(result, what)), false);
            case FoundAction.AddExpect:
                return (Added(FoundStep.Expect(result, what)), false);
            case FoundAction.AddType:
                return (AskText() is { } stepText ? Added(FoundStep.Type(result, what, stepText)) : null, false);
            case FoundAction.ClickNow:
                if (settings.AskBeforeRiskyActions && FindElementService.IsRisky(found, settings.Rules)
                    && !AnsiConsole.Confirm($"[yellow]\"{Markup.Escape(found.Name)}\" may change or send something. Click it?[/]", defaultValue: false))
                {
                    return (null, false);
                }

                await ActAsync(
                    "VERA is clicking…",
                    "click it",
                    () => service.ClickAsync(app.Id, app.WindowTitle, found, CancellationToken.None),
                    clicked => $"Clicked \"{clicked.Name}\".");
                return (null, true);
            case FoundAction.TypeNow:
                await TypeNowAsync(app, service, found);
                return (null, true);
            default:
                return (null, false);
        }
    }

    private static StepCommand? Added(StepCommand? step)
    {
        if (step is null)
        {
            AnsiConsole.MarkupLine("[yellow]VERA can't write that as a step. Write it yourself, in your own words.[/]");
        }

        return step;
    }

    private static async Task TypeNowAsync(RunningApp app, FindElementService service, UiElement found)
    {
        if (AskText() is not { } text)
        {
            return;
        }

        // Only a run fills in saved secrets; here the placeholder itself would be typed.
        if (SecretPlaceholder.FindNames(text).Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Saved secrets are only filled in when steps run. Add it as a step instead.[/]");
            return;
        }

        await ActAsync(
            "VERA is typing…",
            "type into it",
            () => service.TypeAsync(app.Id, app.WindowTitle, found, text, CancellationToken.None),
            typed => text.Length == 0 ? $"Cleared \"{typed.Name}\"." : $"Typed {text.Length} characters into \"{typed.Name}\".");
    }

    // Null for /back.
    private static string? AskText()
    {
        var text = AnsiConsole.Prompt(
            new TextPrompt<string>($"What should VERA type? [grey](empty clears the field, {ExitCommand} types nothing)[/]").AllowEmpty());
        return text.Trim().Equals(ExitCommand, StringComparison.OrdinalIgnoreCase) ? null : text;
    }

    private static async Task ActAsync(string status, string what, Func<Task<UiElement>> act, Func<UiElement, string> done)
    {
        try
        {
            var element = await AnsiConsole.Status().StartAsync(status, _ => act());
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(done(element))}[/]");
            AnsiConsole.MarkupLine("[grey]The app is in front now. Click back into this window to continue.[/]");
        }
#pragma warning disable CA1031 // A failed action is shown on screen; the screen itself must stay open.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Ui.ShowErrorPanel($"Could not {what}: {e.Message}");
        }
    }

    private enum FoundAction
    {
        AddClick,
        AddType,
        AddExpect,
        ClickNow,
        TypeNow,
        Nothing,
    }

    // Opens the screenshot in the default image viewer; without one, the saved path above is enough.
    private static void Open(string path)
    {
        try
        {
            using var viewer = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
        }
    }
}
