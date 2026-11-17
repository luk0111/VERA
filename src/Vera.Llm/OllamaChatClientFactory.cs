using Microsoft.Extensions.AI;

using OllamaSharp;
using OllamaSharp.Models;

namespace Vera.Llm;

/// <summary>Builds the <see cref="IChatClient"/> for an Ollama profile. The only place that knows OllamaSharp's chat types.</summary>
internal static class OllamaChatClientFactory
{
    // The first call loads the model into VRAM, which can take minutes.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Only for the small local model (qwen3-vl:8b): at its default temperature it answers the same question differently from one try to the next,
    /// e.g. it sometimes refuses a question about the app (#22). A low temperature makes it keep to the prompt. Other providers keep their own default.
    /// </summary>
    private const float LocalTemperature = 0.2f;

    public static IChatClient Create(LlmProfile profile)
    {
        var http = new HttpClient { BaseAddress = new Uri(profile.Endpoint), Timeout = RequestTimeout };
        var ollama = new OllamaApiClient(http, profile.Model);

        // Apply the profile's options to every request, so callers stay provider-agnostic and pass no Ollama options.
        return new ChatClientBuilder(ollama)
            .Use(inner => new ReasoningFallbackChatClient(inner))
            .ConfigureOptions(options =>
            {
                options.Temperature ??= LocalTemperature;
                options
                    .AddOllamaOption(OllamaOption.NumCtx, profile.ContextSize)
                    .AddOllamaOption(OllamaOption.Think, profile.Think);
            })
            .Build();
    }
}