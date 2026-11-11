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

    /// <summary>The context window in tokens. Ignored for <c>Gemini</c>: there the window is fixed by the model (this is not a limit on output tokens).</summary>
    public int ContextSize { get; init; }

    /// <summary>Whether the model may use its reasoning ("thinking") mode.</summary>
    public bool Think { get; init; }

    /// <summary>
    /// The <em>name</em> of the secret that holds the API key, e.g. <c>gemini</c> (Credential Manager <c>VERA/gemini</c>, env var <c>VERA_GEMINI_API_KEY</c>).
    /// Never the key itself. Empty for providers that need no key.
    /// </summary>
    public string ApiKeyName { get; init; } = "";
}