namespace Vera.Core;

/// <summary>A <c>{{secret:name}}</c> placeholder names a secret the store does not have. The message has the name, never a value.</summary>
public sealed class SecretNotFoundException : Exception
{
    public SecretNotFoundException()
    {
    }

    public SecretNotFoundException(string secretName)
        : base($"There is no secret named '{secretName}'. Add it before running the test.")
    {
        SecretName = secretName;
    }

    public SecretNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string SecretName { get; } = "";
}