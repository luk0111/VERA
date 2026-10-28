namespace Vera.Application;

/// <summary>The configuration is missing or invalid. The message is written for the tester and is safe to print as is.</summary>
public sealed class VeraConfigurationException : Exception
{
    public VeraConfigurationException()
    {
    }

    public VeraConfigurationException(string message)
        : base(message)
    {
    }

    public VeraConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}