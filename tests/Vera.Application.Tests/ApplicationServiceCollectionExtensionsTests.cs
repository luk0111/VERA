using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddVeraApplication(config).BuildServiceProvider();
    }
}