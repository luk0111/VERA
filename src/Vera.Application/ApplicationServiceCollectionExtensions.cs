using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

#if WINDOWS
using Vera.Adapters.Desktop;
using Vera.Input;
using Vera.Secrets;
#else
using Vera.Adapters.Mac;
#endif
using Vera.Core;
using Vera.Files;
using Vera.Llm;

namespace Vera.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Reads every model profile from <c>Llm:Profiles</c> in config and registers each through <see cref="LlmServiceCollectionExtensions.AddVeraLlm"/>,
    /// keyed by its name; <c>Llm:DefaultProfile</c> is also registered without a key.
    /// Registers <see cref="ChatSession"/> as transient for the default profile, a <see cref="Func{T, TResult}"/> that creates one for any profile name,
    /// and the list of <see cref="ConfiguredModel"/>s (default first).
    /// Reads the <c>Files</c> section (limits for <c>/file</c>) through <see cref="FilesServiceCollectionExtensions.AddVeraFiles"/>.
    /// Also registers the secret store (Windows Credential Manager on Windows, environment variables elsewhere), the screen and app adapter for this system
    /// (on Windows, the screen reads <c>VeraInput</c> in config) and <see cref="FindElementService"/> (transient for the default profile, plus a
    /// <see cref="Func{T, TResult}"/> that creates one for any profile name), which saves its screenshots under <c>runs/find</c>.
    /// </summary>
    /// <exception cref="VeraConfigurationException">The <c>Llm</c> section or a profile is missing or invalid.</exception>
    public static IServiceCollection AddVeraApplication(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var (profiles, defaultName) = ReadProfiles(config);

#if WINDOWS
        services.AddVeraSecrets();
#else
        services.AddSingleton<ISecretStore, EnvironmentSecretStore>();
#endif

        foreach (var profile in profiles)
        {
            try
            {
                services.AddVeraLlm(profile, asDefault: profile.Name == defaultName);
            }
            catch (Exception e) when (e is NotSupportedException or ArgumentException)
            {
                throw new VeraConfigurationException($"Model profile '{profile.Name}': {e.Message}", e);
            }
        }

        var defaultProfile = profiles[0];
        services.AddSingleton(new LocalModelInfo(defaultProfile.DisplayName, defaultProfile.Model));
        services.AddSingleton<IReadOnlyList<ConfiguredModel>>(profiles
            .Select(p => new ConfiguredModel(p.Name, p.Provider, p.DisplayName, p.Model, !string.IsNullOrWhiteSpace(p.ApiKeyName), p.ApiKeyName))
            .ToList());
        CheckFilesSection(config);
        services.AddVeraFiles(config);
        services.AddTransient<ChatSession>();
        services.AddSingleton<Func<string, ChatSession>>(sp => name => new ChatSession(
            sp.GetRequiredKeyedService<IChatClient>(name),
            sp.GetRequiredKeyedService<LlmProfile>(name),
            sp.GetRequiredKeyedService<IConnectionChecker>(name),
            sp.GetRequiredService<IFileReader>()));
        services.AddLogging();
#if WINDOWS
        services.AddVeraInput(config);
        services.AddVeraDesktopAdapter();
        services.AddSingleton<IRunningAppSource, WindowsRunningApps>();
#else
        services.AddVeraMacAdapter();
#endif
        services.AddTransient(sp => new FindElementService(
            sp.GetRequiredService<Func<IAppAdapter>>(), sp.GetRequiredService<ILlmClient>(), Path.Combine("runs", "find")));
        services.AddSingleton<Func<string, FindElementService>>(sp => name => new FindElementService(
            sp.GetRequiredService<Func<IAppAdapter>>(), sp.GetRequiredKeyedService<ILlmClient>(name), Path.Combine("runs", "find")));
        return services;
    }

    // The console app has no host, so the options are not checked at start. Check the Files section here to report a mistake in it as a configuration error.
    private static void CheckFilesSection(IConfiguration config)
    {
        var options = new FileAccessOptions();
        try
        {
            config.GetSection(FileAccessOptions.SectionName).Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);
        }
        catch (InvalidOperationException e)
        {
            throw new VeraConfigurationException($"'Files' in appsettings.json: {e.Message}", e);
        }

        var problems = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), problems, validateAllProperties: true))
        {
            throw new VeraConfigurationException($"'Files' in appsettings.json: {string.Join(" ", problems.Select(p => p.ErrorMessage))}");
        }
    }

    // Returns every profile, the default one first, then the rest in config order.
    private static (List<LlmProfile> Profiles, string DefaultName) ReadProfiles(IConfiguration config)
    {
        var llm = config.GetSection("Llm");
        if (!llm.Exists())
        {
            throw new VeraConfigurationException("The 'Llm' section is missing from appsettings.json.");
        }

        var defaultName = llm["DefaultProfile"];
        if (string.IsNullOrWhiteSpace(defaultName))
        {
            throw new VeraConfigurationException("'Llm:DefaultProfile' is missing from appsettings.json.");
        }

        if (!llm.GetSection($"Profiles:{defaultName}").Exists())
        {
            throw new VeraConfigurationException($"'Llm:DefaultProfile' is '{defaultName}', but there is no profile 'Llm:Profiles:{defaultName}' in appsettings.json.");
        }

        var profiles = new List<LlmProfile> { ReadProfile(llm.GetSection($"Profiles:{defaultName}"), defaultName) };
        foreach (var section in llm.GetSection("Profiles").GetChildren())
        {
            if (section.Key != defaultName)
            {
                profiles.Add(ReadProfile(section, section.Key));
            }
        }

        return (profiles, defaultName);
    }

    private static LlmProfile ReadProfile(IConfigurationSection section, string name)
    {
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
