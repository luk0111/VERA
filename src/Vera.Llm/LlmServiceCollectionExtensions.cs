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
    /// <param name="hideWords">
    /// Words the tester wants hidden from models that don't run on this computer, e.g. their name. Emails, numbers and codes are hidden anyway (see <see cref="PersonalData"/>).
    /// </param>
    public static IServiceCollection AddVeraLlm(this IServiceCollection services, LlmProfile profile, bool asDefault = true, IReadOnlyCollection<string>? hideWords = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        IReadOnlyCollection<string> words = hideWords ?? [];

        var key = profile.Name;
        if (IsProvider(profile, "Ollama"))
        {
            services.AddKeyedSingleton(key, (_, _) => ProtectIfHosted(OllamaChatClientFactory.Create(profile), profile, words));
            services.AddKeyedSingleton<IConnectionChecker>(key, (_, _) =>
                new OllamaConnectionChecker(new HttpClient { BaseAddress = BaseAddress(profile.Endpoint) }));
        }
        else if (IsProvider(profile, "Gemini"))
        {
            if (string.IsNullOrWhiteSpace(profile.ApiKeyName))
            {
                throw new ArgumentException("Provider 'Gemini' needs an 'ApiKeyName' (the name of the secret that holds the key, e.g. gemini).");
            }

            services.AddKeyedSingleton(key, (sp, _) => ProtectIfHosted(GeminiChatClientFactory.Create(profile, sp.GetRequiredService<ISecretStore>()), profile, words));
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

    // A model on another computer (Gemini, or Ollama on a server) gets the app's text only after personal data is hidden in it.
    private static IChatClient ProtectIfHosted(IChatClient client, LlmProfile profile, IReadOnlyCollection<string> hideWords) =>
        profile.RunsOnThisComputer ? client : new PersonalDataChatClient(client, hideWords);

    private static bool IsProvider(LlmProfile profile, string provider) =>
        string.Equals(profile.Provider, provider, StringComparison.OrdinalIgnoreCase);

    // A trailing slash keeps any path in the endpoint when relative URIs like "api/tags" are resolved.
    private static Uri BaseAddress(string endpoint) =>
        new(endpoint.EndsWith('/') ? endpoint : endpoint + "/");
}
