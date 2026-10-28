using Microsoft.Extensions.AI;

using OllamaSharp;
using OllamaSharp.Models;

namespace Vera.Llm;

/// <summary>Builds the <see cref="IChatClient"/> for an Ollama profile. The only place that knows OllamaSharp's chat types.</summary>
internal static class OllamaChatClientFactory
{
    // The first call loads the model into VRAM, which can take minutes.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    public static IChatClient Create(LlmProfile profile)
    {
        var http = new HttpClient { BaseAddress = new Uri(profile.Endpoint), Timeout = RequestTimeout };
        var ollama = new OllamaApiClient(http, profile.Model);

        // Apply the profile's options to every request, so callers stay provider-agnostic and pass no Ollama options.
        return new ChatClientBuilder(ollama)
            .ConfigureOptions(options => options
                .AddOllamaOption(OllamaOption.NumCtx, profile.ContextSize)
                .AddOllamaOption(OllamaOption.Think, profile.Think))
            .Build();
    }
}