using System.ComponentModel.DataAnnotations;

namespace Vera.Files;

/// <summary>The <c>Files</c> section of appsettings.json. Both limits must be above 0.</summary>
public sealed record FileAccessOptions
{
    public const string SectionName = "Files";

    /// <summary>Files larger than this many megabytes are refused before anything is read.</summary>
    [Range(1, 1024)]
    public int MaxFileSizeMb { get; init; } = 20;

    /// <summary>The most characters one read returns. Keep it small enough to fit next to the system prompt in the model's context.</summary>
    [Range(1, int.MaxValue)]
    public int MaxCharsPerRead { get; init; } = 4000;

    internal long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}
