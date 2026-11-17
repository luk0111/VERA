namespace Vera.Files;

/// <summary>What to read.</summary>
/// <param name="Path">The path as the tester typed it; quotes and spaces around it are removed.</param>
/// <param name="Pages">PDFs only: <c>"3"</c> for one page or <c>"2-4"</c> for a range (1-based). Not set means from page 1.</param>
public sealed record FileRequest(string Path, string? Pages = null);
