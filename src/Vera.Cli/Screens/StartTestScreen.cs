using System.Diagnostics;
using Spectre.Console;

namespace Vera.Cli.Screens;

internal static class StartTestScreen
{
    private const string Back = "Back";

    public static void Show(AppSettings settings)
    {
        Ui.ShowHeader(settings);
        Ui.Title("Start testing an app");

        if (!settings.IsApiKeyConnected)
        {
            AnsiConsole.MarkupLine("[yellow]No API key connected yet.[/] [grey]You can add one in Settings.[/]");
            AnsiConsole.WriteLine();
        }

        var processes = RunningApps();
        if (processes.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No running apps with a window were found.[/]");
            Ui.Pause();
            return;
        }

        var process = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a process:")
                .HighlightStyle(Ui.Highlight)
                .PageSize(10)
                .MoreChoicesText("[grey](Move up and down to see more processes)[/]")
                .AddChoices(processes)
                .AddChoices(Back)
                .UseConverter(Markup.Escape));

        if (process == Back)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
                new Rows(
                    new Markup($"Process: [teal]{Markup.Escape(process)}[/]"),
                    new Markup($"Model:   [teal]{Markup.Escape(settings.Model)}[/]")))
            .Header("[bold]Ready to test[/]")
            .BorderColor(Color.Teal));

        AnsiConsole.MarkupLine("[grey]Running the test is not implemented yet.[/]");
        Ui.Pause();
    }

    // Apps with a visible main window, e.g. "notepad.exe - Untitled - Notepad".
    private static List<string> RunningApps()
    {
        var apps = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id != Environment.ProcessId && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                {
                    apps.Add($"{p.ProcessName}.exe - {p.MainWindowTitle}");
                }
            }
        }

        apps.Sort(StringComparer.OrdinalIgnoreCase);
        return apps;
    }
}
