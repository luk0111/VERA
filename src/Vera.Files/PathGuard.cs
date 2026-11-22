namespace Vera.Files;

/// <summary>Turns the text the tester typed into a full file path, or refuses it. Never touches the file's content.</summary>
internal static class PathGuard
{
    private const string NotReadable = "VERA does not read this kind of file.";

    private static readonly string[] DeniedExtensions = [".pfx", ".p12", ".pem", ".key", ".kdbx", ".db"];
    private static readonly string[] DeniedFolders = [".git", ".ssh"];

    /// <summary>The full path of the file <paramref name="path"/> points to.</summary>
    /// <exception cref="FileAccessException"><see cref="FileAccessError.NotAllowed"/> for a refused path or kind of file; <see cref="FileAccessError.NotFound"/> for a missing file or a folder.</exception>
    public static string Resolve(string path)
    {
        // Windows Terminal puts quotes around a path when a file is dragged in.
        var typed = Unquote(path ?? "");
        if (typed.Length == 0)
        {
            throw new FileAccessException(FileAccessError.NotFound, "No file path was given.");
        }

        RejectUnsafeForm(typed);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(typed);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FileAccessException(FileAccessError.NotFound, $"'{typed}' is not a valid file path.");
        }

        // Before the existence check, so a refused file never shows whether it exists.
        RejectDenied(fullPath);

        if (Directory.Exists(fullPath))
        {
            throw new FileAccessException(FileAccessError.NotFound, $"'{fullPath}' is a folder, not a file.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileAccessException(FileAccessError.NotFound, $"File not found: {fullPath}");
        }

        return fullPath;
    }

    private static string Unquote(string path)
    {
        var text = path.Trim();
        if (text.Length >= 2 && text[0] is '"' or '\'' && text[^1] == text[0])
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    // Device paths (\\?\, \\.\), network shares (\\server\share) and alternate data streams (file.txt:secret) are Windows-only forms.
    private static void RejectUnsafeForm(string typed)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (typed.StartsWith(@"\\", StringComparison.Ordinal) || typed.StartsWith("//", StringComparison.Ordinal))
        {
            throw new FileAccessException(FileAccessError.NotAllowed, "VERA does not read device paths or network shares.");
        }

        // A drive letter is the only place a colon may be.
        var colon = typed.IndexOf(':');
        if (colon >= 0 && !(colon == 1 && char.IsAsciiLetter(typed[0]) && typed.IndexOf(':', 2) < 0))
        {
            throw new FileAccessException(FileAccessError.NotAllowed, "VERA does not read alternate data streams or paths with a colon.");
        }
    }

    private static void RejectDenied(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(name);

        var denied =
            name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("id_rsa", StringComparison.OrdinalIgnoreCase)
            || DeniedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => DeniedFolders.Contains(segment, StringComparer.OrdinalIgnoreCase));

        if (denied)
        {
            throw new FileAccessException(FileAccessError.NotAllowed, NotReadable);
        }
    }
}
