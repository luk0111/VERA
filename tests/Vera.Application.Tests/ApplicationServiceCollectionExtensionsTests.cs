using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Vera.Core;
using Vera.Llm;

namespace Vera.Application.Tests;

public sealed class ApplicationServiceCollectionExtensionsTests
{
    private static readonly Dictionary<string, string?> ValidConfig = new()
    {
        ["Llm:DefaultProfile"] = "local",
        ["Llm:Profiles:local:Provider"] = "Ollama",
        ["Llm:Profiles:local:DisplayName"] = "Local",
        ["Llm:Profiles:local:Endpoint"] = "http://localhost:11434",
        ["Llm:Profiles:local:Model"] = "qwen3-vl:8b",
        ["Llm:Profiles:local:ContextSize"] = "8192",
        ["Llm:Profiles:local:Think"] = "false",
    };

    [Fact]
    public void A_valid_configuration_builds_a_provider_that_creates_chat_sessions()
    {
        using var provider = Build(ValidConfig);

        var session = provider.GetRequiredService<ChatSession>();

        session.ProviderLabel.ShouldBe("Local");
        session.ModelName.ShouldBe("qwen3-vl:8b");
        provider.GetRequiredService<ChatSession>().ShouldNotBeSameAs(session);
        provider.GetRequiredService<LocalModelInfo>().ShouldBe(new LocalModelInfo("Local", "qwen3-vl:8b"));
    }

    [Fact]
    public void A_valid_configuration_also_creates_find_element_services()
    {
        using var provider = Build(ValidConfig);

        provider.GetRequiredService<FindElementService>().ShouldNotBeNull();
    }

    [Fact]
    public void Adapters_are_new_each_time_and_not_held_by_the_container()
    {
        var provider = Build(ValidConfig);
        var newAdapter = provider.GetRequiredService<Func<IAppAdapter>>();

        newAdapter().ShouldNotBeSameAs(newAdapter());

        // The container would throw here if it tracked an adapter, since adapters are only IAsyncDisposable.
        Should.NotThrow(provider.Dispose);
    }

    [Fact]
    public void The_profile_is_bound_from_configuration()
    {
        using var provider = Build(ValidConfig);

        provider.GetRequiredService<LlmProfile>().ShouldBe(new LlmProfile
        {
            Name = "local",
            Provider = "Ollama",
            DisplayName = "Local",
            Endpoint = "http://localhost:11434",
            Model = "qwen3-vl:8b",
            ContextSize = 8192,
            Think = false,
        });
    }

    [Fact]
    public void A_missing_Llm_section_gives_a_clear_error()
    {
        var error = Should.Throw<VeraConfigurationException>(() => Build([]));

        error.Message.ShouldBe("The 'Llm' section is missing from appsettings.json.");
    }

    [Fact]
    public void A_wrong_DefaultProfile_name_gives_a_clear_error()
    {
        var config = new Dictionary<string, string?>(ValidConfig) { ["Llm:DefaultProfile"] = "nope" };

        var error = Should.Throw<VeraConfigurationException>(() => Build(config));

        error.Message.ShouldBe("'Llm:DefaultProfile' is 'nope', but there is no profile 'Llm:Profiles:nope' in appsettings.json.");
    }

    [Fact]
    public void An_unknown_provider_gives_a_clear_error()
    {
        var config = new Dictionary<string, string?>(ValidConfig) { ["Llm:Profiles:local:Provider"] = "Anthropic" };

        var error = Should.Throw<VeraConfigurationException>(() => Build(config));

        error.Message.ShouldBe("Model profile 'local': Provider 'Anthropic' is not implemented yet.");
    }

    private static Dictionary<string, string?> WithGemini(Dictionary<string, string?> values) => new(values)
    {
        ["Llm:Profiles:gemini:Provider"] = "Gemini",
        ["Llm:Profiles:gemini:DisplayName"] = "Gemini",
        ["Llm:Profiles:gemini:Endpoint"] = "https://generativelanguage.googleapis.com",
        ["Llm:Profiles:gemini:Model"] = "gemini-3.5-flash-lite",
        ["Llm:Profiles:gemini:ContextSize"] = "0",
        ["Llm:Profiles:gemini:Think"] = "false",
        ["Llm:Profiles:gemini:ApiKeyName"] = "gemini",
    };

    [Fact]
    public void Chat_sessions_can_be_created_for_each_configured_profile()
    {
        using var provider = Build(WithGemini(ValidConfig));
        var newChat = provider.GetRequiredService<Func<string, ChatSession>>();

        var gemini = newChat("gemini");
        var local = newChat("local");

        gemini.ProviderLabel.ShouldBe("Gemini");
        gemini.ModelName.ShouldBe("gemini-3.5-flash-lite");
        local.ProviderLabel.ShouldBe("Local");
        newChat("gemini").ShouldNotBeSameAs(gemini);
        provider.GetRequiredService<ChatSession>().ProviderLabel.ShouldBe("Local");
    }

    [Fact]
    public void The_configured_models_are_listed_with_the_default_first()
    {
        var config = WithGemini(ValidConfig);
        config["Llm:DefaultProfile"] = "gemini";
        using var provider = Build(config);

        provider.GetRequiredService<IReadOnlyList<ConfiguredModel>>().ShouldBe(
        [
            new ConfiguredModel("gemini", "Gemini", "Gemini", "gemini-3.5-flash-lite", NeedsApiKey: true, ApiKeyName: "gemini"),
            new ConfiguredModel("local", "Ollama", "Local", "qwen3-vl:8b", NeedsApiKey: false, ApiKeyName: ""),
        ]);
    }

    [Fact]
    public void A_secret_store_is_registered()
    {
        using var provider = Build(ValidConfig);

        provider.GetRequiredService<ISecretStore>().ShouldNotBeNull();
    }

    [Fact]
    public void A_Gemini_profile_without_an_ApiKeyName_gives_a_clear_error()
    {
        var config = WithGemini(ValidConfig);
        config.Remove("Llm:Profiles:gemini:ApiKeyName");

        var error = Should.Throw<VeraConfigurationException>(() => Build(config));

        error.Message.ShouldBe("Model profile 'gemini': Provider 'Gemini' needs an 'ApiKeyName' (the name of the secret that holds the key, e.g. gemini).");
    }

    [Fact]
    public void A_non_default_profile_is_validated_too()
    {
        var config = WithGemini(ValidConfig);
        config["Llm:Profiles:gemini:Endpoint"] = "not a url";

        var error = Should.Throw<VeraConfigurationException>(() => Build(config));

        error.Message.ShouldStartWith("Model profile 'gemini' has no valid 'Endpoint'");
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddVeraApplication(config).BuildServiceProvider();
    }
}