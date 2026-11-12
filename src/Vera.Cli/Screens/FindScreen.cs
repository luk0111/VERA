using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Spectre.Console;
using Vera.Application;
using Vera.Core;
using Vera.Llm;

namespace Vera.Cli.Screens;

/// <summary>
/// Ask where something is in the selected app. VERA answers in one line and opens a screenshot with the element boxed.
/// On Windows it then offers to click the element in the app, or to type into it when it is a text field.
/// </summary>
internal static class FindScreen
{
    private const string ExitCommand = "/back";

    public static async Task ShowAsync(AppSettings settings, RunningApp app, FindElementService service)
    {
        Ui.ShowHeader(settings);
        Ui.Title("Find an element");
        AnsiConsole.MarkupLine($"[grey]Process:[/] [teal]{Markup.Escape(app.ToString())}[/]");
        AnsiConsole.MarkupLine($"[grey]Model:[/] [teal]{Markup.Escape(settings.ModelLabel)}[/]");

        AnsiConsole.MarkupLine($"[grey]Say what you are looking for, e.g. [teal]the Log in button[/]. Type [teal]{ExitCommand}[/] to return to the main menu.[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var what = AnsiConsole.Prompt(new TextPrompt<string>("[bold teal]Find >[/]").AllowEmpty()).Trim();
            if (what.Length == 0)
            {
                continue;
            }

            if (what.Equals(ExitCommand, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await FindAsync(settings, app, service, what);
            AnsiConsole.WriteLine();
        }
    }

    private static async Task FindAsync(AppSettings settings, RunningApp app, FindElementService service, string what)
    {
        FindResult result;
        try
        {
            result = await AnsiConsole.Status().StartAsync("VERA is looking…", _ => service.FindAsync(app.Id, app.WindowTitle, what, CancellationToken.None));
        }
#pragma warning disable CA1031 // Any failure of one search is shown on screen; the screen itself must stay open.
        catch (Exception e)
#pragma warning restore CA1031
        {
            ShowError(settings, e);
            return;
        }

        if (result.Element is { } element)
        {
            var x = element.Bounds.X + (element.Bounds.Width / 2);
            var y = element.Bounds.Y + (element.Bounds.Height / 2);
            AnsiConsole.MarkupLine($"[green]Found \"{Markup.Escape(element.Name)}\" (element {element.Id}) at x {x}, y {y}[/]");
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

        // Only the Windows adapter can act so far.
        if (result.Element is { } found && OperatingSystem.IsWindows())
        {
            await OfferActionAsync(app, service, found);
        }
    }

    // A text field offers typing and clicking; anything else only clicking.
    private static async Task OfferActionAsync(RunningApp app, FindElementService service, UiElement found)
    {
        var action = FindElementService.AcceptsText(found)
            ? AnsiConsole.Prompt(
                new SelectionPrompt<FoundAction>()
                    .Title("What should VERA do with it?")
                    .HighlightStyle(Ui.Highlight)
                    .AddChoices(FoundAction.Type, FoundAction.Click, FoundAction.Nothing)
                    .UseConverter(a => a switch
                    {
                        FoundAction.Type => "Type into it",
                        FoundAction.Click => "Click it",
                        _ => "Nothing",
                    }))
            : AnsiConsole.Confirm("Click it?", defaultValue: false) ? FoundAction.Click : FoundAction.Nothing;

        switch (action)
        {
            case FoundAction.Click:
                await ActAsync(
                    "VERA is clicking…",
                    "click it",
                    () => service.ClickAsync(app.Id, app.WindowTitle, found, CancellationToken.None),
                    clicked => $"Clicked \"{clicked.Name}\".");
                break;
            case FoundAction.Type:
                var text = AnsiConsole.Prompt(new TextPrompt<string>("What should VERA type? [grey](empty clears the field)[/]").AllowEmpty());
                await ActAsync(
                    "VERA is typing…",
                    "type into it",
                    () => service.TypeAsync(app.Id, app.WindowTitle, found, text, CancellationToken.None),
                    typed => text.Length == 0 ? $"Cleared \"{typed.Name}\"." : $"Typed {text.Length} characters into \"{typed.Name}\".");
                break;
        }
    }

    private static async Task ActAsync(string status, string what, Func<Task<UiElement>> act, Func<UiElement, string> done)
    {
        try
        {
            var element = await AnsiConsole.Status().StartAsync(status, _ => act());
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(done(element))}[/]");
            AnsiConsole.MarkupLine("[grey]The app is in front now. Click back into this window to find something else.[/]");
        }
#pragma warning disable CA1031 // A failed action is shown on screen; the screen itself must stay open.
        catch (Exception e)
#pragma warning restore CA1031
        {
            ShowPanel($"Could not {what}: {e.Message}");
        }
    }

    private enum FoundAction
    {
        Nothing,
        Click,
        Type,
    }

    private static void ShowError(AppSettings settings, Exception error)
    {
        var text = error switch
        {
            LlmUnauthorizedException => error.Message,
            LlmDailyQuotaException => $"{error.Message} Switch to a local model in Settings to keep working.",
            // Ollama answers 404 when the model is not installed.
            HttpRequestException { StatusCode: HttpStatusCode.NotFound } when settings.IsLocalModel =>
                $"Model '{settings.SelectedModel?.Model}' is not installed. Run: ollama pull {settings.SelectedModel?.Model}",
            HttpRequestException or IOException or OperationCanceledException => settings.IsLocalModel
                ? "Could not reach Ollama. Start it and try again."
                : $"Could not reach {settings.SelectedModel?.DisplayName}. Check your connection and try again.",
            _ => error.Message,
        };

        ShowPanel(text);
    }

    private static void ShowPanel(string text)
    {
        AnsiConsole.Write(new Panel(new Text(text, new Style(Color.Red)))
            .Header("[red]Error[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red)
            .Padding(1, 0));
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
