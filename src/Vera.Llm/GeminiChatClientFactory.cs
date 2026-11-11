using System.ClientModel;
using System.ClientModel.Primitives;

using Microsoft.Extensions.AI;

using OpenAI;

using Vera.Core;

namespace Vera.Llm;

/// <summary>
/// Builds the <see cref="IChatClient"/> for a Gemini profile over Gemini's OpenAI-compatible endpoint (<c>{Endpoint}/v1beta/openai/</c>).
/// The only place that knows the OpenAI SDK. <see cref="LlmProfile.ContextSize"/> is ignored: Gemini's window is fixed by the model.
/// </summary>
internal static class GeminiChatClientFactory
{
    // The OpenAI client needs a credential; GeminiApiKeyHandler replaces it with the real key on every request.
    private const string PlaceholderKey = "vera-key-from-secret-store";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

    public static IChatClient Create(LlmProfile profile, ISecretStore secrets) =>
        Create(profile, secrets, new HttpClientHandler(), TimeProvider.System);

    internal static IChatClient Create(LlmProfile profile, ISecretStore secrets, HttpMessageHandler innerHandler, TimeProvider time)
    {
        var handler = new GeminiApiKeyHandler(secrets, profile.ApiKeyName)
        {
            InnerHandler = new GeminiErrorHandler { InnerHandler = innerHandler },
        };
        var http = new HttpClient(handler) { Timeout = RequestTimeout };

        var options = new OpenAIClientOptions
        {
            Endpoint = OpenAiEndpoint(profile.Endpoint),
            Transport = new HttpClientPipelineTransport(http),
            NetworkTimeout = RequestTimeout,
            // RateLimitChatClient does the retrying; the SDK's own retries would multiply the waits and the quota used.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };
        var chat = new OpenAIClient(new ApiKeyCredential(PlaceholderKey), options).GetChatClient(profile.Model).AsIChatClient();

        // Gemini 3 models can't turn thinking off ("none" is 2.5-only), so Think=false asks for the lowest effort.
        return new ChatClientBuilder(chat)
            .Use(inner => new RateLimitChatClient(inner, time))
            .ConfigureOptions(o =>
            {
                if (!profile.Think)
                {
                    o.Reasoning ??= new ReasoningOptions { Effort = ReasoningEffort.Low };
                }
            })
            .Build();
    }

    internal static Uri OpenAiEndpoint(string endpoint) => new(endpoint.TrimEnd('/') + "/v1beta/openai/");
}
