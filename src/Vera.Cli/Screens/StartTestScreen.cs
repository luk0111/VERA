using System.Diagnostics;
using Spectre.Console;

namespace Vera.Cli.Screens;

internal static class StartTestScreen
{
    // Menu entries shown after the process list. Negative ids never clash with a real process.
    private static readonly RunningApp Refresh = new(-1, "Refresh list", string.Empty);
    private static readonly RunningApp Back = new(-2, "Back", string.Empty);

    public static void Show(AppSettings settings)
    {
        while (true)
        {
            Ui.ShowHeader(settings);
            Ui.Title("Start testing an app");

            if (!settings.IsApiKeyConnected)
            {
                AnsiConsole.MarkupLine("[yellow]No API key connected yet.[/] [grey]You can add one in Settings.[/]");
                AnsiConsole.WriteLine();
            }

            var apps = RunningApps();
            if (apps.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No running apps with a window were found.[/] [grey]Start one and choose Refresh list.[/]");
                AnsiConsole.WriteLine();
            }

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<RunningApp>()
                    .Title("Select a process:")
                    .HighlightStyle(Ui.Highlight)
                    .PageSize(10)
                    .MoreChoicesText("[grey](Move up and down to see more processes)[/]")
                    .AddChoices(apps)
                    .AddChoices(Refresh, Back)
                    .UseConverter(app => app == Refresh || app == Back
                        ? $"[grey]{app.Name}[/]"
                        : Markup.Escape(app.ToString())));

            if (choice == Back)
            {
                return;
            }

            if (choice == Refresh)
            {
                continue;
            }

            ChatScreen.Show(settings, choice);
            return;
        }
    }

    // Apps with a visible main window, sorted by name, excluding VERA itself.
    private static List<RunningApp> RunningApps()
    {
        var apps = new List<RunningApp>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        apps.Add(new RunningApp(process.Id, process.ProcessName, process.MainWindowTitle));
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the list was being read.
                }
            }
        }

        apps.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.ToString(), b.ToString()));
        return apps;
    }
}

/// <summary>A running app the tester can pick. The id tells apart two windows with the same name.</summary>
internal sealed record RunningApp(int Id, string Name, string WindowTitle)
{
    public override string ToString() => $"{Name}.exe ({Id}) - {WindowTitle}";
}
