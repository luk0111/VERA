namespace Vera.Application.Tests;

public sealed class FileCommandTests
{
    private static FileCommand Parse(string message)
    {
        FileCommand.TryParse(message, out var command, out var error).ShouldBeTrue(error);
        return command.ShouldNotBeNull();
    }

    private static string ErrorOf(string message)
    {
        FileCommand.TryParse(message, out var command, out var error).ShouldBeFalse();
        command.ShouldBeNull();
        return error;
    }

    [Theory]
    [InlineData("/file", true)]
    [InlineData("/file x.txt", true)]
    [InlineData("/FILE x.txt", true)]
    [InlineData("/file\tx.txt", true)]
    [InlineData("/files x.txt", false)]
    [InlineData("/filex.txt", false)]
    [InlineData("file x.txt", false)]
    [InlineData("please /file x.txt", false)]
    [InlineData("/clear", false)]
    public void Only_the_file_command_is_recognised(string message, bool expected)
    {
        FileCommand.IsFileCommand(message).ShouldBe(expected);
    }

    [Fact]
    public void A_path_alone_attaches_the_file_without_a_question()
    {
        Parse(@"/file C:\specs\login.txt").ShouldBe(new FileCommand(@"C:\specs\login.txt", null, null));
    }

    [Fact]
    public void The_text_after_the_path_is_the_question()
    {
        Parse(@"/file C:\specs\login.txt what does it say about login?")
            .ShouldBe(new FileCommand(@"C:\specs\login.txt", null, "what does it say about login?"));
    }

    [Fact]
    public void A_quoted_path_may_hold_spaces()
    {
        Parse("/file \"C:\\my specs\\login page.pdf\" write a test for the error message")
            .ShouldBe(new FileCommand(@"C:\my specs\login page.pdf", null, "write a test for the error message"));
    }

    [Fact]
    public void A_path_in_single_quotes_works_too()
    {
        Parse("/file 'C:\\my specs\\notes.txt'").ShouldBe(new FileCommand(@"C:\my specs\notes.txt", null, null));
    }

    [Fact]
    public void A_dragged_in_path_with_extra_spaces_is_read()
    {
        Parse("/file    \"C:\\my specs\\notes.txt\"   ").ShouldBe(new FileCommand(@"C:\my specs\notes.txt", null, null));
    }

    [Theory]
    [InlineData("pages:4", "4")]
    [InlineData("pages:4-6", "4-6")]
    [InlineData("PAGES:2-3", "2-3")]
    public void Pages_right_after_the_path_are_taken(string token, string expected)
    {
        Parse($"/file \"C:\\a b\\spec.pdf\" {token}").ShouldBe(new FileCommand(@"C:\a b\spec.pdf", expected, null));
        Parse($"/file C:\\spec.pdf {token} summarise it").ShouldBe(new FileCommand(@"C:\spec.pdf", expected, "summarise it"));
    }

    [Fact]
    public void Pages_inside_the_question_are_part_of_the_question()
    {
        Parse(@"/file C:\spec.pdf what is on pages:4-6?").ShouldBe(new FileCommand(@"C:\spec.pdf", null, "what is on pages:4-6?"));
    }

    [Fact]
    public void The_pages_text_is_passed_on_unchecked_for_the_reader_to_judge()
    {
        Parse(@"/file C:\spec.pdf pages:abc").Pages.ShouldBe("abc");
    }

    [Fact]
    public void The_question_keeps_its_inner_spacing_and_quotes()
    {
        Parse(@"/file C:\spec.pdf is ""Invalid credentials""  the right text?").Question
            .ShouldBe(@"is ""Invalid credentials""  the right text?");
    }

    [Theory]
    [InlineData("/file")]
    [InlineData("/file   ")]
    [InlineData("/file \"\"")]
    [InlineData("/file \"   \"")]
    public void Without_a_path_the_tester_is_asked_which_file(string message)
    {
        ErrorOf(message).ShouldBe("Say which file to attach.");
    }

    [Fact]
    public void A_missing_closing_quote_is_reported()
    {
        ErrorOf("/file \"C:\\my specs\\notes.txt").ShouldContain("closing quote");
    }

    [Fact]
    public void Pages_without_a_value_is_reported()
    {
        ErrorOf(@"/file C:\spec.pdf pages:").ShouldContain("pages:");
    }

    [Fact]
    public void A_message_that_is_not_the_command_is_refused()
    {
        ErrorOf("hello").ShouldContain("/file");
    }
}
