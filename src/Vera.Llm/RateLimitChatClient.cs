using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;

namespace Vera.Llm;

/// <summary>
/// Retries a request the provider refused because of a per-minute rate limit (<see cref="LlmRateLimitException"/>),
/// at most <see cref="MaxRetries"/> times. It waits as long as the provider asked (at most <see cref="MaxDelay"/>), or 5, 15 and 30 seconds.
/// A daily quota or a rejected key is never retried. A stream is retried only before its first update, so no text is ever repeated.
/// </summary>
internal sealed class RateLimitChatClient(IChatClient inner, TimeProvider time) : DelegatingChatClient(inner)
{
    internal const int MaxRetries = 3;

    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan[] FallbackDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            }
            catch (LlmRateLimitException e) when (attempt < MaxRetries)
            {
                await Task.Delay(DelayFor(e, attempt), time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var updates = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            bool hasFirst;
            try
            {
                hasFirst = await updates.MoveNextAsync().ConfigureAwait(false);
            }
            catch (LlmRateLimitException e) when (attempt < MaxRetries)
            {
                await updates.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(DelayFor(e, attempt), time, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch
            {
                await updates.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            try
            {
                if (!hasFirst)
                {
                    yield break;
                }

                yield return updates.Current;
                while (await updates.MoveNextAsync().ConfigureAwait(false))
                {
                    yield return updates.Current;
                }
            }
            finally
            {
                await updates.DisposeAsync().ConfigureAwait(false);
            }

            yield break;
        }
    }

    internal static TimeSpan DelayFor(LlmRateLimitException error, int attempt)
    {
        if (error.RetryAfter is { } asked && asked > TimeSpan.Zero)
        {
            return asked < MaxDelay ? asked : MaxDelay;
        }

        return FallbackDelays[Math.Min(attempt, FallbackDelays.Length - 1)];
    }
}
