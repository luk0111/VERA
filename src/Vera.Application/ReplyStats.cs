namespace Vera.Application;

/// <summary>Numbers about one finished reply, shown under it in the chat.</summary>
/// <param name="Duration">From sending the message to the end of the reply, including any model load and prompt reading.</param>
/// <param name="GenerationTime">From the first streamed update to the end: the time spent writing the reply.</param>
/// <param name="OutputTokens">Tokens the model generated, including hidden reasoning. Null when the provider does not report it.</param>
public sealed record ReplyStats(TimeSpan Duration, TimeSpan GenerationTime, long? OutputTokens)
{
    /// <summary>Output tokens per second of generation, or null without a token count.</summary>
    public double? TokensPerSecond =>
        OutputTokens is { } tokens && GenerationTime > TimeSpan.Zero ? tokens / GenerationTime.TotalSeconds : null;
}