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
var models = services.GetRequiredService<IReadOnlyList<ConfiguredModel>>();
var savedProfile = await UserSettingsStore.LoadSelectedProfileAsync(CancellationToken.None);
var settings = new AppSettings
{
    ConfiguredModels = models,
    SelectedModel = models.FirstOrDefault(m => m.ProfileName == savedProfile) ?? models[0],
};
var secrets = services.GetRequiredService<ISecretStore>();
var newChat = services.GetRequiredService<Func<string, ChatSession>>();
var newFind = services.GetRequiredService<Func<string, FindElementService>>();
var runningApps = services.GetRequiredService<IRunningAppSource>();
await SettingsScreen.RefreshApiKeyStateAsync(settings, secrets);

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
            await StartTestScreen.ShowAsync(
                settings, runningApps, () => newChat(settings.SelectedModel!.ProfileName), services.GetRequiredService<ChatAppContextReader>());
            break;
        case MainMenuOption.FindElement:
            if (ProcessPicker.Pick(settings, runningApps, "Find an element in an app") is { } app)
            {
                await FindScreen.ShowAsync(settings, app, newFind(settings.SelectedModel!.ProfileName));
            }

            break;
        case MainMenuOption.Settings:
            await SettingsScreen.ShowAsync(settings, secrets);
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
