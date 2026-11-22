using System.Text;

using Vera.Files;

namespace Vera.Files.Tests;

public sealed class TextFileReaderTests : FileTestBase
{
    private const string Unicode = "Login → Überprüfung: fehlgeschlagen ✓ 日本語";

    private static Task<FileText> Read(IFileReader reader, string path, string? pages = null) =>
        reader.ReadAsync(new FileRequest(path, pages), CancellationToken.None);

    private static async Task<FileAccessError> ErrorOf(Task<FileText> read) =>
        (await Should.ThrowAsync<FileAccessException>(read)).Error;

    [Fact]
    public async Task A_short_text_file_is_returned_whole()
    {
        var path = CreateText("notes.txt", "line one\nline two");

        var result = await Read(NewReader(), path);

        result.FullPath.ShouldBe(path);
        result.Kind.ShouldBe(FileKind.Text);
        result.Text.ShouldBe("line one\nline two");
        result.TotalChars.ShouldBe(17);
        result.Truncated.ShouldBeFalse();
        result.PageCount.ShouldBeNull();
        result.PagesRead.ShouldBeNull();
    }

    [Fact]
    public async Task An_empty_file_gives_empty_text()
    {
        var result = await Read(NewReader(), CreateText("empty.txt", ""));

        result.Text.ShouldBeEmpty();
        result.Truncated.ShouldBeFalse();
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("md")]
    [InlineData("log")]
    [InlineData("csv")]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("yaml")]
    [InlineData("yml")]
    [InlineData("ini")]
    [InlineData("TXT")]
    public async Task Every_listed_text_extension_can_be_read(string extension)
    {
        var result = await Read(NewReader(), CreateText($"file.{extension}", "hello"));

        result.Text.ShouldBe("hello");
    }

    [Fact]
    public async Task UTF8_without_a_byte_order_mark_is_read()
    {
        var result = await Read(NewReader(), CreateText("utf8.txt", Unicode));

        result.Text.ShouldBe(Unicode);
    }

    [Fact]
    public async Task UTF8_with_a_byte_order_mark_is_read_without_the_mark()
    {
        var result = await Read(NewReader(), CreateText("bom.txt", Unicode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));

        result.Text.ShouldBe(Unicode);
    }

    [Fact]
    public async Task UTF16_little_endian_is_read_although_it_holds_NUL_bytes()
    {
        var result = await Read(NewReader(), CreateText("utf16.txt", Unicode, Encoding.Unicode));

        result.Text.ShouldBe(Unicode);
    }

    [Fact]
    public async Task UTF16_big_endian_is_read()
    {
        var result = await Read(NewReader(), CreateText("utf16be.txt", Unicode, Encoding.BigEndianUnicode));

        result.Text.ShouldBe(Unicode);
    }

    [Fact]
    public async Task A_file_with_NUL_bytes_and_no_byte_order_mark_is_binary()
    {
        var path = CreateBytes("data.txt", [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x01, 0x02]);

        (await ErrorOf(Read(NewReader(), path))).ShouldBe(FileAccessError.Binary);
    }

    [Fact]
    public async Task An_unsupported_extension_names_what_can_be_read()
    {
        var path = CreateBytes("picture.png", [0x89, 0x50, 0x4E, 0x47]);

        var exception = await Should.ThrowAsync<FileAccessException>(Read(NewReader(), path));

        exception.Error.ShouldBe(FileAccessError.Unsupported);
        exception.Message.ShouldContain(".txt");
        exception.Message.ShouldContain(".pdf");
    }

    [Fact]
    public async Task A_file_without_an_extension_is_unsupported()
    {
        (await ErrorOf(Read(NewReader(), CreateText("README", "hello")))).ShouldBe(FileAccessError.Unsupported);
    }

    [Fact]
    public async Task A_file_above_the_size_limit_is_refused_before_it_is_read()
    {
        var path = CreateBytes("big.txt", new byte[(1024 * 1024) + 1]);

        // The bytes are NUL, so reading it would have failed as binary: TooLarge shows that nothing was read.
        (await ErrorOf(Read(NewReader(maxFileSizeMb: 1), path))).ShouldBe(FileAccessError.TooLarge);
    }

    [Fact]
    public async Task A_file_exactly_at_the_size_limit_is_read()
    {
        var path = CreateText("limit.txt", new string('a', 1024 * 1024));

        var result = await Read(NewReader(maxFileSizeMb: 1, maxCharsPerRead: 100), path);

        result.TotalChars.ShouldBe(1024 * 1024);
    }

    [Fact]
    public async Task Long_text_is_cut_at_a_line_break()
    {
        var lines = Enumerable.Range(1, 40).Select(i => $"line {i:00} of the file").ToArray();
        var path = CreateText("long.log", string.Join("\n", lines));

        var result = await Read(NewReader(maxCharsPerRead: 100), path);

        result.Truncated.ShouldBeTrue();
        result.Text.Length.ShouldBeLessThanOrEqualTo(100);
        result.TotalChars.ShouldBe(string.Join("\n", lines).Length);
        // Whole lines only: every kept line is one of the original lines.
        result.Text.Split('\n').ShouldAllBe(line => lines.Contains(line));
    }

    [Fact]
    public async Task Windows_line_endings_do_not_leave_a_dangling_carriage_return()
    {
        var path = CreateText("crlf.txt", string.Join("\r\n", Enumerable.Range(1, 30).Select(i => $"row {i:00} ........")));

        var result = await Read(NewReader(maxCharsPerRead: 100), path);

        result.Truncated.ShouldBeTrue();
        result.Text.ShouldNotEndWith("\r");
    }

    [Fact]
    public async Task One_very_long_line_is_cut_hard_at_the_limit()
    {
        var path = CreateText("oneline.txt", new string('x', 500));

        var result = await Read(NewReader(maxCharsPerRead: 100), path);

        result.Truncated.ShouldBeTrue();
        result.Text.Length.ShouldBe(100);
        result.TotalChars.ShouldBe(500);
    }

    [Fact]
    public async Task A_cut_never_splits_an_emoji()
    {
        // "😀" is two UTF-16 chars; with a limit of 5 the cut would fall in the middle of the third one.
        var path = CreateText("emoji.txt", "😀😀😀😀");

        var result = await Read(NewReader(maxCharsPerRead: 5), path);

        result.Text.ShouldBe("😀😀");
    }

    [Fact]
    public async Task Pages_on_a_text_file_is_refused()
    {
        var path = CreateText("notes.txt", "hello");

        (await ErrorOf(Read(NewReader(), path, pages: "2"))).ShouldBe(FileAccessError.BadPages);
    }

    [Fact]
    public async Task A_missing_file_is_not_found()
    {
        (await ErrorOf(Read(NewReader(), PathOf("missing.txt")))).ShouldBe(FileAccessError.NotFound);
    }

    [Fact]
    public async Task A_secret_file_is_refused_by_the_path_check_before_the_type_check()
    {
        // ".env" is also an unsupported type; NotAllowed shows the deny-list answered first.
        var secret = CreateText(".env", "KEY=value");

        (await ErrorOf(Read(NewReader(), secret))).ShouldBe(FileAccessError.NotAllowed);
    }

    [Fact]
    public async Task A_file_another_program_has_open_can_be_read()
    {
        var path = CreateText("busy.log", "still being written");
        await using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        var result = await Read(NewReader(), path);

        result.Text.ShouldBe("still being written");
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_read()
    {
        var path = CreateText("notes.txt", "hello");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewReader().ReadAsync(new FileRequest(path), cts.Token));
    }

    [Fact]
    public async Task Quotes_around_the_path_are_accepted_by_the_reader()
    {
        var path = CreateText("my notes.txt", "hello");

        var result = await Read(NewReader(), $"\"{path}\"");

        result.FullPath.ShouldBe(path);
    }
}
