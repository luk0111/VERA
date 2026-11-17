using System.Text;

namespace Vera.Files;

internal static class TextExtractor
{
    private const int ProbeBytes = 8192;

    /// <summary>
    /// Reads a whole text file. UTF-8 is the default; a byte order mark (UTF-8, UTF-16, UTF-32) picks its own encoding.
    /// Other encodings are not guessed, so Windows-1252 text may show replacement characters.
    /// </summary>
    /// <exception cref="FileAccessException"><see cref="FileAccessError.Binary"/> when the start of the file holds a NUL byte and no byte order mark explains it.</exception>
    public static async Task<string> ReadAsync(string fullPath, CancellationToken ct)
    {
        await using var stream = FileOpener.OpenRead(fullPath);

        var probe = new byte[ProbeBytes];
        var probed = await stream.ReadAsync(probe, ct).ConfigureAwait(false);
        if (!HasByteOrderMark(probe.AsSpan(0, probed)) && probe.AsSpan(0, probed).Contains((byte)0))
        {
            throw new FileAccessException(FileAccessError.Binary, "This file looks like a binary file, not text.");
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    private static bool HasByteOrderMark(ReadOnlySpan<byte> start) =>
        start.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])
        || start.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])
        || start.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])
        || start.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]);
}
