using System.Net;
using System.Net.Sockets;

namespace Vera.Llm.Tests;

public sealed class GeminiConnectionCheckerTests
{
    private const string TestKey = "test-key-not-real";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LlmProfile Profile = new()
    {
        Name = "gemini",
        Provider = "Gemini",
        Endpoint = "https://generativelanguage.googleapis.com",
        Model = "gemini-3.5-flash-lite",
        ApiKeyName = "gemini",
    };

    [Fact]
    public async Task A_known_key_and_model_returns_Connected_and_sends_the_key_as_a_header()
    {
        HttpRequestMessage? sent = null;
        var handler = new FakeHandler((request, _) =>
        {
            sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var status = await CheckAsync(handler, new FakeSecretStore(("gemini", TestKey)));

        status.Kind.ShouldBe(ConnectionStatusKind.Connected);
        sent!.RequestUri.ShouldBe(new Uri("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite"));
        sent.Headers.GetValues("x-goog-api-key").ShouldBe([TestKey]);
    }

    [Fact]
    public async Task No_key_returns_Unauthorized_without_calling_Google()
    {
        var calls = 0;
        var handler = new FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var status = await CheckAsync(handler, new FakeSecretStore());

        status.Kind.ShouldBe(ConnectionStatusKind.Unauthorized);
        status.Message.ShouldContain("Connect your API key");
        calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ConnectionStatusKind.Unauthorized)]
    [InlineData(HttpStatusCode.Unauthorized, ConnectionStatusKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ConnectionStatusKind.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, ConnectionStatusKind.ModelMissing)]
    [InlineData(HttpStatusCode.InternalServerError, ConnectionStatusKind.Unreachable)]
    [InlineData(HttpStatusCode.TooManyRequests, ConnectionStatusKind.Unreachable)]
    public async Task Each_status_code_maps_to_a_connection_state(HttpStatusCode code, ConnectionStatusKind expected)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(code)));

        var status = await CheckAsync(handler, new FakeSecretStore(("gemini", TestKey)));

        status.Kind.ShouldBe(expected);
        status.Message.ShouldNotContain(TestKey);
    }

    [Fact]
    public async Task No_network_returns_Unreachable()
    {
        var handler = new FakeHandler((_, _) =>
            throw new HttpRequestException("no route", new SocketException((int)SocketError.HostUnreachable)));

        var status = await CheckAsync(handler, new FakeSecretStore(("gemini", TestKey)));

        status.Kind.ShouldBe(ConnectionStatusKind.Unreachable);
    }

    private static async Task<ConnectionStatus> CheckAsync(FakeHandler handler, FakeSecretStore secrets)
    {
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
        return await new GeminiConnectionChecker(http, secrets).CheckAsync(Profile, Ct);
    }
}
