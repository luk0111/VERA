using Vera.Files;

namespace Vera.Files.Tests;

public sealed class PdfFileReaderTests : FileTestBase
{
    // A tiny PDF with one blank page, protected with the user password "secret" (made once with pypdf, RC4-128).
    private const string PasswordProtectedPdf =
        "JVBERi0xLjMKJeLjz9MKMSAwIG9iago8PAovUHJvZHVjZXIgPDJhYzZmYzAyMzk+Cj4+CmVuZG9iagoyIDAgb2JqCjw8Ci9UeXBlIC9QYWdlcwovQ291bnQgMQovS2lkcyBbIDQgMCBSIF0KPj4KZW5kb2JqCjMgMCBvYmoKPDwKL1R5cGUgL0NhdGFsb2cKL1BhZ2VzIDIgMCBSCj4+CmVuZG9iago0IDAgb2JqCjw8Ci9UeXBlIC9QYWdlCi9SZXNvdXJjZXMgPDwKPj4KL01lZGlhQm94IFsgMC4wIDAuMCAyMDAgMjAwIF0KL1BhcmVudCAyIDAgUgo+PgplbmRvYmoKNSAwIG9iago8PAovViAyCi9SIDMKL0xlbmd0aCAxMjgKL1AgNDI5NDk2NzI5MgovRmlsdGVyIC9TdGFuZGFyZAovTyA8MGRiNTg1NWZjNTMyNjU2OWU3NjU5MDZjYWY2NGU0NDI5YTRjMjBkNmU5OTZmZGVmOTYzZTliNTA4MGY5ZTA4Mz4KL1UgPGI1YzFlM2U1NjVmZWUzNmM2YzNmYTNkMjkxNjdmNzVjMjhiZjRlNWU0ZTc1OGE0MTY0MDA0ZTU2ZmZmYTAxMDg+Cj4+CmVuZG9iagp4cmVmCjAgNgowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwMTUgMDAwMDAgbiAKMDAwMDAwMDA1OSAwMDAwMCBuIAowMDAwMDAwMTE4IDAwMDAwIG4gCjAwMDAwMDAxNjcgMDAwMDAgbiAKMDAwMDAwMDI2MSAwMDAwMCBuIAp0cmFpbGVyCjw8Ci9TaXplIDYKL1Jvb3QgMyAwIFIKL0luZm8gMSAwIFIKL0lEIFsgPDM1Mzk2MzMyMzA2MjYyNjE2NTYzMzgzMjY1MzE2MjM1NjMzNjMzNjM2MTYyNjU2NjM1NjE2NjYxNjU2NjMxMzE+IDwzNTM5NjMzMjMwNjI2MjYxNjU2MzM4MzI2NTMxNjIzNTYzMzYzMzYzNjE2MjY1NjYzNTYxNjY2MTY1NjYzMTMxPiBdCi9FbmNyeXB0IDUgMCBSCj4+CnN0YXJ0eHJlZgo0NzYKJSVFT0YK";

    private static readonly string[] PageOne = ["Login page", "Enter your user name and password."];
    private static readonly string[] PageTwo = ["Error messages", "A wrong password shows: Invalid credentials."];
    private static readonly string[] PageThree = ["Logout", "The session ends after 15 minutes."];

    private static Task<FileText> Read(IFileReader reader, string path, string? pages = null) =>
        reader.ReadAsync(new FileRequest(path, pages), CancellationToken.None);

    private static async Task<FileAccessError> ErrorOf(Task<FileText> read) =>
        (await Should.ThrowAsync<FileAccessException>(read)).Error;

    [Fact]
    public async Task A_whole_pdf_is_read_page_by_page_with_page_markers()
    {
        var path = CreatePdf("spec.pdf", PageOne, PageTwo, PageThree);

        var result = await Read(NewReader(), path);

        result.Kind.ShouldBe(FileKind.Pdf);
        result.PageCount.ShouldBe(3);
        result.PagesRead.ShouldBe("1-3");
        result.Truncated.ShouldBeFalse();
        result.LastPageCut.ShouldBeFalse();
        result.Text.ShouldStartWith("--- Page 1 ---");
        result.Text.ShouldContain("--- Page 2 ---");
        result.Text.ShouldContain("--- Page 3 ---");
        result.Text.ShouldContain("Invalid credentials");
        result.Text.IndexOf("Login page", StringComparison.Ordinal).ShouldBeLessThan(result.Text.IndexOf("Logout", StringComparison.Ordinal));
        result.TotalChars.ShouldBe(result.Text.Length);
    }

