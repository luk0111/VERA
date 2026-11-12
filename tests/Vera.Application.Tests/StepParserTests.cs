namespace Vera.Application.Tests;

public sealed class StepParserTests
{
    [Fact]
    public void Click_takes_the_rest_of_the_line_as_the_description() =>
        StepParser.Parse("click the search button").ShouldBe(new ClickStep("the search button"));

    [Fact]
    public void A_quoted_description_loses_its_quotes() =>
        StepParser.Parse("click \"Search\"").ShouldBe(new ClickStep("Search"));

    [Fact]
    public void Type_reads_the_quoted_text_and_the_description() =>
        StepParser.Parse("type \"cats\" into the search box").ShouldBe(new TypeStep("cats", "the search box"));

    [Fact]
    public void A_secret_placeholder_is_kept_as_written() =>
        StepParser.Parse("type \"{{secret:portal-password}}\" into the password field")
            .ShouldBe(new TypeStep("{{secret:portal-password}}", "the password field"));

    [Fact]
    public void Empty_text_is_allowed_to_clear_a_field() =>
        StepParser.Parse("type \"\" into the search box").ShouldBe(new TypeStep("", "the search box"));

    [Theory]
    [InlineData("press enter", "enter")]
    [InlineData("press ctrl+s", "ctrl+s")]
    [InlineData("press  Ctrl+Shift+Tab ", "Ctrl+Shift+Tab")]
    [InlineData("press \"alt+f4\"", "alt+f4")]
    public void Press_takes_the_rest_of_the_line_as_the_keys(string line, string keys) =>
        StepParser.Parse(line).ShouldBe(new PressStep(keys));

    [Fact]
    public void Expect_alone_checks_the_element_is_shown() =>
        StepParser.Parse("expect the results list").ShouldBe(new ExpectShownStep("the results list"));

    [Fact]
    public void Expect_contains_checks_the_elements_text() =>
        StepParser.Parse("expect the search box contains \"cats\"").ShouldBe(new ExpectContainsStep("the search box", "cats"));

    [Fact]
    public void Expect_title_checks_the_window_title() =>
        StepParser.Parse("expect title contains \"cats\"").ShouldBe(new ExpectTitleStep("cats"));

    [Fact]
    public void Expect_title_without_contains_is_an_element() =>
        StepParser.Parse("expect title bar").ShouldBe(new ExpectShownStep("title bar"));

    [Fact]
    public void Commands_and_into_ignore_case() =>
        StepParser.Parse("TYPE \"cats\" INTO the Search box").ShouldBe(new TypeStep("cats", "the Search box"));

    [Fact]
    public void Quotes_and_backslashes_can_be_typed_with_a_backslash() =>
        StepParser.Parse(@"type ""say \""hi\"" C:\\temp"" into the note").ShouldBe(new TypeStep(@"say ""hi"" C:\temp", "the note"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# log in first")]
    [InlineData("  # indented comment")]
    public void Blank_lines_and_comments_are_not_steps(string line) =>
        StepParser.Parse(line).ShouldBeNull();

    [Theory]
    [InlineData("click the search button")]
    [InlineData("type \"say \\\"hi\\\"\" into the note")]
    [InlineData("press ctrl+s")]
    [InlineData("expect the results list")]
    [InlineData("expect the next link contains \"2\"")]
    [InlineData("expect title contains \"cats\"")]
    public void A_step_is_shown_back_as_it_can_be_written(string line) =>
        StepParser.Parse(StepParser.Parse(line)!.ToString()).ShouldBe(StepParser.Parse(line));

    [Theory]
    [InlineData("open the search box", "Unknown command \"open\". A step starts with click, type, press or expect.")]
    [InlineData("\"Search\"", "A step starts with click, type, press or expect.")]
    [InlineData("click", "Say what to click, e.g. click the search button.")]
    [InlineData("click \"\"", "Say what to click, e.g. click the search button.")]
    [InlineData("type cats into the search box", "Put the text in quotes, e.g. type \"cats\" into the search box.")]
    [InlineData("type \"cats", "A quote is not closed. Text goes between two \" marks.")]
    [InlineData("type \"cats\" the search box", "Say where to type with \"into\", e.g. type \"cats\" into the search box.")]
    [InlineData("type \"cats\" into", "Say what to type into, e.g. type \"cats\" into the search box.")]
    [InlineData("press", "Say which keys to press, e.g. press enter or press ctrl+s.")]
    [InlineData("expect", "Say what should be on the screen, e.g. expect the results list.")]
    [InlineData("expect the search box contains cats", "Put the text in quotes, e.g. expect the search box contains \"cats\".")]
    [InlineData("expect contains \"cats\"", "Say what should contain the text, e.g. expect the search box contains \"cats\".")]
    public void A_wrong_line_says_what_is_wrong(string line, string message) =>
        Should.Throw<FormatException>(() => StepParser.Parse(line)).Message.ShouldBe(message);
}
