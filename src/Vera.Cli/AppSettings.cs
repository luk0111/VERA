namespace Vera.Cli;

/// <summary>Settings chosen in the Settings tab. Kept in memory for this session only (not saved yet).</summary>
internal sealed class AppSettings
{
    public static IReadOnlyList<string> AvailableModels { get; } = ["Claude Sonnet", "Claude Opus"];

    public static IReadOnlyList<string> UpcomingModels { get; } = ["GPT", "Gemini"];

    public string Model { get; set; } = "Claude Sonnet";

    public bool IsApiKeyConnected { get; set; }

    public int MaxStepsPerTest { get; set; } = 30;

    public bool AskBeforeRiskyActions { get; set; } = true;

    public bool SaveScreenshots { get; set; } = true;
}
