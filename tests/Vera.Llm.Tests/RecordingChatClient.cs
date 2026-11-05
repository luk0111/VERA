using Microsoft.Extensions.AI;

namespace Vera.Llm.Tests;

/// <summary>Answers each <see cref="GetResponseAsync"/> call with the next scripted reply and records what it was asked.</summary>
internal sealed class RecordingChatClient(params string[] replies) : IChatClient
{
    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((messages.ToList(), options));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, replies[Calls.Count - 1])));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
