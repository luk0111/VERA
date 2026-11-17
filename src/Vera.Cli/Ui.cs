using System.Net;
using Spectre.Console;
using Vera.Application;
using Vera.Llm;

namespace Vera.Cli;

/// <summary>Shared look and helpers for all screens.</summary>
internal static class Ui
{
    public static Style Highlight { get; } = new(Color.Teal, decoration: Decoration.Bold);

    public static void ShowHeader(AppSettings settings)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("VERA").Color(Color.Teal));
        AnsiConsole.MarkupLine("[grey]Verified Execution & Replay Agent[/]");

        if (settings.IsLocalModel)
        {
            AnsiConsole.MarkupLine(LocalModelStatus(settings));
        }
        else
        {
            var api = !settings.IsApiKeyConnected ? "[yellow]not connected[/]"
                : settings.ConnectionState switch
                {
                    ConnectionState.Unauthorized => "[yellow]key rejected[/]",
                    ConnectionState.Unreachable => "[red]unreachable[/]",
                    ConnectionState.ModelMissing => "[yellow]model not available[/]",
                    _ => "[green]connected[/]",
                };
            AnsiConsole.MarkupLine($"Model: [teal]{ModelName(settings)}[/]   API: {api}");
        }

        // The checker's message says what to do, e.g. "Run: ollama pull qwen3-vl:8b".
        if (settings.ConnectionState is not (ConnectionState.Connected or ConnectionState.Unknown)
            && !string.IsNullOrWhiteSpace(settings.ConnectionDetail))
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(settings.ConnectionDetail)}[/]");
        }

        AnsiConsole.Write(new Rule().RuleStyle(Color.Grey));
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Checks the model before a process is picked, so a broken setup never switches to the app's window.
    /// Returns false when the tester goes back instead.
    /// </summary>
    public static async Task<bool> EnsureModelReadyAsync(AppSettings settings, Func<ChatSession> newSession, string title)
    {
        while (true)
        {
            ShowHeader(settings);
            Title(title);
            await CheckModelAsync(settings, newSession());
            if (settings.ConnectionState == ConnectionState.Connected)
            {
                return true;
            }

            ShowHeader(settings);
            Title(title);
            AnsiConsole.MarkupLine("[yellow]The model is not ready, so VERA won't open the app yet.[/] [grey]Fix it (see above), then check again.[/]");
            AnsiConsole.WriteLine();
            var again = AnsiConsole.Prompt(
                new SelectionPrompt<bool>()
                    .HighlightStyle(Highlight)
                    .AddChoices(true, false)
                    .UseConverter(retry => retry ? "Check again" : "Back"));
            if (!again)
            {
                return false;
            }
        }
    }

    /// <summary>Checks the selected model's setup (Ollama running, model pulled, API key accepted) and stores the result for the header.</summary>
    public static async Task CheckModelAsync(AppSettings settings, ChatSession session)
    {
        var status = await AnsiConsole.Status()
            .StartAsync($"Checking {Markup.Escape(session.ProviderLabel)}…", _ => session.CheckConnectionAsync(CancellationToken.None));
        settings.ConnectionState = status.Kind switch
        {
            ConnectionStatusKind.Connected => ConnectionState.Connected,
            ConnectionStatusKind.ModelMissing => ConnectionState.ModelMissing,
            ConnectionStatusKind.Unauthorized => ConnectionState.Unauthorized,
            _ => ConnectionState.Unreachable,
        };
        settings.ConnectionDetail = status.Message;
    }

    /// <summary>The local model's last known status, e.g. "Connected to: Local · qwen3-vl:8b". Makes no network call.</summary>
    private static string LocalModelStatus(AppSettings settings)
    {
        var model = ModelName(settings);

        return settings.ConnectionState switch
        {
            ConnectionState.Connected => $"[green]Connected to: {model}[/]",
            ConnectionState.Unreachable => $"[red]{model}  Ollama is not running[/]",
            ConnectionState.ModelMissing => $"[yellow]{model}  model not installed[/]",
            _ => $"[grey]{model}  (not checked yet)[/]",
        };
    }

    // e.g. "Local · qwen3-vl:8b", already escaped for markup.
    private static string ModelName(AppSettings settings) =>
        settings.SelectedModel is { } selected
            ? Markup.Escape($"{selected.DisplayName} · {selected.Model}")
            : Markup.Escape(settings.ModelLabel);

    public static void Title(string title)
    {
        AnsiConsole.Write(new Rule($"[teal]{Markup.Escape(title)}[/]").LeftJustified().RuleStyle(Color.Grey));
        AnsiConsole.WriteLine();
    }

    /// <summary>Shows an error from asking the model or the app, in the tester's words (Ollama not running, model not installed, key or quota).</summary>
    public static void ShowError(AppSettings settings, Exception error)
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

        ShowErrorPanel(text);
    }

    public static void ShowErrorPanel(string text)
    {
        AnsiConsole.Write(new Panel(new Text(text, new Style(Color.Red)))
            .Header("[red]Error[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red)
            .Padding(1, 0));
    }

    public static void Pause()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to go back...[/]");
        Console.ReadKey(intercept: true);
    }
}
