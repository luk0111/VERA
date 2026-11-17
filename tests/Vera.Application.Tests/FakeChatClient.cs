using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;

namespace Vera.Application.Tests;

/// <summary>Streams the given chunks and records the messages of every call. Can fail after a number of chunks.</summary>
internal sealed class FakeChatClient(params string[] chunks) : IChatClient
{
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    /// <summary>The chunks of the second, third… call; a call past the end of this list streams the constructor's chunks again.</summary>
    public IReadOnlyList<string[]> LaterReplies { get; init; } = [];

    /// <summary>When set, the stream throws this after <see cref="FailAfter"/> chunks.</summary>
    public Exception? Failure { get; init; }

    public int FailAfter { get; init; }

    /// <summary>Runs after each chunk is yielded, e.g. to cancel the caller's token midway.</summary>
    public Action<int>? AfterChunk { get; init; }

    /// <summary>When set, a last update reports this many output tokens, like a provider's usage at the end of a stream.</summary>
    public long? OutputTokens { get; init; }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add(messages.ToList());
        var reply = Calls.Count > 1 && Calls.Count - 2 < LaterReplies.Count ? LaterReplies[Calls.Count - 2] : chunks;
        for (var i = 0; i < reply.Length; i++)
        {
            if (Failure is not null && i == FailAfter)
            {
                throw Failure;
            }

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, reply[i]);
            AfterChunk?.Invoke(i);
        }

        if (OutputTokens is { } tokens)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { OutputTokenCount = tokens })]);
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}