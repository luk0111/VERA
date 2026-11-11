using System.Net.Http.Headers;

using Vera.Core;

namespace Vera.Llm;

/// <summary>
/// Puts the Gemini API key on every request. The key is read from the <see cref="ISecretStore"/> each time,
/// so a key connected in Settings works at once. Without a key, no request leaves the machine.
/// </summary>
internal sealed class GeminiApiKeyHandler(ISecretStore secrets, string apiKeyName) : DelegatingHandler
{
    internal const string MissingKeyMessage = "There is no Gemini API key yet. Add it in Settings → Connect your API key.";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = await secrets.GetAsync(apiKeyName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new LlmUnauthorizedException(MissingKeyMessage);
        }

        // Replaces the placeholder credential the OpenAI client put there.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
