using Spectre.Console;

namespace Vera.Cli.Screens;

internal static class SettingsScreen
{
    private enum Option
    {
        ChooseModel,
        ConnectApi,
        MaxSteps,
        RiskyActions,
        Screenshots,
        Back,
    }

    public static void Show(AppSettings settings)
    {
        while (true)
        {
            Ui.ShowHeader(settings);
            Ui.Title("Settings");
            AnsiConsole.Write(CurrentSettingsTable(settings));
            AnsiConsole.WriteLine();

            var option = AnsiConsole.Prompt(
                new SelectionPrompt<Option>()
                    .Title("What do you want to change?")
                    .HighlightStyle(Ui.Highlight)
                    .AddChoices(Enum.GetValues<Option>())
                    .UseConverter(o => o switch
                    {
                        Option.ChooseModel => "Choose the model",
                        Option.ConnectApi => "Connect your API key",
                        Option.MaxSteps => "Max steps per test",
                        Option.RiskyActions => "Ask before risky actions",
                        Option.Screenshots => "Save screenshots",
                        Option.Back => "Back",
                        _ => o.ToString(),
                    }));

            switch (option)
            {
                case Option.ChooseModel:
                    ChooseModel(settings);
                    break;
                case Option.ConnectApi:
                    ConnectApi(settings);
                    break;
                case Option.MaxSteps:
                    settings.MaxStepsPerTest = AnsiConsole.Prompt(
                        new TextPrompt<int>("Max steps per test:")
                            .DefaultValue(settings.MaxStepsPerTest)
                            .Validate(n => n is >= 1 and <= 200
                                ? ValidationResult.Success()
                                : ValidationResult.Error("[red]Enter a number between 1 and 200.[/]")));
                    break;
                case Option.RiskyActions:
                    settings.AskBeforeRiskyActions = AnsiConsole.Confirm(
                        "Ask for approval before risky actions (delete, save, submit...)?", settings.AskBeforeRiskyActions);
                    break;
                case Option.Screenshots:
                    settings.SaveScreenshots = AnsiConsole.Confirm(
                        "Save a screenshot for every step?", settings.SaveScreenshots);
                    break;
                case Option.Back:
                    return;
            }
        }
    }

    private static Table CurrentSettingsTable(AppSettings settings)
    {
        static string YesNo(bool value) => value ? "[green]Yes[/]" : "[grey]No[/]";

        return new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumns("Setting", "Value")
            .AddRow("Model", $"[teal]{Markup.Escape(settings.Model)}[/]")
            .AddRow("API key", settings.IsLocalModel
                ? "[grey]Not needed (local)[/]"
                : settings.IsApiKeyConnected ? "[green]Connected[/]" : "[yellow]Not connected[/]")
            .AddRow("Max steps per test", settings.MaxStepsPerTest.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AddRow("Ask before risky actions", YesNo(settings.AskBeforeRiskyActions))
            .AddRow("Save screenshots", YesNo(settings.SaveScreenshots));
    }

    private static void ChooseModel(AppSettings settings)
    {
        var choices = AppSettings.AvailableModels.Concat(AppSettings.UpcomingModels).ToList();

        var model = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Choose the model:")
                .HighlightStyle(Ui.Highlight)
                .AddChoices(choices)
                .UseConverter(m => AppSettings.UpcomingModels.Contains(m)
                    ? $"{Markup.Escape(m)} [grey](coming later)[/]"
                    : Markup.Escape(m)));

        if (AppSettings.UpcomingModels.Contains(model))
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(model)} is not available yet.[/] Keeping [teal]{Markup.Escape(settings.Model)}[/].");
            Ui.Pause();
            return;
        }

        settings.Model = model;
    }

    private static void ConnectApi(AppSettings settings)
    {
        AnsiConsole.MarkupLine("Create a key at [link]https://console.anthropic.com[/] (API keys are separate from a Claude.ai subscription).");

        var key = AnsiConsole.Prompt(
            new TextPrompt<string>("Paste your [teal]Anthropic[/] API key:")
                .Secret()
                .Validate(k => string.IsNullOrWhiteSpace(k)
                    ? ValidationResult.Error("[red]The key can't be empty.[/]")
                    : ValidationResult.Success()));

        // The key itself is not kept: storing it in Windows Credential Manager comes with the backend.
        settings.IsApiKeyConnected = key.Length > 0;
        AnsiConsole.MarkupLine("[green]API key connected for this session.[/] [grey](Saving it comes later.)[/]");
        Ui.Pause();
    }
}
