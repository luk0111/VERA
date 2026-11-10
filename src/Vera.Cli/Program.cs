using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Vera.Application;
using Vera.Cli;
using Vera.Cli.Screens;
using Vera.Core;

ServiceProvider provider;
try
{
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        // Each tester's own overrides (e.g. another local model). Ignored by git.
        .AddJsonFile("appsettings.Local.json", optional: true)
        .Build();
    provider = new ServiceCollection().AddVeraApplication(config).BuildServiceProvider();
}
catch (Exception e) when (e is VeraConfigurationException or FileNotFoundException or InvalidDataException)
{
    var message = e switch
    {
        FileNotFoundException => $"appsettings.json was not found in {AppContext.BaseDirectory}",
        // The message names the file (appsettings.json or appsettings.Local.json); the inner one says what is wrong.
        InvalidDataException => $"{e.Message} {e.InnerException?.Message}".TrimEnd(),
        _ => e.Message,
    };
    AnsiConsole.MarkupLine($"[red]Configuration error:[/] {Markup.Escape(message)}");
    return 1;
}

await using var services = provider;
var settings = new AppSettings { LocalModel = services.GetRequiredService<LocalModelInfo>() };
var runningApps = services.GetRequiredService<IRunningAppSource>();

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
                MainMenuOption.FindElement => "Find an element in an app",
                MainMenuOption.Settings => "Settings",
                MainMenuOption.Exit => "Exit",
                _ => option.ToString(),
            }));

    switch (choice)
    {
        case MainMenuOption.StartTesting:
            await StartTestScreen.ShowAsync(settings, runningApps, () => services.GetRequiredService<ChatSession>());
            break;
        case MainMenuOption.FindElement:
            if (ProcessPicker.Pick(settings, runningApps, "Find an element in an app") is { } app)
            {
                await FindScreen.ShowAsync(settings, app, services.GetRequiredService<FindElementService>());
            }

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
    FindElement,
    Settings,
    Exit,
}
