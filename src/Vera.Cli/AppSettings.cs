using Vera.Application;

namespace Vera.Cli;

/// <summary>Settings chosen in the Settings tab. Kept in memory for this session only (not saved yet).</summary>
internal sealed class AppSettings
{
    public const string LocalModelEntry = "Local (Ollama)";

    public static IReadOnlyList<string> AvailableModels { get; } = [LocalModelEntry, "Claude Sonnet", "Claude Opus"];

    public static IReadOnlyList<string> UpcomingModels { get; } = ["GPT", "Gemini"];

    public string Model { get; set; } = LocalModelEntry;

    public bool IsLocalModel => Model == LocalModelEntry;

    /// <summary>The configured local model (label and tag). Set in Program.cs from configuration.</summary>
    public LocalModelInfo? LocalModel { get; set; }

    /// <summary>The last known connection state. Only stored here; the chat screen checks it.</summary>
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Unknown;

    public string ConnectionDetail { get; set; } = "";

    public bool IsApiKeyConnected { get; set; }

    public int MaxStepsPerTest { get; set; } = 30;

    public bool AskBeforeRiskyActions { get; set; } = true;

    public bool SaveScreenshots { get; set; } = true;
}

internal enum ConnectionState
{
    Unknown,
    Connected,
    Unreachable,
    ModelMissing,
}
