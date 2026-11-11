namespace Vera.Llm;

/// <summary>The provider's daily request quota for this model is used up. Retrying today will not help.</summary>
public sealed class LlmDailyQuotaException : Exception
{
    public LlmDailyQuotaException()
    {
    }

    public LlmDailyQuotaException(string message)
        : base(message)
    {
    }

    public LlmDailyQuotaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
