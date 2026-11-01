namespace Vera.Core;

/// <summary>Holds the real values behind <c>{{secret:name}}</c> placeholders, e.g. in Windows Credential Manager.</summary>
public interface ISecretStore
{
    /// <summary>Returns the secret's value, or null when there is no secret with that name.</summary>
    Task<string?> GetAsync(string name, CancellationToken ct);
}