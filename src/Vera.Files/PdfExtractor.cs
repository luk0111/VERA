using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Vera.Files;

internal static partial class PdfExtractor
{
    internal sealed record Result(string Text, int TotalChars, bool Truncated, int PageCount, string PagesRead, bool LastPageCut);

    /// <summary>
    /// Reads the text layer of a PDF, a page at a time and in reading order, until <paramref name="maxChars"/> is reached.
    /// Only whole pages are returned, except that the first page with text is cut when it alone is longer than the limit.
    /// Pages before the first one with text are skipped, so a scanned PDF ends in <see cref="FileAccessError.NoText"/> instead of a list of empty pages.
    /// </summary>
    /// <exception cref="FileAccessException">
    /// <see cref="FileAccessError.BadPages"/>, <see cref="FileAccessError.NoText"/>, <see cref="FileAccessError.Encrypted"/>,
    /// or <see cref="FileAccessError.Unsupported"/> for a damaged file.
    /// </exception>
    public static Result Extract(byte[] bytes, string? pages, int maxChars, CancellationToken ct)
    {
        try
        {
            using var document = PdfDocument.Open(bytes, new ParsingOptions { SkipMissingFonts = true });
            return Read(document, pages, maxChars, ct);
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new FileAccessException(FileAccessError.Encrypted, "This PDF is password-protected, and VERA cannot open it.");
        }
        catch (Exception e) when (e is not (OperationCanceledException or FileAccessException))
        {
            throw new FileAccessException(FileAccessError.Unsupported, "VERA could not read this PDF. It may be damaged.");
        }
    }

    private static Result Read(PdfDocument document, string? pages, int maxChars, CancellationToken ct)
    {
        var pageCount = document.NumberOfPages;
        var (firstAsked, lastAsked) = ParsePages(pages, pageCount);

        var text = new StringBuilder();
        var total = 0;
        var truncated = false;
        var pageCut = false;
        int? firstRead = null;
        var lastRead = 0;

        for (var number = firstAsked; number <= lastAsked; number++)
        {
            ct.ThrowIfCancellationRequested();

            // The extractor joins lines with the system's line ending; the page markers use \n.
            var body = ContentOrderTextExtractor.GetText(document.GetPage(number), addDoubleNewline: false).ReplaceLineEndings("\n").Trim();
            if (text.Length == 0 && !body.Any(char.IsLetterOrDigit))
            {
                continue;
            }

            var block = $"--- Page {number.ToString(CultureInfo.InvariantCulture)} ---" + (body.Length > 0 ? "\n" + body : "");
            var separator = text.Length == 0 ? "" : "\n\n";
            total += separator.Length + block.Length;

            if (text.Length + separator.Length + block.Length > maxChars)
            {
                truncated = true;
                if (text.Length == 0)
                {
                    text.Append(TextCutter.Cut(block, maxChars).Text);
                    firstRead = lastRead = number;
                    pageCut = true;
                }

                break;
            }

            text.Append(separator).Append(block);
            firstRead ??= number;
            lastRead = number;
        }

        if (firstRead is not { } first)
        {
            throw new FileAccessException(FileAccessError.NoText, "This PDF has no text layer (scanned pages are not supported yet).");
        }

        var pagesRead = first == lastRead
            ? first.ToString(CultureInfo.InvariantCulture)
            : $"{first.ToString(CultureInfo.InvariantCulture)}-{lastRead.ToString(CultureInfo.InvariantCulture)}";
        return new Result(text.ToString(), total, truncated, pageCount, pagesRead, pageCut);
    }

    // "3" or "2-4", 1-based; not set means every page.
    private static (int First, int Last) ParsePages(string? pages, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(pages))
        {
            return (1, pageCount);
        }

        var match = PagesPattern().Match(string.Concat(pages.Where(c => !char.IsWhiteSpace(c))));
        if (!match.Success)
        {
            throw new FileAccessException(FileAccessError.BadPages, $"'{pages.Trim()}' is not a page number. Use a page like 3 or a range like 2-4.");
        }

        var first = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var last = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : first;
        if (first < 1 || last < first)
        {
            throw new FileAccessException(FileAccessError.BadPages, $"'{pages.Trim()}' is not a valid page range. Pages start at 1, and a range goes upwards, like 2-4.");
        }

        if (last > pageCount)
        {
            throw new FileAccessException(
                FileAccessError.BadPages,
                $"This PDF has {pageCount.ToString(CultureInfo.InvariantCulture)} page(s), so '{pages.Trim()}' is out of range.");
        }

        return (first, last);
    }

    [GeneratedRegex(@"^(\d{1,9})(?:-(\d{1,9}))?$")]
    private static partial Regex PagesPattern();
}