    [Fact]
    public async Task Lines_of_a_page_stay_on_separate_lines_in_reading_order()
    {
        var path = CreatePdf("lines.pdf", ["First line", "Second line", "Third line"]);

        var result = await Read(NewReader(), path);

        result.Text.ShouldContain("First line\nSecond line\nThird line");
    }

    [Fact]
    public async Task One_page_can_be_chosen()
    {
        var path = CreatePdf("spec.pdf", PageOne, PageTwo, PageThree);

        var result = await Read(NewReader(), path, pages: "2");

        result.PagesRead.ShouldBe("2");
        result.PageCount.ShouldBe(3);
        result.Truncated.ShouldBeFalse();
        result.Text.ShouldContain("--- Page 2 ---");
        result.Text.ShouldContain("Invalid credentials");
        result.Text.ShouldNotContain("Login page");
        result.Text.ShouldNotContain("Logout");
    }

    [Theory]
    [InlineData("2-3")]
    [InlineData(" 2 - 3 ")]
    public async Task A_range_of_pages_can_be_chosen(string pages)
    {
        var path = CreatePdf("spec.pdf", PageOne, PageTwo, PageThree);

        var result = await Read(NewReader(), path, pages);

        result.PagesRead.ShouldBe("2-3");
        result.Text.ShouldNotContain("Login page");
        result.Text.ShouldContain("Invalid credentials");
        result.Text.ShouldContain("The session ends");
    }

    [Theory]
    [InlineData("9")]
    [InlineData("4")]
    [InlineData("2-9")]
    [InlineData("0")]
    [InlineData("3-2")]
    [InlineData("abc")]
    [InlineData("1-")]
    [InlineData("-2")]
    [InlineData("1,2")]
    [InlineData("1-2-3")]
    [InlineData("99999999999")]
    public async Task Pages_that_are_out_of_range_or_malformed_are_refused(string pages)
    {
        var path = CreatePdf("spec.pdf", PageOne, PageTwo, PageThree);

        (await ErrorOf(Read(NewReader(), path, pages))).ShouldBe(FileAccessError.BadPages);
    }

    [Fact]
    public async Task The_out_of_range_message_says_how_many_pages_the_pdf_has()
    {
        var path = CreatePdf("spec.pdf", PageOne, PageTwo, PageThree);

        var exception = await Should.ThrowAsync<FileAccessException>(Read(NewReader(), path, pages: "9"));

        exception.Message.ShouldContain("3 page");
        exception.Message.ShouldContain("'9'");
    }

    [Fact]
    public async Task A_pdf_is_cut_after_the_last_whole_page_that_fits()
    {
        var path = CreatePdf("spec.pdf", PageOne, PageTwo, PageThree);
        var all = await Read(NewReader(), path);
        var firstTwoPages = all.Text[..all.Text.IndexOf("--- Page 3 ---", StringComparison.Ordinal)].TrimEnd();

        var result = await Read(NewReader(maxCharsPerRead: firstTwoPages.Length + 5), path);

        result.Truncated.ShouldBeTrue();
        result.LastPageCut.ShouldBeFalse();
        result.PagesRead.ShouldBe("1-2");
        result.PageCount.ShouldBe(3);
        result.Text.ShouldBe(firstTwoPages);
        result.TotalChars.ShouldBeGreaterThan(result.Text.Length);
    }

