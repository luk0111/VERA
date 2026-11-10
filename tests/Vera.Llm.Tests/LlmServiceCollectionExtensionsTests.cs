using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using Vera.Core;

namespace Vera.Llm.Tests;

public sealed class LlmServiceCollectionExtensionsTests
{
    private static readonly LlmProfile Local = new()
    {
        Name = "local",
        Provider = "Ollama",
        DisplayName = "Local",
        Endpoint = "http://localhost:11434",
        Model = "qwen3-vl:8b",
        ContextSize = 4096,
    };

    private static readonly LlmProfile Gemini = new()
    {
        Name = "gemini",
        Provider = "Gemini",
        DisplayName = "Gemini",
        Endpoint = "https://generativelanguage.googleapis.com",
        Model = "gemini-3.5-flash-lite",
        ApiKeyName = "gemini",
    };

    [Fact]
    public void Each_profile_is_registered_under_its_name()
    {
        using var provider = Build();

        provider.GetRequiredKeyedService<LlmProfile>("local").ShouldBe(Local);
        provider.GetRequiredKeyedService<LlmProfile>("gemini").ShouldBe(Gemini);
        provider.GetRequiredKeyedService<IChatClient>("gemini").ShouldNotBeSameAs(provider.GetRequiredKeyedService<IChatClient>("local"));
        provider.GetRequiredKeyedService<IConnectionChecker>("local").ShouldBeOfType<OllamaConnectionChecker>();
        provider.GetRequiredKeyedService<IConnectionChecker>("gemini").ShouldBeOfType<GeminiConnectionChecker>();
        provider.GetRequiredKeyedService<ILlmClient>("gemini").ShouldNotBeSameAs(provider.GetRequiredKeyedService<ILlmClient>("local"));
    }

    [Fact]
    public void The_default_profile_is_also_registered_without_a_name()
    {
        using var provider = Build();

        provider.GetRequiredService<LlmProfile>().ShouldBe(Local);
        provider.GetRequiredService<IChatClient>().ShouldBeSameAs(provider.GetRequiredKeyedService<IChatClient>("local"));
        provider.GetRequiredService<IConnectionChecker>().ShouldBeSameAs(provider.GetRequiredKeyedService<IConnectionChecker>("local"));
        provider.GetRequiredService<ILlmClient>().ShouldBeSameAs(provider.GetRequiredKeyedService<ILlmClient>("local"));
    }

    [Fact]
    public void A_Gemini_profile_without_an_ApiKeyName_is_rejected()
    {
        var error = Should.Throw<ArgumentException>(() => new ServiceCollection().AddVeraLlm(Gemini with { ApiKeyName = " " }));

        error.Message.ShouldStartWith("Provider 'Gemini' needs an 'ApiKeyName'");
    }

    [Fact]
    public void An_unknown_provider_is_not_supported()
    {
        var error = Should.Throw<NotSupportedException>(() => new ServiceCollection().AddVeraLlm(Local with { Provider = "Anthropic" }));

        error.Message.ShouldBe("Provider 'Anthropic' is not implemented yet.");
    }

    private static ServiceProvider Build() =>
        new ServiceCollection()
            .AddSingleton<ISecretStore>(new FakeSecretStore())
            .AddVeraLlm(Local, asDefault: true)
            .AddVeraLlm(Gemini, asDefault: false)
            .BuildServiceProvider();
}
