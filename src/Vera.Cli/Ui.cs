using Spectre.Console;

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
            var api = settings.IsApiKeyConnected ? "[green]connected[/]" : "[yellow]not connected[/]";
            AnsiConsole.MarkupLine($"Model: [teal]{Markup.Escape(settings.Model)}[/]   API: {api}");
        }

        AnsiConsole.Write(new Rule().RuleStyle(Color.Grey));
        AnsiConsole.WriteLine();
    }

    /// <summary>The local model's last known status, e.g. "Connected to: Local · qwen3-vl:8b". Makes no network call.</summary>
    private static string LocalModelStatus(AppSettings settings)
    {
        var model = settings.LocalModel is { } local
            ? Markup.Escape($"{local.DisplayName} · {local.Model}")
            : Markup.Escape(settings.Model);

        return settings.ConnectionState switch
        {
            ConnectionState.Connected => $"[green]Connected to: {model}[/]",
            ConnectionState.Unreachable => $"[red]{model}  Ollama is not running[/]",
            ConnectionState.ModelMissing => $"[yellow]{model}  model not installed[/]",
            _ => $"[grey]{model}  (not checked yet)[/]",
        };
    }

    public static void Title(string title)
    {
        AnsiConsole.Write(new Rule($"[teal]{Markup.Escape(title)}[/]").LeftJustified().RuleStyle(Color.Grey));
        AnsiConsole.WriteLine();
    }

    public static void Pause()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to go back...[/]");
        Console.ReadKey(intercept: true);
    }
}
