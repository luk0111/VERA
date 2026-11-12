using System.Net;
using System.Text;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace Vera.Llm.Tests;

/// <summary>Runs the whole Gemini pipeline (OpenAI SDK included) against a fake HTTP handler.</summary>
public sealed class GeminiChatClientFactoryTests
{
    private const string TestKey = "test-key-not-real";

    private const string Completion = """
        {"id":"c1","object":"chat.completion","created":1,"model":"gemini-3.5-flash-lite",
         "choices":[{"index":0,"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LlmProfile Profile(bool think = false) => new()
    {
        Name = "gemini",
        Provider = "Gemini",
        Endpoint = "https://generativelanguage.googleapis.com",
        Model = "gemini-3.5-flash-lite",
        ApiKeyName = "gemini",
        Think = think,
    };

    [Fact]
    public async Task A_request_goes_to_the_OpenAI_compatible_endpoint_with_the_stored_key()
    {
        var recorder = new Recorder(_ => Ok());
        using var client = Create(Profile(), recorder);

        var response = await client.GetResponseAsync("hi", cancellationToken: Ct);

        response.Text.ShouldBe("hello");
        recorder.Requests.Count.ShouldBe(1);
        recorder.Requests[0].Uri.ShouldBe(new Uri("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"));
        recorder.Requests[0].Authorization.ShouldBe($"Bearer {TestKey}");
        recorder.Requests[0].Body.ShouldContain("\"model\":\"gemini-3.5-flash-lite\"");
    }

    [Fact]
    public async Task Think_false_asks_for_low_reasoning_effort()
    {
        var recorder = new Recorder(_ => Ok());
        using var client = Create(Profile(think: false), recorder);

        await client.GetResponseAsync("hi", cancellationToken: Ct);

        recorder.Requests[0].Body.ShouldContain("\"reasoning_effort\":\"low\"");
    }

    [Fact]
    public async Task Think_true_leaves_reasoning_to_the_model()
    {
        var recorder = new Recorder(_ => Ok());
        using var client = Create(Profile(think: true), recorder);

        await client.GetResponseAsync("hi", cancellationToken: Ct);

        recorder.Requests[0].Body.ShouldNotContain("reasoning_effort");
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var recorder = new Recorder(_ => Ok());
        using var client = Create(Profile(), recorder, new FakeSecretStore());

        await Should.ThrowAsync<LlmUnauthorizedException>(() => client.GetResponseAsync("hi", cancellationToken: Ct));

        recorder.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_daily_quota_surfaces_as_its_own_error_after_one_request()
    {
        var recorder = new Recorder(_ => Reply(HttpStatusCode.TooManyRequests, GeminiErrorBodies.PerDay));
        using var client = Create(Profile(), recorder);

        await Should.ThrowAsync<LlmDailyQuotaException>(() => client.GetResponseAsync("hi", cancellationToken: Ct));

        recorder.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_rejected_key_surfaces_as_unauthorized()
    {
        var recorder = new Recorder(_ => Reply(HttpStatusCode.BadRequest, GeminiErrorBodies.InvalidKey));
        using var client = Create(Profile(), recorder);

        await Should.ThrowAsync<LlmUnauthorizedException>(() => client.GetResponseAsync("hi", cancellationToken: Ct));
    }

    [Fact]
    public async Task A_per_minute_limit_is_retried_once_by_VERA_and_never_by_the_SDK()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder(n => n == 1 ? Reply(HttpStatusCode.TooManyRequests, GeminiErrorBodies.PerMinute) : Ok());
        using var client = Create(Profile(), recorder, time: time);

        var call = client.GetResponseAsync("hi", cancellationToken: Ct);
        for (var i = 0; i < 1000 && !call.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, Ct);
        }

        (await call).Text.ShouldBe("hello");
        recorder.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_busy_server_is_retried_by_VERA()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder(n => n == 1 ? Reply(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"status":"UNAVAILABLE"}}""") : Ok());
        using var client = Create(Profile(), recorder, time: time);

        var call = client.GetResponseAsync("hi", cancellationToken: Ct);
        for (var i = 0; i < 1000 && !call.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, Ct);
        }

        (await call).Text.ShouldBe("hello");
        recorder.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_server_that_stays_busy_ends_with_the_busy_message()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder(_ => Reply(HttpStatusCode.ServiceUnavailable, ""));
        using var client = Create(Profile(), recorder, time: time);

        var call = client.GetResponseAsync("hi", cancellationToken: Ct);
        for (var i = 0; i < 1000 && !call.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, Ct);
        }

        (await Should.ThrowAsync<LlmRateLimitException>(() => call)).Message.ShouldBe(GeminiErrorHandler.BusyMessage);
        recorder.Requests.Count.ShouldBe(1 + RateLimitChatClient.MaxRetries);
    }

    [Fact]
    public async Task A_stream_yields_the_text_and_the_usage()
    {
        const string sse = """
            data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gemini-3.5-flash-lite","choices":[{"index":0,"delta":{"role":"assistant","content":"hel"}}]}

            data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gemini-3.5-flash-lite","choices":[{"index":0,"delta":{"content":"lo"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":2,"total_tokens":5}}

            data: [DONE]


            """;
        var recorder = new Recorder(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });
        using var client = Create(Profile(), recorder);

        var text = new StringBuilder();
        long? outputTokens = null;
        await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: Ct))
        {
            text.Append(update.Text);
            outputTokens = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details.OutputTokenCount ?? outputTokens;
        }

        text.ToString().ShouldBe("hello");
        outputTokens.ShouldBe(2);
    }

    private static IChatClient Create(LlmProfile profile, Recorder recorder, FakeSecretStore? secrets = null, TimeProvider? time = null) =>
        GeminiChatClientFactory.Create(profile, secrets ?? new FakeSecretStore(("gemini", TestKey)), recorder.Handler, time ?? TimeProvider.System);

    private static HttpResponseMessage Ok() => Reply(HttpStatusCode.OK, Completion);

    private static HttpResponseMessage Reply(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Records each request (URL, auth header, body) and answers it with the reply for that request number (1-based).</summary>
    private sealed class Recorder
    {
        public Recorder(Func<int, HttpResponseMessage> reply)
        {
            Handler = new FakeHandler(async (request, ct) =>
            {
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), body));
                return reply(Requests.Count);
            });
        }

        public FakeHandler Handler { get; }

        public List<(Uri Uri, string? Authorization, string Body)> Requests { get; } = [];
    }
}
