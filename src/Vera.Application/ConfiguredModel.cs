namespace Vera.Application;

/// <summary>
/// One model profile from <c>Llm:Profiles</c>, as the UI lists it. <see cref="ApiKeyName"/> is the secret's name, never the key.
/// </summary>
public sealed record ConfiguredModel(
    string ProfileName,
    string Provider,
    string DisplayName,
    string Model,
    bool NeedsApiKey,
    string ApiKeyName);
