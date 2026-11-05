using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.Extensions.AI;

namespace Vera.Llm;

/// <summary>
/// Thinking models such as qwen3-vl sometimes skip the marker between thinking and answer, usually later in a chat,
/// and Ollama then labels the whole answer as reasoning. When a reply ends with no answer text, this passes the reasoning on as the answer.
/// </summary>
internal sealed class ReasoningFallbackChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sawText = false;
        var reasoning = new StringBuilder();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            sawText |= !string.IsNullOrWhiteSpace(update.Text);
            foreach (var part in update.Contents.OfType<TextReasoningContent>())
            {
                reasoning.Append(part.Text);
            }

            yield return update;
        }

        var fallback = WithoutThinkTags(reasoning.ToString());
        if (!sawText && fallback.Length > 0)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, fallback);
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(response.Text) || response.Messages.Count == 0)
        {
            return response;
        }

        var reasoning = string.Concat(response.Messages.SelectMany(m => m.Contents).OfType<TextReasoningContent>().Select(r => r.Text));
        var fallback = WithoutThinkTags(reasoning);
        if (fallback.Length > 0)
        {
            response.Messages[^1].Contents.Add(new TextContent(fallback));
        }

        return response;
    }

    // The reasoning can still carry the model's own tags, e.g. a leading "<think>".
    private static string WithoutThinkTags(string reasoning) =>
        reasoning.Replace("<think>", "", StringComparison.Ordinal).Replace("</think>", "", StringComparison.Ordinal).Trim();
}
