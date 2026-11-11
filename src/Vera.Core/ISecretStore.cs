namespace Vera.Core;

/// <summary>Holds the real values behind <c>{{secret:name}}</c> placeholders and API keys, e.g. in Windows Credential Manager.</summary>
public interface ISecretStore
{
    /// <summary>Returns the secret's value, or null when there is no secret with that name.</summary>
    Task<string?> GetAsync(string name, CancellationToken ct);

    /// <summary>Saves the secret, replacing any earlier value.</summary>
    /// <exception cref="NotSupportedException">The store is read-only. The message says how to set the secret instead.</exception>
    Task SetAsync(string name, string value, CancellationToken ct);
}
