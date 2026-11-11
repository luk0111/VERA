namespace Vera.Core;

/// <summary>
/// Reads secrets from environment variables: the secret <c>gemini</c> is <c>VERA_GEMINI_API_KEY</c>.
/// Used where there is no Windows Credential Manager (macOS) and by integration tests. It is read-only.
/// </summary>
public sealed class EnvironmentSecretStore : ISecretStore
{
    /// <summary>The variable that holds a secret, e.g. <c>gemini</c> → <c>VERA_GEMINI_API_KEY</c>. Dashes become underscores.</summary>
    public static string VariableName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return "VERA_" + name.Trim().ToUpperInvariant().Replace('-', '_') + "_API_KEY";
    }

    public Task<string?> GetAsync(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var value = Environment.GetEnvironmentVariable(VariableName(name));
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public Task SetAsync(string name, string value, CancellationToken ct) =>
        throw new NotSupportedException($"Secrets can't be saved here. Set the environment variable {VariableName(name)} instead, then start VERA again.");
}
