namespace Vera.Files;

public enum FileKind
{
    Text,
    Pdf,
}

/// <summary>The text read from a file, already cut to the limit.</summary>
/// <param name="FullPath">The full path that was read.</param>
/// <param name="Text">At most <see cref="FileAccessOptions.MaxCharsPerRead"/> characters. A PDF page starts with <c>--- Page N ---</c>.</param>
/// <param name="TotalChars">The characters the file holds. For a PDF, counted only as far as pages were read.</param>
/// <param name="Truncated">True when more text follows what <paramref name="Text"/> holds.</param>
/// <param name="PageCount">PDFs only: the number of pages in the file.</param>
/// <param name="PagesRead">PDFs only: the pages <paramref name="Text"/> covers, like <c>"1-3"</c> or <c>"2"</c>. Whole pages, except that a first page longer than the limit is cut.</param>
/// <param name="LastPageCut">PDFs only: true when the last page in <paramref name="Text"/> is cut, so the end of that page was not read (a single page longer than the limit).</param>
public sealed record FileText(
    string FullPath,
    FileKind Kind,
    string Text,
    int TotalChars,
    bool Truncated,
    int? PageCount,
    string? PagesRead,
    bool LastPageCut = false);
