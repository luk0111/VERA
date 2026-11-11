namespace Vera.Llm;

/// <summary>The provider refused a request because of a short-term (per-minute) rate limit. <see cref="RateLimitChatClient"/> waits and retries.</summary>
internal sealed class LlmRateLimitException : Exception
{
    public LlmRateLimitException()
    {
    }

    public LlmRateLimitException(string message)
        : base(message)
    {
    }

    public LlmRateLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LlmRateLimitException(string message, TimeSpan? retryAfter)
        : base(message)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>How long the provider asked to wait, when it said so.</summary>
    public TimeSpan? RetryAfter { get; }
}
