using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

using Vera.Files;

namespace Vera.Files.Tests;

/// <summary>A temp folder per test, a reader with small limits, and PDFs built in memory (no binary fixtures in git).</summary>
public abstract class FileTestBase : IDisposable
{
    protected string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "vera-files-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
        GC.SuppressFinalize(this);
    }

    protected string PathOf(string relativePath) => Path.Combine(Root, relativePath);

    protected string CreateText(string relativePath, string content, Encoding? encoding = null)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    protected string CreateBytes(string relativePath, byte[] content)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Each page is its lines of text, top to bottom; an empty array is a page without text.</summary>
    protected string CreatePdf(string relativePath, params string[][] pages) => CreateBytes(relativePath, BuildPdf(pages));

    protected static IFileReader NewReader(int maxFileSizeMb = 20, int maxCharsPerRead = 4000) =>
        new FileReader(
            Options.Create(new FileAccessOptions { MaxFileSizeMb = maxFileSizeMb, MaxCharsPerRead = maxCharsPerRead }),
            NullLogger<FileReader>.Instance);

    protected static byte[] BuildPdf(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 780.0;
            foreach (var line in lines)
            {
                page.AddText(line, 12, new PdfPoint(40, y), font);
                y -= 20;
            }
        }

        return builder.Build();
    }
}
