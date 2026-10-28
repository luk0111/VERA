using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Vera.Llm;

namespace Vera.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Reads the default model profile from <c>Llm</c> in config, registers the model through <see cref="LlmServiceCollectionExtensions.AddVeraLlm"/>,
    /// and registers <see cref="ChatSession"/> as transient, so each chat starts empty.
    /// </summary>
    /// <exception cref="VeraConfigurationException">The <c>Llm</c> section or the profile is missing or invalid.</exception>
    public static IServiceCollection AddVeraApplication(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var profile = ReadDefaultProfile(config);
        try
        {
            services.AddVeraLlm(profile);
        }
        catch (NotSupportedException e)
        {
            throw new VeraConfigurationException($"Model profile '{profile.Name}': {e.Message}", e);
        }

        services.AddSingleton(new LocalModelInfo(profile.DisplayName, profile.Model));
        services.AddTransient<ChatSession>();
        return services;
    }

    private static LlmProfile ReadDefaultProfile(IConfiguration config)
    {
        var llm = config.GetSection("Llm");
        if (!llm.Exists())
        {
            throw new VeraConfigurationException("The 'Llm' section is missing from appsettings.json.");
        }

        var name = llm["DefaultProfile"];
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new VeraConfigurationException("'Llm:DefaultProfile' is missing from appsettings.json.");
        }

        var section = llm.GetSection($"Profiles:{name}");
        if (!section.Exists())
        {
            throw new VeraConfigurationException($"'Llm:DefaultProfile' is '{name}', but there is no profile 'Llm:Profiles:{name}' in appsettings.json.");
        }

        var profile = (section.Get<LlmProfile>() ?? new LlmProfile()) with { Name = name };
        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            throw new VeraConfigurationException($"Model profile '{name}' has no 'Model'.");
        }

        if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out _))
        {
            throw new VeraConfigurationException($"Model profile '{name}' has no valid 'Endpoint' (for example http://localhost:11434).");
        }

        return profile;
    }
}