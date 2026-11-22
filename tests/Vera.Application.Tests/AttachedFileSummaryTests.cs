using Vera.Files;

namespace Vera.Application.Tests;

public sealed class AttachedFileSummaryTests
{
    private static FileText Pdf(string text, string pagesRead, int pageCount, bool truncated, bool lastPageCut = false) =>
        new(@"C:\specs\login.pdf", FileKind.Pdf, text, text.Length, truncated, pageCount, pagesRead, lastPageCut);

    private static FileText Text(string text, bool truncated) =>
        new(@"C:\notes\todo.txt", FileKind.Text, text, text.Length, truncated, PageCount: null, PagesRead: null);

    [Fact]
    public void A_whole_pdf_shows_its_pages_and_characters()
    {
        AttachedFileSummary.Describe(Pdf(new string('x', 1234), "1-5", 5, truncated: false))
            .ShouldBe("Attached login.pdf · pages 1–5 of 5 · 1,234 characters");
    }

    [Fact]
    public void One_page_is_shown_without_a_range()
    {
        AttachedFileSummary.Describe(Pdf("abc", "2", 5, truncated: false))
            .ShouldBe("Attached login.pdf · pages 2 of 5 · 3 characters");
    }

    [Fact]
    public void A_cut_pdf_says_which_pages_to_attach_next()
    {
        AttachedFileSummary.Describe(Pdf(new string('x', 4000), "1-3", 5, truncated: true))
            .ShouldBe("Attached login.pdf · pages 1–3 of 5 · 4,000 characters · cut, attach more with pages:4-5");
    }

    [Fact]
    public void When_one_page_is_left_the_hint_names_just_that_page()
    {
        AttachedFileSummary.Describe(Pdf("abc", "1-4", 5, truncated: true))
            .ShouldEndWith("cut, attach more with pages:5");
    }

    [Fact]
    public void A_single_page_that_was_too_long_says_so_and_names_the_next_pages()
    {
        AttachedFileSummary.Describe(Pdf(new string('x', 4000), "1", 5, truncated: true, lastPageCut: true))
            .ShouldBe("Attached login.pdf · pages 1 of 5 · 4,000 characters · page 1 was too long and was cut, attach the next pages with pages:2-5");
    }

    [Fact]
    public void A_too_long_last_page_has_no_next_pages_to_offer()
    {
        AttachedFileSummary.Describe(Pdf("abc", "3", 3, truncated: true, lastPageCut: true))
            .ShouldEndWith("page 3 was too long and was cut, the rest was not sent");
    }

    [Fact]
    public void A_text_file_shows_only_its_characters()
    {
        AttachedFileSummary.Describe(Text("hello", truncated: false)).ShouldBe("Attached todo.txt · 5 characters");
    }

    [Fact]
    public void A_cut_text_file_says_the_rest_was_not_sent()
    {
        AttachedFileSummary.Describe(Text(new string('x', 4000), truncated: true))
            .ShouldBe("Attached todo.txt · 4,000 characters · cut, the rest was not sent");
    }

    [Fact]
    public void The_character_count_does_not_depend_on_the_current_culture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            AttachedFileSummary.Describe(Text(new string('x', 4000), truncated: false)).ShouldContain("4,000 characters");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}
