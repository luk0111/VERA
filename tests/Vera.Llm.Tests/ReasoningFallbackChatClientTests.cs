using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;

namespace Vera.Llm.Tests;

public sealed class ReasoningFallbackChatClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stream_with_only_reasoning_ends_with_the_reasoning_as_text()
    {
        var client = new ReasoningFallbackChatClient(new ScriptedChatClient(Reasoning("<think>\nI can only "), Reasoning("help you.")));

        var text = await StreamedTextAsync(client);

        text.ShouldBe("I can only help you.");
    }

    [Fact]
    public async Task A_stream_with_text_never_shows_the_reasoning()
    {
        var client = new ReasoningFallbackChatClient(new ScriptedChatClient(Reasoning("let me think"), Text("Hello "), Text("there.")));

        var text = await StreamedTextAsync(client);

        text.ShouldBe("Hello there.");
    }

    [Fact]
    public async Task A_stream_with_neither_text_nor_reasoning_adds_nothing()
    {
        var client = new ReasoningFallbackChatClient(new ScriptedChatClient(Reasoning("<think></think>")));

        var updates = await DrainAsync(client.GetStreamingResponseAsync([], cancellationToken: Ct));

        updates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_response_with_only_reasoning_gets_the_reasoning_as_text()
    {
        var client = new ReasoningFallbackChatClient(new ScriptedChatClient(Reasoning("<think>I can only help you.")));

        var response = await client.GetResponseAsync([], cancellationToken: Ct);

        response.Text.ShouldBe("I can only help you.");
    }

    [Fact]
    public async Task A_response_with_text_is_left_as_it_is()
    {
        var client = new ReasoningFallbackChatClient(new ScriptedChatClient(Reasoning("let me think"), Text("Hello.")));

        var response = await client.GetResponseAsync([], cancellationToken: Ct);

        response.Text.ShouldBe("Hello.");
    }

    private static ChatResponseUpdate Reasoning(string text) => new(ChatRole.Assistant, [new TextReasoningContent(text)]);

    private static ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, text);

    private static async Task<string> StreamedTextAsync(ReasoningFallbackChatClient client) =>
        string.Concat((await DrainAsync(client.GetStreamingResponseAsync([], cancellationToken: Ct))).Select(u => u.Text));

    private static async Task<List<ChatResponseUpdate>> DrainAsync(IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in stream)
        {
            updates.Add(update);
        }

        return updates;
    }

    /// <summary>Replays the given updates, streamed one by one or joined into one response.</summary>
    private sealed class ScriptedChatClient(params ChatResponseUpdate[] updates) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in updates)
            {
                await Task.Yield();
                yield return update;
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(updates.ToChatResponse());

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
