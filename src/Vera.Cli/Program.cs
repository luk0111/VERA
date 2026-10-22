using Spectre.Console;
using Vera.Cli;
using Vera.Cli.Screens;

var settings = new AppSettings();

while (true)
{
    Ui.ShowHeader(settings);

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<MainMenuOption>()
            .Title("What do you want to do?")
            .HighlightStyle(Ui.Highlight)
            .AddChoices(Enum.GetValues<MainMenuOption>())
            .UseConverter(option => option switch
            {
                MainMenuOption.StartTesting => "Start testing an app",
                MainMenuOption.Settings => "Settings",
                MainMenuOption.Exit => "Exit",
                _ => option.ToString(),
            }));

    switch (choice)
    {
        case MainMenuOption.StartTesting:
            StartTestScreen.Show(settings);
            break;
        case MainMenuOption.Settings:
            SettingsScreen.Show(settings);
            break;
        case MainMenuOption.Exit:
            AnsiConsole.MarkupLine("[grey]Booted off.[/]");
            return;
    }
}

internal enum MainMenuOption
{
    StartTesting,
    Settings,
    Exit,
}
