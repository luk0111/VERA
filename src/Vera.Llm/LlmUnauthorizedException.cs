namespace Vera.Llm;

/// <summary>The model provider has no valid API key. The message says what to do; it never contains the key.</summary>
public sealed class LlmUnauthorizedException : Exception
{
    public LlmUnauthorizedException()
    {
    }

    public LlmUnauthorizedException(string message)
        : base(message)
    {
    }

    public LlmUnauthorizedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
