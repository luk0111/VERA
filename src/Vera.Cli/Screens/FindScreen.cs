using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Spectre.Console;
using Vera.Application;
using Vera.Llm;

namespace Vera.Cli.Screens;

/// <summary>Ask where something is in the selected app. VERA answers in one line and opens a screenshot with the element boxed.</summary>
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
