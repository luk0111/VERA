namespace Vera.Files;

/// <summary>Reads the text of a file from disk. Read-only: nothing is ever written, moved or deleted.</summary>
public interface IFileReader
{
    /// <exception cref="FileAccessException">The file was refused or could not be read; the message says why in plain words.</exception>
    Task<FileText> ReadAsync(FileRequest request, CancellationToken ct);
}
