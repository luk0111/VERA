using System.Globalization;

using Vera.Files;

namespace Vera.Application;

/// <summary>The one line the chat shows after a file is attached.</summary>
public static class AttachedFileSummary
{
    /// <summary>
    /// For example <c>Attached login.pdf · pages 1–3 of 5 · 4,000 characters · cut, attach more with pages:4-5</c>.
    /// The characters are those sent to the model, not those in the file.
    /// </summary>
    public static string Describe(FileText file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var parts = new List<string> { $"Attached {Path.GetFileName(file.FullPath)}" };
        if (file.PagesRead is not null && file.PageCount is { } pageCount)
        {
            parts.Add($"pages {file.PagesRead.Replace("-", "–", StringComparison.Ordinal)} of {pageCount.ToString(CultureInfo.InvariantCulture)}");
        }

        parts.Add($"{file.Text.Length.ToString("N0", CultureInfo.InvariantCulture)} characters");
        if (file.Truncated)
        {
            parts.Add(CutNote(file));
        }

        return string.Join(" · ", parts);
    }

    private static string CutNote(FileText file)
    {
        if (file.PagesRead is null || file.PageCount is not { } pageCount)
        {
            return "cut, the rest was not sent";
        }

        var lastRead = int.Parse(file.PagesRead[(file.PagesRead.LastIndexOf('-') + 1)..], CultureInfo.InvariantCulture);
        string? next = null;
        if (lastRead < pageCount)
        {
            next = lastRead + 1 == pageCount
                ? $"pages:{pageCount.ToString(CultureInfo.InvariantCulture)}"
                : $"pages:{(lastRead + 1).ToString(CultureInfo.InvariantCulture)}-{pageCount.ToString(CultureInfo.InvariantCulture)}";
        }

        if (file.LastPageCut)
        {
            var cut = $"page {lastRead.ToString(CultureInfo.InvariantCulture)} was too long and was cut";
            return next is null ? $"{cut}, the rest was not sent" : $"{cut}, attach the next pages with {next}";
        }

        return next is null ? "cut, the rest was not sent" : $"cut, attach more with {next}";
    }
}
