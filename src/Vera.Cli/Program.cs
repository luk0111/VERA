using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Vera.Application;
using Vera.Cli;
using Vera.Cli.Screens;

ServiceProvider provider;
try
{
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .Build();
    provider = new ServiceCollection().AddVeraApplication(config).BuildServiceProvider();
}
catch (Exception e) when (e is VeraConfigurationException or FileNotFoundException or InvalidDataException)
{
    var message = e switch
    {
        FileNotFoundException => $"appsettings.json was not found in {AppContext.BaseDirectory}",
        InvalidDataException => $"appsettings.json could not be read: {e.InnerException?.Message ?? e.Message}",
        _ => e.Message,
    };
    AnsiConsole.MarkupLine($"[red]Configuration error:[/] {Markup.Escape(message)}");
    return 1;
}

await using var services = provider;
var settings = new AppSettings { LocalModel = services.GetRequiredService<LocalModelInfo>() };

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
            await StartTestScreen.ShowAsync(settings, () => services.GetRequiredService<ChatSession>());
            break;
        case MainMenuOption.Settings:
            SettingsScreen.Show(settings);
            break;
        case MainMenuOption.Exit:
            AnsiConsole.MarkupLine("[grey]Booted off.[/]");
            return 0;
    }
}

internal enum MainMenuOption
{
    StartTesting,
    Settings,
    Exit,
}
