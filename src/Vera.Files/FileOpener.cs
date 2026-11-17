namespace Vera.Files;

internal static class FileOpener
{
    /// <summary>
    /// Opens a file for reading. Other programs may keep it open (a log being written, a document open in an editor).
    /// </summary>
    /// <exception cref="FileAccessException">Windows or the file system refused the read.</exception>
    public static FileStream OpenRead(string fullPath)
    {
        try
        {
            return new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (UnauthorizedAccessException)
        {
            throw new FileAccessException(FileAccessError.NotAllowed, "The system does not allow VERA to read this file.");
        }
        catch (IOException e)
        {
            throw new FileAccessException(FileAccessError.NotFound, $"VERA could not open this file: {e.Message}");
        }
    }
}
