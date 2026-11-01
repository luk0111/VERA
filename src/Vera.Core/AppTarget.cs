namespace Vera.Core;

public enum AppPlatform
{
    Desktop,
    Web,
}

/// <summary>The app under test: started from <see cref="Location"/>, or attached to when <see cref="ProcessId"/> is set.</summary>
/// <param name="Name">The name shown to the tester, e.g. <c>Demo app</c>.</param>
/// <param name="Location">The exe path (desktop) or the URL (web).</param>
/// <param name="Arguments">Command-line arguments for a desktop app, e.g. <c>--bug wrong-password</c>.</param>
/// <param name="ProcessId">Attach to this running process instead of starting a new one.</param>
public sealed record AppTarget(
    string Name,
    AppPlatform Platform,
    string Location,
    string? Arguments = null,
    int? ProcessId = null);