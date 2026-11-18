namespace Vera.Files;

/// <summary>Why a file could not be read.</summary>
public enum FileAccessError
{
    /// <summary>The path or the kind of file is on the deny-list, or the path form is not allowed (device, network share, alternate data stream).</summary>
    NotAllowed,
    NotFound,
    TooLarge,
    Unsupported,
    Binary,
    /// <summary>A PDF without a text layer (scanned pages).</summary>
    NoText,
    Encrypted,
    BadPages,
}

/// <summary>A file was refused or could not be read. The message is plain and safe to show to the tester and the model.</summary>
public sealed class FileAccessException(FileAccessError error, string message) : Exception(message)
{
    public FileAccessError Error { get; } = error;
}
