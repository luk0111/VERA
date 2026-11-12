using Spectre.Console;
using Vera.Application;
using Vera.Core;

namespace Vera.Cli.Screens;

internal static partial class SettingsScreen
{
    private const string GeminiKeyPage = "https://aistudio.google.com/apikey";

    private enum Option
    {
        ChooseModel,
        ConnectApi,
        SaveSecret,
        MaxSteps,
        RiskyActions,
        Screenshots,
        Back,
    }

    public static async Task ShowAsync(AppSettings settings, ISecretStore secrets)
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
                        Option.SaveSecret => "Save a secret for typing (e.g. a password)",
                        Option.MaxSteps => "Max steps per test",
                        Option.RiskyActions => "Ask before risky actions",
                        Option.Screenshots => "Save screenshots",
                        Option.Back => "Back",
                        _ => o.ToString(),
                    }));

            switch (option)
            {
                case Option.ChooseModel:
                    await ChooseModelAsync(settings, secrets);
                    break;
                case Option.ConnectApi:
                    await ConnectApiAsync(settings, secrets);
                    break;
                case Option.SaveSecret:
                    await SaveSecretAsync(settings, secrets);
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

    /// <summary>Looks up whether the selected model's key is in the store. Reads only whether it exists; the key is not kept.</summary>
    public static async Task RefreshApiKeyStateAsync(AppSettings settings, ISecretStore secrets)
    {
        if (settings.SelectedModel is not { NeedsApiKey: true } model)
        {
            settings.IsApiKeyConnected = false;
            return;
        }

        try
        {
            settings.IsApiKeyConnected = !string.IsNullOrWhiteSpace(await secrets.GetAsync(model.ApiKeyName, CancellationToken.None));
        }
#pragma warning disable CA1031 // A store that can't be read just means "not connected"; the chat explains when it is used.
        catch (Exception)
#pragma warning restore CA1031
        {
            settings.IsApiKeyConnected = false;
        }
    }

    private static Table CurrentSettingsTable(AppSettings settings)
    {
        static string YesNo(bool value) => value ? "[green]Yes[/]" : "[grey]No[/]";

        return new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumns("Setting", "Value")
            .AddRow("Model", $"[teal]{Markup.Escape(settings.ModelLabel)}[/]")
            .AddRow("API key", settings.IsLocalModel
                ? "[grey]Not needed (local)[/]"
                : settings.IsApiKeyConnected ? "[green]Connected[/]" : "[yellow]Not connected[/]")
            .AddRow("Max steps per test", settings.MaxStepsPerTest.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AddRow("Ask before risky actions", YesNo(settings.AskBeforeRiskyActions))
            .AddRow("Save screenshots", YesNo(settings.SaveScreenshots));
    }

    private static async Task ChooseModelAsync(AppSettings settings, ISecretStore secrets)
    {
        var choices = settings.ConfiguredModels.Select(m => new ModelChoice(m, null))
            .Concat(AppSettings.UpcomingModels.Select(name => new ModelChoice(null, name)))
            .ToList();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<ModelChoice>()
                .Title("Choose the model:")
                .HighlightStyle(Ui.Highlight)
                .AddChoices(choices)
                .UseConverter(c => c.Model is { } model
                    ? Markup.Escape(AppSettings.Display(model))
                    : $"{Markup.Escape(c.Upcoming!)} [grey](coming later)[/]"));

        if (choice.Model is not { } chosen)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(choice.Upcoming!)} is not available yet.[/] Keeping [teal]{Markup.Escape(settings.ModelLabel)}[/].");
            Ui.Pause();
            return;
        }

        if (chosen == settings.SelectedModel)
        {
            return;
        }

        settings.SelectedModel = chosen;
        settings.ConnectionState = ConnectionState.Unknown;
        settings.ConnectionDetail = "";
        await RefreshApiKeyStateAsync(settings, secrets);
        await UserSettingsStore.SaveSelectedProfileAsync(chosen.ProfileName, CancellationToken.None);
    }

    private static async Task ConnectApiAsync(AppSettings settings, ISecretStore secrets)
    {
        if (settings.SelectedModel is not { NeedsApiKey: true } model)
        {
            AnsiConsole.MarkupLine("The selected model runs locally and needs no API key. Choose a hosted model first.");
            Ui.Pause();
            return;
        }

        if (string.Equals(model.Provider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"Create a free key at [link]{GeminiKeyPage}[/] (Google AI Studio; keep billing off to stay on the free tier).");
        }

        var key = AnsiConsole.Prompt(
            new TextPrompt<string>($"Paste your [teal]{Markup.Escape(model.DisplayName)}[/] API key:")
                .Secret()
                .Validate(k => string.IsNullOrWhiteSpace(k)
                    ? ValidationResult.Error("[red]The key can't be empty.[/]")
                    : ValidationResult.Success()));

        try
        {
            await secrets.SetAsync(model.ApiKeyName, key.Trim(), CancellationToken.None);
        }
        catch (NotSupportedException e)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(e.Message)}[/]");
            Ui.Pause();
            return;
        }

        settings.IsApiKeyConnected = true;
        settings.ConnectionState = ConnectionState.Unknown;
        settings.ConnectionDetail = "";
        AnsiConsole.MarkupLine("[green]API key saved to Windows Credential Manager.[/] [grey]It stays there for the next runs.[/]");
        Ui.Pause();
    }

    // A secret a step types with {{secret:name}}, e.g. a test account's password. It's saved like the API keys.
    private static async Task SaveSecretAsync(AppSettings settings, ISecretStore secrets)
    {
        AnsiConsole.MarkupLine(Markup.Escape("A step types a saved secret with \"{{secret:name}}\", e.g. type \"{{secret:portal-password}}\" into the password field."));
        AnsiConsole.MarkupLine("[grey]VERA fills it in only when typing. The steps and logs show only the placeholder. Type secrets into password fields, so they stay hidden on screen too.[/]");
        AnsiConsole.WriteLine();

        var keyNames = settings.ConfiguredModels
            .Where(m => !string.IsNullOrWhiteSpace(m.ApiKeyName))
            .Select(m => m.ApiKeyName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("Secret name [grey](letters, digits, _ . -)[/]:")
                .Validate(n => n.Trim() switch
                {
                    var trimmed when !SecretName().IsMatch(trimmed) => ValidationResult.Error("[red]Use only letters, digits, _ . and -, e.g. portal-password.[/]"),
                    var trimmed when keyNames.Contains(trimmed) => ValidationResult.Error("[red]That name holds a model's API key. Pick another name.[/]"),
                    _ => ValidationResult.Success(),
                })).Trim();
        var value = AnsiConsole.Prompt(
            new TextPrompt<string>($"Value of [teal]{Markup.Escape(name)}[/]:")
                .Secret()
                .Validate(v => v.Length == 0 ? ValidationResult.Error("[red]The value can't be empty.[/]") : ValidationResult.Success()));

        try
        {
            await secrets.SetAsync(name, value, CancellationToken.None);
        }
        catch (NotSupportedException e)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(e.Message)}[/]");
            Ui.Pause();
            return;
        }

        AnsiConsole.MarkupLine($"[green]Saved {Markup.Escape(name)} to Windows Credential Manager.[/] " +
            Markup.Escape($"Use it in a step as \"{{{{secret:{name}}}}}\"."));
        Ui.Pause();
    }

    // The characters a {{secret:name}} placeholder allows (see SecretPlaceholder in Vera.Core).
    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9_.\-]+$")]
    private static partial System.Text.RegularExpressions.Regex SecretName();

    // Either a configured model or the name of one that comes later.
    private sealed record ModelChoice(ConfiguredModel? Model, string? Upcoming);
}
