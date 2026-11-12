using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;

namespace Vera.Llm;

/// <summary>
/// Sits in front of a model that doesn't run on this computer, e.g. Gemini, and hides personal data in the app's text before each request (#30).
/// Only the app's data blocks change; images such as screenshots are passed on as they are (#31).
/// </summary>
internal sealed class PersonalDataChatClient(IChatClient inner, IReadOnlyCollection<string> hideWords) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Clean(messages), options, cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(Clean(messages), options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    // Copies, so the caller's history keeps the real text: the local model, or the tester, may still need it.
    private List<ChatMessage> Clean(IEnumerable<ChatMessage> messages) =>
        messages.Select(m => new ChatMessage(m.Role, m.Contents.Select(Clean).ToList()) { AuthorName = m.AuthorName }).ToList();

    private AIContent Clean(AIContent content) =>
        content is TextContent text ? new TextContent(PersonalData.HideInAppData(text.Text, hideWords)) : content;
}
