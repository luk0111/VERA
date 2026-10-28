using System.Net;
using System.Net.Sockets;

namespace Vera.Llm.Tests;

public sealed class OllamaConnectionCheckerTests
{
    private const string TagsWithQwen8b = """{"models":[{"name":"qwen3-vl:8b","model":"qwen3-vl:8b"}]}""";

    [Fact]
    public async Task Ollama_up_and_model_present_returns_Connected()
    {
        var status = await CheckAsync(Respond(TagsWithQwen8b), "qwen3-vl:8b", TestContext.Current.CancellationToken);

        status.Kind.ShouldBe(ConnectionStatusKind.Connected);
    }

    [Fact]
    public async Task Ollama_up_and_model_absent_returns_ModelMissing_with_pull_hint()
    {
        var status = await CheckAsync(Respond(TagsWithQwen8b), "qwen3-vl:8b-instruct", TestContext.Current.CancellationToken);

        status.Kind.ShouldBe(ConnectionStatusKind.ModelMissing);
        status.Message.ShouldBe("Model 'qwen3-vl:8b-instruct' is not installed. Run: ollama pull qwen3-vl:8b-instruct");
    }

    [Fact]
    public async Task Connection_refused_returns_Unreachable()
    {
        var handler = new FakeHandler((_, _) =>
            throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));

        var status = await CheckAsync(handler, "qwen3-vl:8b", TestContext.Current.CancellationToken);

        status.Kind.ShouldBe(ConnectionStatusKind.Unreachable);
        status.Message.ShouldBe("Ollama is not running. Start it, then try again.");
    }

    [Fact]
    public async Task Http_error_returns_Unreachable()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var status = await CheckAsync(handler, "qwen3-vl:8b", TestContext.Current.CancellationToken);

        status.Kind.ShouldBe(ConnectionStatusKind.Unreachable);
    }

    [Fact]
    public async Task No_answer_within_the_timeout_returns_Unreachable()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var status = await CheckAsync(handler, "qwen3-vl:8b", TestContext.Current.CancellationToken);

        status.Kind.ShouldBe(ConnectionStatusKind.Unreachable);
    }

    [Fact]
    public async Task Caller_cancelling_throws_OperationCanceledException()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handler = new FakeHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Should.ThrowAsync<OperationCanceledException>(() => CheckAsync(handler, "qwen3-vl:8b", cts.Token));
    }

    [Theory]
    [InlineData("qwen3-vl", """{"models":[{"name":"qwen3-vl:latest","model":"qwen3-vl:latest"}]}""")]
    [InlineData("qwen3-vl:latest", """{"models":[{"name":"qwen3-vl","model":"qwen3-vl"}]}""")]
    [InlineData("Qwen3-VL:8B", TagsWithQwen8b)]
    public async Task Bare_name_matches_latest_and_comparison_ignores_case(string model, string tags)
    {
        var status = await CheckAsync(Respond(tags), model, TestContext.Current.CancellationToken);

        status.Kind.ShouldBe(ConnectionStatusKind.Connected);
    }

    [Fact]
    public async Task Requests_the_tags_endpoint()
    {
        Uri? requested = null;
        var handler = new FakeHandler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TagsWithQwen8b) });
        });

        await CheckAsync(handler, "qwen3-vl:8b", TestContext.Current.CancellationToken);

        requested.ShouldBe(new Uri("http://localhost:11434/api/tags"));
    }

    private static async Task<ConnectionStatus> CheckAsync(FakeHandler handler, string model, CancellationToken ct)
    {
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var checker = new OllamaConnectionChecker(http);
        return await checker.CheckAsync(new LlmProfile { Provider = "Ollama", Model = model }, ct);
    }

    private static FakeHandler Respond(string json) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}