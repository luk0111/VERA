using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.Extensions.AI;

using Vera.Llm;

namespace Vera.Application;

/// <summary>One chat with the model: keeps the conversation and streams each reply. Provider-agnostic.</summary>
public sealed class ChatSession
{
    internal const string SystemPrompt = "You are VERA's assistant. Answer briefly and in plain language.";

    private readonly IChatClient _chatClient;
    private readonly LlmProfile _profile;
    private readonly IConnectionChecker _connectionChecker;
    private readonly List<ChatMessage> _history = [new(ChatRole.System, SystemPrompt)];

    public ChatSession(IChatClient chatClient, LlmProfile profile, IConnectionChecker connectionChecker)
    {
        _chatClient = chatClient;
        _profile = profile;
        _connectionChecker = connectionChecker;
    }

    /// <summary>The short provider name shown to the tester, e.g. <c>Local</c>.</summary>
    public string ProviderLabel => _profile.DisplayName;

    public string ModelName => _profile.Model;

    internal IReadOnlyList<ChatMessage> History => _history;

    public Task<ConnectionStatus> CheckConnectionAsync(CancellationToken ct) =>
        _connectionChecker.CheckAsync(_profile, ct);

    /// <summary>
    /// Sends <paramref name="userText"/> with the conversation so far and yields the reply chunk by chunk.
    /// The reply joins the history only if the stream ends normally; on an error or cancellation only the user message stays.
    /// </summary>
    public async IAsyncEnumerable<string> StreamReplyAsync(string userText, [EnumeratorCancellation] CancellationToken ct)
    {
        _history.Add(new ChatMessage(ChatRole.User, userText));

        var reply = new StringBuilder();
        // A snapshot, so the client never sees the history change while it streams.
        await foreach (var update in _chatClient.GetStreamingResponseAsync(_history.ToArray(), cancellationToken: ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            reply.Append(update.Text);
            yield return update.Text;
        }

        _history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()));
    }

    /// <summary>Forgets the conversation; only the system message stays.</summary>
    public void Reset()
    {
        _history.RemoveRange(1, _history.Count - 1);
    }
}