using Vera.Files;

namespace Vera.Files.Tests;

public sealed class PathGuardTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "vera-files-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Create(string relativePath, string content = "text")
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static FileAccessError ErrorOf(Action action) =>
        Should.Throw<FileAccessException>(action).Error;

    [Fact]
    public void A_normal_file_returns_its_full_path()
    {
        var path = Create("notes.txt");

        PathGuard.Resolve(path).ShouldBe(path);
    }

    [Fact]
    public void A_relative_path_is_made_full()
    {
        var path = Create("notes.txt");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);

        PathGuard.Resolve(relative).ShouldBe(path);
    }

    [Theory]
    [InlineData("\"{0}\"")]
    [InlineData("  \"{0}\"  ")]
    [InlineData("'{0}'")]
    [InlineData("  {0}  ")]
    public void Spaces_and_quotes_around_the_path_are_removed(string template)
    {
        var path = Create("my notes.txt");

        PathGuard.Resolve(template.Replace("{0}", path, StringComparison.Ordinal)).ShouldBe(path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void An_empty_path_is_not_found(string typed)
    {
        ErrorOf(() => PathGuard.Resolve(typed)).ShouldBe(FileAccessError.NotFound);
    }

    [Fact]
    public void A_missing_file_is_not_found()
    {
        ErrorOf(() => PathGuard.Resolve(Path.Combine(_root, "missing.txt"))).ShouldBe(FileAccessError.NotFound);
    }

    [Fact]
    public void A_folder_is_not_found_as_a_file()
    {
        var exception = Should.Throw<FileAccessException>(() => PathGuard.Resolve(_root));

        exception.Error.ShouldBe(FileAccessError.NotFound);
        exception.Message.ShouldContain("folder");
    }

    [Theory]
    [InlineData(@"\\server\share\notes.txt")]
    [InlineData(@"\\?\C:\notes.txt")]
    [InlineData(@"\\.\PhysicalDrive0")]
    public void Device_and_network_paths_are_not_allowed(string typed)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Device and network paths are Windows forms.");

        ErrorOf(() => PathGuard.Resolve(typed)).ShouldBe(FileAccessError.NotAllowed);
    }

    [Fact]
    public void An_alternate_data_stream_is_not_allowed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Alternate data streams are a Windows form.");
        var path = Create("notes.txt");

        ErrorOf(() => PathGuard.Resolve(path + ":secret")).ShouldBe(FileAccessError.NotAllowed);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".ENV")]
    [InlineData(".env.local")]
    [InlineData("id_rsa")]
    [InlineData("id_rsa.pub")]
    [InlineData("cert.pfx")]
    [InlineData("CERT.PFX")]
    [InlineData("cert.p12")]
    [InlineData("cert.pem")]
    [InlineData("server.key")]
    [InlineData("passwords.kdbx")]
    [InlineData("data.db")]
    [InlineData(@".git\config")]
    [InlineData(@"repo\.git\HEAD")]
    [InlineData(@".ssh\known_hosts")]
    [InlineData(@"home\.SSH\config")]
    public void Secret_files_are_not_allowed(string relativePath)
    {
        var path = Create(relativePath);

        var exception = Should.Throw<FileAccessException>(() => PathGuard.Resolve(path));

        exception.Error.ShouldBe(FileAccessError.NotAllowed);
        exception.Message.ShouldBe("VERA does not read this kind of file.");
    }

    [Fact]
    public void A_secret_file_that_does_not_exist_is_refused_too()
    {
        // The answer must not reveal whether a secret file exists.
        ErrorOf(() => PathGuard.Resolve(Path.Combine(_root, ".env"))).ShouldBe(FileAccessError.NotAllowed);
    }

    [Theory]
    [InlineData("environment.txt")]
    [InlineData("dotenv.md")]
    [InlineData("gitnotes.txt")]
    [InlineData("keys.txt")]
    [InlineData(@"docs\ssh-guide.txt")]
    public void Look_alike_names_are_allowed(string relativePath)
    {
        var path = Create(relativePath);

        PathGuard.Resolve(path).ShouldBe(path);
    }

    [Fact]
    public void A_link_to_a_secret_file_is_not_allowed()
    {
        var secret = Create(@".ssh\config");
        var link = Path.Combine(_root, "notes.txt");
        Link(() => File.CreateSymbolicLink(link, secret));

        ErrorOf(() => PathGuard.Resolve(link)).ShouldBe(FileAccessError.NotAllowed);
    }

    [Fact]
    public void A_linked_folder_that_leads_into_a_secret_one_is_not_allowed()
    {
        Create(@".ssh\config");
        Link(() => Directory.CreateSymbolicLink(Path.Combine(_root, "keys"), Path.Combine(_root, ".ssh")));

        ErrorOf(() => PathGuard.Resolve(Path.Combine(_root, "keys", "config"))).ShouldBe(FileAccessError.NotAllowed);
    }

    [Fact]
    public void A_junction_that_leads_into_a_secret_folder_is_not_allowed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Junctions are a Windows form.");
        Create(@".git\config");
        var junction = Path.Combine(_root, "docs");

        // Unlike a symbolic link, a junction needs no special rights.
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", junction, Path.Combine(_root, ".git")])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        })!)
        {
            mklink.WaitForExit();
            Assert.SkipUnless(mklink.ExitCode == 0, "Could not make a junction here.");
        }

        try
        {
            ErrorOf(() => PathGuard.Resolve(Path.Combine(junction, "config"))).ShouldBe(FileAccessError.NotAllowed);
        }
        finally
        {
            // Removes the junction, not the folder it leads to; a recursive delete of the test folder can't.
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void A_short_name_for_a_secret_folder_is_not_allowed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Short names are a Windows form.");
        Create(@".ssh\config");
        var shortName = Path.Combine(_root, "SSH~1");
        Assert.SkipUnless(Directory.Exists(shortName), "This drive has no short names.");

        ErrorOf(() => PathGuard.Resolve(Path.Combine(shortName, "config"))).ShouldBe(FileAccessError.NotAllowed);
    }

    [Fact]
    public void A_link_to_a_normal_file_is_allowed()
    {
        var notes = Create("notes.txt");
        var link = Path.Combine(_root, "today.txt");
        Link(() => File.CreateSymbolicLink(link, notes));

        PathGuard.Resolve(link).ShouldBe(link);
    }

    // Windows lets only administrators and Developer Mode make symbolic links.
    private static void Link(Action create)
    {
        try
        {
            create();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Could not make a symbolic link here: {e.Message}");
        }
    }
}
