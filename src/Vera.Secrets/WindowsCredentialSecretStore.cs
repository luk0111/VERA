using Meziantou.Framework.Win32;

using Vera.Core;

namespace Vera.Secrets;

/// <summary>
/// Keeps secrets in Windows Credential Manager as generic credentials. The secret <c>gemini</c> is the credential <c>VERA/gemini</c>.
/// Values never leave this class except as the return value of <see cref="GetAsync"/>.
/// </summary>
public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const string Prefix = "VERA/";

    /// <summary>The credential's target name in Credential Manager, e.g. <c>VERA/gemini</c>.</summary>
    internal static string TargetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Prefix + name.Trim();
    }

    public Task<string?> GetAsync(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var value = CredentialManager.ReadCredential(TargetName(name))?.Password;
        return Task.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }

    public Task SetAsync(string name, string value, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ct.ThrowIfCancellationRequested();
        CredentialManager.WriteCredential(TargetName(name), userName: name.Trim(), secret: value, CredentialPersistence.LocalMachine);
        return Task.CompletedTask;
    }
}
