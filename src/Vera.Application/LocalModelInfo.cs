namespace Vera.Application;

/// <summary>What the UI shows about the configured model, e.g. <c>Local</c> and <c>qwen3-vl:8b</c>.</summary>
public sealed record LocalModelInfo(string DisplayName, string Model);