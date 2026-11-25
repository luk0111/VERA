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
    [InlineData("scroll down 3 in the results list", ScrollDirection.Down, 3, "the results list")]
    [InlineData("Scroll UP 10 IN the page", ScrollDirection.Up, 10, "the page")]
    [InlineData("scroll left 1 in \"the table\"", ScrollDirection.Left, 1, "the table")]
    [InlineData("scroll right 50 in the timeline", ScrollDirection.Right, 50, "the timeline")]
    public void A_scroll_step_has_a_direction_notches_and_an_element(string line, ScrollDirection direction, int notches, string target) =>
        StepParser.Parse(line).ShouldBe(new ScrollStep(direction, notches, target));

    [Theory]
    [InlineData("scroll down until the orange cat", ScrollDirection.Down, "the orange cat")]
    [InlineData("Scroll UP until you see the title", ScrollDirection.Up, "the title")]
    [InlineData("scroll right until \"the last column\"", ScrollDirection.Right, "the last column")]
    public void Scroll_until_has_a_direction_and_an_element(string line, ScrollDirection direction, string target)
    {
        var step = StepParser.Parse(line);

        step.ShouldBe(new ScrollUntilStep(direction, target));
        StepParser.Parse(step!.ToString()!).ShouldBe(step);
    }

    [Theory]
    [InlineData("scroll down until")]
    [InlineData("scroll down until you see")]
    public void Scroll_until_needs_an_element(string line) =>
        Should.Throw<FormatException>(() => StepParser.Parse(line)).Message.ShouldBe("Say what to scroll to, e.g. scroll down until the orange cat.");

    [Theory]
    [InlineData("drag the report file to the Documents folder", "the report file", "the Documents folder")]
    [InlineData("drag the Go to button to the toolbar", "the Go to button", "the toolbar")]
    [InlineData("drag the file to \"the Go to button\"", "the file", "the Go to button")]
    [InlineData("DRAG \"card 1\" TO the Done column", "card 1", "the Done column")]
    public void A_drag_step_splits_at_the_last_to_outside_quotes(string line, string source, string destination) =>
        StepParser.Parse(line).ShouldBe(new DragStep(source, destination));

    [Theory]
    [InlineData("hover over the help icon", "the help icon")]
    [InlineData("Hover Over \"Recent\"", "Recent")]
    public void A_hover_step_needs_over(string line, string target) =>
        StepParser.Parse(line).ShouldBe(new HoverStep(target));

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
    [InlineData("scroll down 3 in the results list")]
    [InlineData("drag the Go to button to the toolbar")]
    [InlineData("drag the file to \"the Go to button\"")]
    [InlineData("hover over the help icon")]
    public void A_step_is_shown_back_as_it_can_be_written(string line) =>
        StepParser.Parse(StepParser.Parse(line)!.ToString()).ShouldBe(StepParser.Parse(line));

    [Theory]
    [InlineData("open the search box", "Unknown command \"open\". A step starts with click, type, press, scroll, drag, hover or expect.")]
    [InlineData("\"Search\"", "A step starts with click, type, press, scroll, drag, hover or expect.")]
    [InlineData("scroll down in the list", "Write scroll, a direction (up, down, left or right), how many notches, then \"in\" and the element, e.g. scroll down 3 in the results list.")]
    [InlineData("scroll 3 in the list", "Write scroll, a direction (up, down, left or right), how many notches, then \"in\" and the element, e.g. scroll down 3 in the results list.")]
    [InlineData("scroll down 3", "Write scroll, a direction (up, down, left or right), how many notches, then \"in\" and the element, e.g. scroll down 3 in the results list.")]
    [InlineData("scroll down 0 in the list", "Scroll between 1 and 50 notches, e.g. scroll down 3 in the results list.")]
    [InlineData("scroll down 51 in the list", "Scroll between 1 and 50 notches, e.g. scroll down 3 in the results list.")]
    [InlineData("drag the file", "Say what to drag and where to drop it with \"to\", e.g. drag the report file to the Documents folder.")]
    [InlineData("drag the file to \"\"", "Say where to drop it, e.g. drag the report file to the Documents folder.")]
    [InlineData("hover the help icon", "Write hover over and the element, e.g. hover over the help icon.")]
    [InlineData("hover over", "Say what to hover over, e.g. hover over the help icon.")]
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
