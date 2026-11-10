using System.Text.Json;

namespace Vera.Cli;

/// <summary>
/// Saves the tester's choices between runs, in <c>settings.json</c> under the user's application data folder
/// (<c>%APPDATA%\VERA</c> on Windows). The API key is never here; it lives in the secret store.
/// </summary>
internal static class UserSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VERA", "settings.json");

    private sealed record SavedSettings(string? SelectedProfile);

    /// <summary>The profile name chosen last run, or null when none was saved or the file can't be read.</summary>
    public static async Task<string?> LoadSelectedProfileAsync(CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(FilePath);
            var saved = await JsonSerializer.DeserializeAsync<SavedSettings>(stream, cancellationToken: ct);
            return saved?.SelectedProfile;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing or unreadable file just means the defaults.
            return null;
        }
    }

    public static async Task SaveSelectedProfileAsync(string profileName, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            await using var stream = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(stream, new SavedSettings(profileName), cancellationToken: ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Saving is best effort; the choice still applies for this run.
        }
    }
}
