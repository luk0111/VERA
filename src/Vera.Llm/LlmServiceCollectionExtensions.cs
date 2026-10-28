using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Vera.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>Registers the profile, the <see cref="IChatClient"/> and the <see cref="IConnectionChecker"/> for its provider. Only <c>Ollama</c> is supported for now.</summary>
    public static IServiceCollection AddVeraLlm(this IServiceCollection services, LlmProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!string.Equals(profile.Provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Provider '{profile.Provider}' is not implemented yet.");
        }

        services.AddSingleton(profile);
        services.AddSingleton(_ => OllamaChatClientFactory.Create(profile));
        services.AddSingleton<IConnectionChecker>(_ => new OllamaConnectionChecker(new HttpClient { BaseAddress = BaseAddress(profile.Endpoint) }));
        return services;
    }

    // A trailing slash keeps any path in the endpoint when relative URIs like "api/tags" are resolved.
    private static Uri BaseAddress(string endpoint) =>
        new(endpoint.EndsWith('/') ? endpoint : endpoint + "/");
}