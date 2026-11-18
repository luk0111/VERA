using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vera.Files;

/// <summary>Reads text files and the text layer of PDFs. Logs the path, size and outcome of each read, never the content.</summary>
internal sealed partial class FileReader(IOptions<FileAccessOptions> options, ILogger<FileReader> logger) : IFileReader
{
    private static readonly string[] TextExtensions = [".txt", ".md", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".ini"];

    private const string PdfExtension = ".pdf";

    private readonly FileAccessOptions _options = options.Value;

    public async Task<FileText> ReadAsync(FileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = request.Path;
        try
        {
            var fullPath = PathGuard.Resolve(request.Path);
            path = fullPath;

            var kind = KindOf(fullPath);
            if (kind == FileKind.Text && !string.IsNullOrWhiteSpace(request.Pages))
            {
                throw new FileAccessException(FileAccessError.BadPages, "Pages can only be chosen for PDF files.");
            }

            var size = new FileInfo(fullPath).Length;
            if (size > _options.MaxFileSizeBytes)
            {
                throw new FileAccessException(
                    FileAccessError.TooLarge,
                    $"This file is {(size / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} MB, and VERA reads files up to {_options.MaxFileSizeMb.ToString(CultureInfo.InvariantCulture)} MB.");
            }

            var result = kind == FileKind.Pdf
                ? await ReadPdfAsync(fullPath, request.Pages, ct).ConfigureAwait(false)
                : await ReadTextAsync(fullPath, ct).ConfigureAwait(false);

            LogRead(fullPath, kind, size, result.Text.Length, result.Truncated);
            return result;
        }
        catch (FileAccessException e)
        {
            LogRefused(path, e.Error);
            throw;
        }
    }

    private static FileKind KindOf(string fullPath)
    {
        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(PdfExtension, StringComparison.OrdinalIgnoreCase))
        {
            return FileKind.Pdf;
        }

        if (TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return FileKind.Text;
        }

        throw new FileAccessException(
            FileAccessError.Unsupported,
            $"VERA can read {string.Join(", ", TextExtensions)} and {PdfExtension} files.");
    }

    private async Task<FileText> ReadTextAsync(string fullPath, CancellationToken ct)
    {
        var text = await TextExtractor.ReadAsync(fullPath, ct).ConfigureAwait(false);
        var (kept, cut) = TextCutter.Cut(text, _options.MaxCharsPerRead);
        return new FileText(fullPath, FileKind.Text, kept, text.Length, cut, PageCount: null, PagesRead: null);
    }

    private async Task<FileText> ReadPdfAsync(string fullPath, string? pages, CancellationToken ct)
    {
        byte[] bytes;
        await using (var stream = FileOpener.OpenRead(fullPath))
        {
            bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        }

        var maxChars = _options.MaxCharsPerRead;
        var result = await Task.Run(() => PdfExtractor.Extract(bytes, pages, maxChars, ct), ct).ConfigureAwait(false);
        return new FileText(fullPath, FileKind.Pdf, result.Text, result.TotalChars, result.Truncated, result.PageCount, result.PagesRead, result.LastPageCut);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Read {Path} ({Kind}, {Size} bytes): {Chars} characters, truncated {Truncated}")]
    private partial void LogRead(string path, FileKind kind, long size, int chars, bool truncated);

    [LoggerMessage(Level = LogLevel.Information, Message = "Did not read {Path}: {Error}")]
    private partial void LogRefused(string path, FileAccessError error);
}
