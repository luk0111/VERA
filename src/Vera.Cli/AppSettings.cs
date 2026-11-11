using Vera.Application;

namespace Vera.Cli;

/// <summary>Settings chosen in the Settings tab. The chosen model is saved between runs by <see cref="UserSettingsStore"/>, the API key by the secret store; the rest is in memory only.</summary>
internal sealed class AppSettings
{
    /// <summary>Models shown in the list but not available yet.</summary>
    public static IReadOnlyList<string> UpcomingModels { get; } = ["Claude Sonnet", "Claude Opus", "GPT"];

    /// <summary>The model profiles from appsettings.json, the default first. Set in Program.cs.</summary>
    public IReadOnlyList<ConfiguredModel> ConfiguredModels { get; set; } = [];

    public ConfiguredModel? SelectedModel { get; set; }

    /// <summary>True when the selected model runs locally and needs no API key.</summary>
    public bool IsLocalModel => SelectedModel is { NeedsApiKey: false };

    /// <summary>The model as shown to the tester, e.g. "Gemini (gemini-3.5-flash-lite)".</summary>
    public string ModelLabel => SelectedModel is { } model ? Display(model) : "(none)";

    /// <summary>The last known connection state. Only stored here; the chat screen checks it.</summary>
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Unknown;

    public string ConnectionDetail { get; set; } = "";

    /// <summary>Whether the selected model's key is in the secret store. Never holds the key itself.</summary>
    public bool IsApiKeyConnected { get; set; }

    public int MaxStepsPerTest { get; set; } = 30;

    public bool AskBeforeRiskyActions { get; set; } = true;

    public bool SaveScreenshots { get; set; } = true;

    public static string Display(ConfiguredModel model) => $"{model.DisplayName} ({model.Model})";
}

internal enum ConnectionState
{
    Unknown,
    Connected,
    Unreachable,
    ModelMissing,
    Unauthorized,
}
