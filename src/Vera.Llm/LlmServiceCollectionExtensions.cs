using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using Vera.Core;

namespace Vera.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers the profile, its <see cref="IChatClient"/>, the <see cref="ILlmClient"/> on top of it and its <see cref="IConnectionChecker"/>, keyed by <see cref="LlmProfile.Name"/>.
    /// With <paramref name="asDefault"/>, also registers them without a key.
    /// Supported providers: <c>Ollama</c> and <c>Gemini</c>. <c>Gemini</c> needs an <see cref="ISecretStore"/> in the container.
    /// </summary>
    /// <exception cref="NotSupportedException">The provider is not implemented.</exception>
    /// <exception cref="ArgumentException">The profile is missing a setting its provider needs.</exception>
    public static IServiceCollection AddVeraLlm(this IServiceCollection services, LlmProfile profile, bool asDefault = true)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var key = profile.Name;
        if (IsProvider(profile, "Ollama"))
        {
            services.AddKeyedSingleton(key, (_, _) => OllamaChatClientFactory.Create(profile));
            services.AddKeyedSingleton<IConnectionChecker>(key, (_, _) =>
                new OllamaConnectionChecker(new HttpClient { BaseAddress = BaseAddress(profile.Endpoint) }));
        }
        else if (IsProvider(profile, "Gemini"))
        {
            if (string.IsNullOrWhiteSpace(profile.ApiKeyName))
            {
                throw new ArgumentException("Provider 'Gemini' needs an 'ApiKeyName' (the name of the secret that holds the key, e.g. gemini).");
            }

            services.AddKeyedSingleton(key, (sp, _) => GeminiChatClientFactory.Create(profile, sp.GetRequiredService<ISecretStore>()));
            services.AddKeyedSingleton<IConnectionChecker>(key, (sp, _) =>
                new GeminiConnectionChecker(new HttpClient { BaseAddress = BaseAddress(profile.Endpoint) }, sp.GetRequiredService<ISecretStore>()));
        }
        else
        {
            throw new NotSupportedException($"Provider '{profile.Provider}' is not implemented yet.");
        }

        services.AddKeyedSingleton(key, profile);
        services.AddKeyedSingleton<ILlmClient>(key, (sp, _) => new ChatLlmClient(sp.GetRequiredKeyedService<IChatClient>(key)));

        if (asDefault)
        {
            services.AddSingleton(profile);
            services.AddSingleton(sp => sp.GetRequiredKeyedService<IChatClient>(key));
            services.AddSingleton(sp => sp.GetRequiredKeyedService<IConnectionChecker>(key));
            services.AddSingleton(sp => sp.GetRequiredKeyedService<ILlmClient>(key));
        }

        return services;
    }

    private static bool IsProvider(LlmProfile profile, string provider) =>
        string.Equals(profile.Provider, provider, StringComparison.OrdinalIgnoreCase);

    // A trailing slash keeps any path in the endpoint when relative URIs like "api/tags" are resolved.
    private static Uri BaseAddress(string endpoint) =>
        new(endpoint.EndsWith('/') ? endpoint : endpoint + "/");
}
