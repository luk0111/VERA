using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace Vera.Llm.Tests;

public sealed class RateLimitChatClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_per_minute_limit_waits_the_asked_delay_then_retries()
    {
        var time = new FakeTimeProvider();
        var inner = new ScriptedChatClient(time, new LlmRateLimitException("busy", TimeSpan.FromSeconds(37)));
        using var client = new RateLimitChatClient(inner, time);

        var response = await RunAsync(client.GetResponseAsync("hi", cancellationToken: Ct), time);

        response.Text.ShouldBe("ok");
        inner.CallTimes.Count.ShouldBe(2);
        (inner.CallTimes[1] - inner.CallTimes[0]).ShouldBeInRange(TimeSpan.FromSeconds(37), TimeSpan.FromSeconds(39));
    }

    [Fact]
    public async Task A_long_delay_is_capped_at_60_seconds()
    {
        var time = new FakeTimeProvider();
        var inner = new ScriptedChatClient(time, new LlmRateLimitException("busy", TimeSpan.FromMinutes(10)));
        using var client = new RateLimitChatClient(inner, time);

        await RunAsync(client.GetResponseAsync("hi", cancellationToken: Ct), time);

        (inner.CallTimes[1] - inner.CallTimes[0]).ShouldBeInRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(62));
    }

    [Fact]
    public void Without_an_asked_delay_it_waits_5_15_30_seconds()
    {
        var error = new LlmRateLimitException("busy", retryAfter: null);

        RateLimitChatClient.DelayFor(error, 0).ShouldBe(TimeSpan.FromSeconds(5));
        RateLimitChatClient.DelayFor(error, 1).ShouldBe(TimeSpan.FromSeconds(15));
        RateLimitChatClient.DelayFor(error, 2).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task It_gives_up_after_three_retries()
    {
        var time = new FakeTimeProvider();
        var inner = new ScriptedChatClient(time, Enumerable.Range(0, 5).Select(_ => (Exception?)new LlmRateLimitException("busy", TimeSpan.FromSeconds(1))).ToArray());
        using var client = new RateLimitChatClient(inner, time);

        await Should.ThrowAsync<LlmRateLimitException>(() => RunAsync(client.GetResponseAsync("hi", cancellationToken: Ct), time));

        inner.CallTimes.Count.ShouldBe(1 + RateLimitChatClient.MaxRetries);
    }

    [Theory]
    [InlineData(typeof(LlmDailyQuotaException))]
    [InlineData(typeof(LlmUnauthorizedException))]
    public async Task A_daily_quota_or_a_rejected_key_is_not_retried(Type errorType)
    {
        var time = new FakeTimeProvider();
        var inner = new ScriptedChatClient(time, (Exception)Activator.CreateInstance(errorType, "stop")!);
        using var client = new RateLimitChatClient(inner, time);

        var error = await Should.ThrowAsync<Exception>(() => client.GetResponseAsync("hi", cancellationToken: Ct));

        error.ShouldBeOfType(errorType);
        inner.CallTimes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_stream_refused_before_its_first_update_is_retried()
    {
        var time = new FakeTimeProvider();
        var inner = new ScriptedChatClient(time, new LlmRateLimitException("busy", TimeSpan.FromSeconds(2)));
        using var client = new RateLimitChatClient(inner, time);

        var text = await RunAsync(CollectAsync(client.GetStreamingResponseAsync("hi", cancellationToken: Ct)), time);

        text.ShouldBe("ok");
        inner.CallTimes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_stream_that_fails_after_its_first_update_is_not_retried()
    {
        var time = new FakeTimeProvider();
        var inner = new ScriptedChatClient(time) { FailAfterFirstUpdate = new LlmRateLimitException("busy", TimeSpan.FromSeconds(2)) };
        using var client = new RateLimitChatClient(inner, time);

        await Should.ThrowAsync<LlmRateLimitException>(() => CollectAsync(client.GetStreamingResponseAsync("hi", cancellationToken: Ct)));

        inner.CallTimes.Count.ShouldBe(1);
    }

    private static async Task<string> CollectAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var text = "";
        await foreach (var update in updates)
        {
            text += update.Text;
        }

        return text;
    }

    // Moves the fake clock forward one second at a time until the call finishes.
    private static async Task<T> RunAsync<T>(Task<T> task, FakeTimeProvider time)
    {
        for (var i = 0; i < 1000 && !task.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, Ct);
        }

        return await task;
    }

    /// <summary>Fails the first calls with the given errors (null = succeed), then answers "ok". Records when each call came.</summary>
    private sealed class ScriptedChatClient(TimeProvider time, params Exception?[] failures) : IChatClient
    {
        public List<DateTimeOffset> CallTimes { get; } = [];

        public Exception? FailAfterFirstUpdate { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            NextFailure() is { } error
                ? Task.FromException<ChatResponse>(error)
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (NextFailure() is { } error)
            {
                throw error;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "o");
            if (FailAfterFirstUpdate is { } late)
            {
                throw late;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "k");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private Exception? NextFailure()
        {
            CallTimes.Add(time.GetUtcNow());
            var index = CallTimes.Count - 1;
            return index < failures.Length ? failures[index] : null;
        }
    }
}
