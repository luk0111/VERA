using Spectre.Console;

namespace Vera.Cli.Screens;

/// <summary>Chat with the agent about the selected app. The agent is not connected yet, so it only gives a fixed reply.</summary>
internal static class ChatScreen
{
    private const string ExitCommand = "/back";

    private static readonly Color Neon = new(57, 255, 20);

    public static void Show(AppSettings settings, RunningApp app)
    {
        Ui.ShowHeader(settings);
        Ui.Title("Agent chat");

        AnsiConsole.Write(new Panel(
                new Grid()
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn()
                    .AddRow("[grey]Process[/]", $"[teal]{Markup.Escape(app.ToString())}[/]")
                    .AddRow("[grey]Model[/]", $"[teal]{Markup.Escape(settings.Model)}[/]"))
            .Header("[bold]Session[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Teal)
            .Expand());

        AnsiConsole.MarkupLine($"[grey]Describe what to test and press Enter. Type [teal]{ExitCommand}[/] to return to the main menu.[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var message = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold teal]You >[/]")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            if (message.Trim().Equals(ExitCommand, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AnsiConsole.Write(new Panel(new Markup($"[{Neon.ToMarkup()} bold]Agent is not implemented yet.[/]"))
                .Header($"[{Neon.ToMarkup()}]VERA[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Neon)
                .Padding(1, 0));
            AnsiConsole.WriteLine();
        }
    }
}
