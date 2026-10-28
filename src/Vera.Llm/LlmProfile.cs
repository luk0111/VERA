namespace Vera.Llm;

/// <summary>One model profile from <c>Llm:Profiles</c> in config: which provider and model the chat talks to.</summary>
public sealed record LlmProfile
{
    /// <summary>The profile key in config, e.g. <c>local</c>.</summary>
    public string Name { get; init; } = "";

    /// <summary>The provider, e.g. <c>Ollama</c>. Picks the implementation in <see cref="LlmServiceCollectionExtensions.AddVeraLlm"/>.</summary>
    public string Provider { get; init; } = "";

    /// <summary>The short name shown to the tester, e.g. <c>Local</c>.</summary>
    public string DisplayName { get; init; } = "";

    public string Endpoint { get; init; } = "";

    /// <summary>The model tag, e.g. <c>qwen3-vl:8b</c>.</summary>
    public string Model { get; init; } = "";

    /// <summary>The context window in tokens.</summary>
    public int ContextSize { get; init; }

    /// <summary>Whether the model may use its reasoning ("thinking") mode.</summary>
    public bool Think { get; init; }
}