    [Fact]
    public async Task The_pages_after_a_cut_can_be_read_next()
    {
        var path = CreatePdf("spec.pdf", PageOne, PageTwo, PageThree);
        var reader = NewReader(maxCharsPerRead: 130);

        // What the tester does: read, see "cut", attach the next pages, until nothing is cut.
        var markers = new List<string>();
        var reads = 0;
        var nextPage = 1;
        FileText read;
        do
        {
            read = await Read(reader, path, pages: nextPage == 1 ? null : $"{nextPage}-3");
            markers.AddRange(System.Text.RegularExpressions.Regex.Matches(read.Text, @"--- Page \d+ ---").Select(m => m.Value));
            nextPage = int.Parse(read.PagesRead!.Split('-')[^1], System.Globalization.CultureInfo.InvariantCulture) + 1;
            reads++;
        }
        while (read.Truncated);

        reads.ShouldBeGreaterThan(1);
        markers.ShouldBe(["--- Page 1 ---", "--- Page 2 ---", "--- Page 3 ---"]);
    }

    [Fact]
    public async Task A_first_page_longer_than_the_limit_is_cut()
    {
        var longLines = Enumerable.Range(1, 30).Select(i => $"Requirement number {i:00} must hold").ToArray();
        var path = CreatePdf("long.pdf", longLines, PageTwo);

        var result = await Read(NewReader(maxCharsPerRead: 200), path);

        result.Truncated.ShouldBeTrue();
        result.LastPageCut.ShouldBeTrue();
        result.PagesRead.ShouldBe("1");
        result.Text.Length.ShouldBeLessThanOrEqualTo(200);
        result.Text.ShouldStartWith("--- Page 1 ---");
        result.Text.ShouldNotContain("--- Page 2 ---");
    }

    [Fact]
    public async Task A_pdf_without_any_text_has_no_text_layer()
    {
        var path = CreatePdf("scan.pdf", [], [], []);

        var exception = await Should.ThrowAsync<FileAccessException>(Read(NewReader(), path));

        exception.Error.ShouldBe(FileAccessError.NoText);
        exception.Message.ShouldContain("no text layer");
    }

    [Fact]
    public async Task A_chosen_page_without_text_has_no_text_layer()
    {
        var path = CreatePdf("mixed.pdf", PageOne, []);

        (await ErrorOf(Read(NewReader(), path, pages: "2"))).ShouldBe(FileAccessError.NoText);
    }

    [Fact]
    public async Task Blank_pages_before_the_first_text_are_skipped()
    {
        var path = CreatePdf("cover.pdf", [], PageTwo);

        var result = await Read(NewReader(), path);

        result.PagesRead.ShouldBe("2");
        result.Text.ShouldStartWith("--- Page 2 ---");
    }

    [Fact]
    public async Task A_blank_page_between_pages_with_text_keeps_its_marker()
    {
        var path = CreatePdf("gap.pdf", PageOne, [], PageThree);

        var result = await Read(NewReader(), path);

        result.PagesRead.ShouldBe("1-3");
        result.Text.ShouldContain("--- Page 2 ---");
    }

    [Fact]
    public async Task A_password_protected_pdf_is_encrypted()
    {
        var path = CreateBytes("locked.pdf", Convert.FromBase64String(PasswordProtectedPdf));

        var exception = await Should.ThrowAsync<FileAccessException>(Read(NewReader(), path));

        exception.Error.ShouldBe(FileAccessError.Encrypted);
        exception.Message.ShouldContain("password");
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_is_unsupported()
    {
        var path = CreateText("fake.pdf", "this is not a pdf");

        (await ErrorOf(Read(NewReader(), path))).ShouldBe(FileAccessError.Unsupported);
    }

    [Fact]
    public async Task An_upper_case_extension_is_read_as_a_pdf()
    {
        var path = CreatePdf("SPEC.PDF", PageOne);

        var result = await Read(NewReader(), path);

        result.Kind.ShouldBe(FileKind.Pdf);
    }

    [Fact]
    public async Task A_pdf_above_the_size_limit_is_refused()
    {
        var path = CreatePdf("spec.pdf", PageOne);
        await using (var stream = new FileStream(path, FileMode.Append))
        {
            await stream.WriteAsync(new byte[(1024 * 1024) + 1], TestContext.Current.CancellationToken);
        }

        (await ErrorOf(Read(NewReader(maxFileSizeMb: 1), path))).ShouldBe(FileAccessError.TooLarge);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_read()
    {
        var path = CreatePdf("spec.pdf", PageOne, PageTwo);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewReader().ReadAsync(new FileRequest(path), cts.Token));
    }
}
