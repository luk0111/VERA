using Microsoft.Extensions.AI;

using Vera.Llm;

namespace Vera.Application.Tests;

public sealed class ChatSessionTests
{
    private static readonly LlmProfile Profile = new() { Provider = "Ollama", DisplayName = "Local", Model = "qwen3-vl:8b" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task After_a_reply_the_history_is_system_user_assistant()
    {
        var session = NewSession(new FakeChatClient("Hello ", "Luca."));

        await DrainAsync(session.StreamReplyAsync("my name is Luca", Ct));

        session.History.Select(m => (m.Role, m.Text)).ShouldBe(
        [
            (ChatRole.System, ChatSession.SystemPrompt),
            (ChatRole.User, "my name is Luca"),
            (ChatRole.Assistant, "Hello Luca."),
        ]);
    }

    [Fact]
    public async Task The_second_call_sends_the_first_exchange()
    {
        var client = new FakeChatClient("ok");
        var session = NewSession(client);

        await DrainAsync(session.StreamReplyAsync("first", Ct));
        await DrainAsync(session.StreamReplyAsync("second", Ct));

        client.Calls[1].Select(m => (m.Role, m.Text)).ShouldBe(
        [
            (ChatRole.System, ChatSession.SystemPrompt),
            (ChatRole.User, "first"),
            (ChatRole.Assistant, "ok"),
            (ChatRole.User, "second"),
        ]);
    }

    [Fact]
    public async Task Chunks_arrive_in_order()
    {
        var session = NewSession(new FakeChatClient("a", "b", "c"));

        var chunks = await DrainAsync(session.StreamReplyAsync("hi", Ct));

        chunks.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public async Task An_error_midway_keeps_the_user_message_and_adds_no_assistant_message()
    {
        var client = new FakeChatClient("a", "b", "c") { Failure = new HttpRequestException("gone"), FailAfter = 1 };
        var session = NewSession(client);

        await Should.ThrowAsync<HttpRequestException>(() => DrainAsync(session.StreamReplyAsync("hi", Ct)));

        session.History.Select(m => m.Role).ShouldBe([ChatRole.System, ChatRole.User]);
    }

    [Fact]
    public async Task Cancellation_midway_throws_and_adds_no_assistant_message()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var client = new FakeChatClient("a", "b", "c") { AfterChunk = _ => cts.Cancel() };
        var session = NewSession(client);

        await Should.ThrowAsync<OperationCanceledException>(() => DrainAsync(session.StreamReplyAsync("hi", cts.Token)));

        session.History.Select(m => m.Role).ShouldBe([ChatRole.System, ChatRole.User]);
    }

    [Fact]
    public async Task Reset_leaves_only_the_system_message()
    {
        var session = NewSession(new FakeChatClient("ok"));
        await DrainAsync(session.StreamReplyAsync("hi", Ct));

        session.Reset();

        session.History.Select(m => (m.Role, m.Text)).ShouldBe([(ChatRole.System, ChatSession.SystemPrompt)]);
    }

    [Fact]
    public async Task CheckConnectionAsync_asks_the_checker_with_the_profile()
    {
        var checker = Substitute.For<IConnectionChecker>();
        var expected = new ConnectionStatus(ConnectionStatusKind.Connected, "");
        checker.CheckAsync(Profile, Arg.Any<CancellationToken>()).Returns(expected);
        var session = new ChatSession(new FakeChatClient(), Profile, checker);

        var status = await session.CheckConnectionAsync(Ct);

        status.ShouldBe(expected);
    }

    [Fact]
    public void Labels_come_from_the_profile()
    {
        var session = NewSession(new FakeChatClient());

        session.ProviderLabel.ShouldBe("Local");
        session.ModelName.ShouldBe("qwen3-vl:8b");
    }

    private static ChatSession NewSession(IChatClient client) =>
        new(client, Profile, Substitute.For<IConnectionChecker>());

    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> stream)
    {
        var chunks = new List<string>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}