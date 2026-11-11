using Spectre.Console;
using Vera.Core;

namespace Vera.Cli.Screens;

/// <summary>Lets the tester pick a running app with a window. Shared by every screen that works on an app.</summary>
internal static class ProcessPicker
{
    // Menu entries shown after the process list. Negative ids never clash with a real process.
    private static readonly RunningApp Refresh = new(-1, "Refresh list", string.Empty);
    private static readonly RunningApp Back = new(-2, "Back", string.Empty);

    /// <returns>The chosen app, or null when the tester chose Back.</returns>
    public static RunningApp? Pick(AppSettings settings, IRunningAppSource runningApps, string title)
    {
        while (true)
        {
            Ui.ShowHeader(settings);
            Ui.Title(title);

            if (settings.SelectedModel is { NeedsApiKey: true } && !settings.IsApiKeyConnected)
            {
                AnsiConsole.MarkupLine("[yellow]No API key connected yet.[/] [grey]You can add one in Settings.[/]");
                AnsiConsole.WriteLine();
            }

            var apps = RunningApps(runningApps);
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
                return null;
            }

            if (choice != Refresh)
            {
                return choice;
            }
        }
    }

    // Sorted by name.
    private static List<RunningApp> RunningApps(IRunningAppSource source)
    {
        var apps = source.List().Select(app => new RunningApp(app.ProcessId, app.Name, app.WindowTitle)).ToList();
        apps.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.ToString(), b.ToString()));
        return apps;
    }
}

/// <summary>A running app the tester can pick. The id tells apart two windows with the same name.</summary>
internal sealed record RunningApp(int Id, string Name, string WindowTitle)
{
    public override string ToString() => WindowTitle.Length == 0 ? $"{Name} ({Id})" : $"{Name} ({Id}) - {WindowTitle}";
}
