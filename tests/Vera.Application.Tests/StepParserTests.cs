namespace Vera.Application.Tests;

public sealed class StepParserTests
{
    [Fact]
    public void Click_reads_the_quoted_name() =>
        StepParser.Parse("click \"Search\"").ShouldBe(new ClickStep(new ElementQuery("Search")));

    [Fact]
    public void Click_can_name_a_role() =>
        StepParser.Parse("click button \"Search\"").ShouldBe(new ClickStep(new ElementQuery("Search", "button")));

    [Fact]
    public void Type_reads_the_text_and_the_field() =>
        StepParser.Parse("type \"cats\" into field \"Search\"").ShouldBe(new TypeStep("cats", new ElementQuery("Search", "field")));

    [Fact]
    public void A_secret_placeholder_is_kept_as_written() =>
        StepParser.Parse("type \"{{secret:portal-password}}\" into \"Password\"")
            .ShouldBe(new TypeStep("{{secret:portal-password}}", new ElementQuery("Password")));

    [Fact]
    public void Empty_text_is_allowed_to_clear_a_field() =>
        StepParser.Parse("type \"\" into \"Search\"").ShouldBe(new TypeStep("", new ElementQuery("Search")));

    [Theory]
    [InlineData("press enter", "enter")]
    [InlineData("press ctrl+s", "ctrl+s")]
    [InlineData("press  Ctrl+Shift+Tab ", "Ctrl+Shift+Tab")]
    [InlineData("press \"alt+f4\"", "alt+f4")]
    public void Press_takes_the_rest_of_the_line_as_the_keys(string line, string keys) =>
        StepParser.Parse(line).ShouldBe(new PressStep(keys));

    [Fact]
    public void Expect_alone_checks_the_element_is_shown() =>
        StepParser.Parse("expect \"Results for cats\"").ShouldBe(new ExpectShownStep(new ElementQuery("Results for cats")));

    [Fact]
    public void Expect_contains_checks_the_elements_text() =>
        StepParser.Parse("expect field \"Search\" contains \"cats\"")
            .ShouldBe(new ExpectContainsStep(new ElementQuery("Search", "field"), "cats"));

    [Fact]
    public void Expect_title_checks_the_window_title() =>
        StepParser.Parse("expect title contains \"cats\"").ShouldBe(new ExpectTitleStep("cats"));

    [Fact]
    public void A_quoted_title_is_an_element_name_not_the_window_title() =>
        StepParser.Parse("expect \"title\"").ShouldBe(new ExpectShownStep(new ElementQuery("title")));

    [Fact]
    public void Keywords_and_roles_ignore_case() =>
        StepParser.Parse("TYPE \"cats\" INTO Field \"Search\"").ShouldBe(new TypeStep("cats", new ElementQuery("Search", "field")));

    [Fact]
    public void Quotes_and_backslashes_can_be_typed_with_a_backslash() =>
        StepParser.Parse(@"type ""say \""hi\"" C:\\temp"" into ""Note""").ShouldBe(new TypeStep(@"say ""hi"" C:\temp", new ElementQuery("Note")));

    [Fact]
    public void A_name_is_trimmed() =>
        StepParser.Parse("click \"  Search \"").ShouldBe(new ClickStep(new ElementQuery("Search")));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# log in first")]
    [InlineData("  # indented comment")]
    public void Blank_lines_and_comments_are_not_steps(string line) =>
        StepParser.Parse(line).ShouldBeNull();

    [Theory]
    [InlineData("click button \"Search\"")]
    [InlineData("type \"say \\\"hi\\\"\" into field \"Note\"")]
    [InlineData("press ctrl+s")]
    [InlineData("expect \"Results\"")]
    [InlineData("expect link \"Next\" contains \"2\"")]
    [InlineData("expect title contains \"cats\"")]
    public void A_step_is_shown_back_as_it_can_be_written(string line) =>
        StepParser.Parse(StepParser.Parse(line)!.ToString()).ShouldBe(StepParser.Parse(line));

    [Fact]
    public void Each_role_word_stands_for_its_roles()
    {
        new ElementQuery("x", "button").Roles.ShouldBe(["Button", "SplitButton"]);
        new ElementQuery("x", "field").Roles.ShouldBe(["Edit"]);
        new ElementQuery("x").Roles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("open \"Search\"", "Unknown command \"open\". A step starts with click, type, press or expect.")]
    [InlineData("\"Search\"", "A step starts with click, type, press or expect.")]
    [InlineData("click", "Put the name in quotes, e.g. click \"Search\".")]
    [InlineData("click button", "Put the name in quotes, e.g. click \"Search\".")]
    [InlineData("click \"\"", "The name in quotes can't be empty.")]
    [InlineData("click \"Search", "A quote is not closed. Names and text go between two \" marks.")]
    [InlineData("click \"Search\" now", "Unexpected \"now\" at the end of the step.")]
    [InlineData("type cats into \"Search\"", "Put the text in quotes, e.g. type \"cats\" into \"Search\".")]
    [InlineData("type \"cats\" \"Search\"", "Say where to type with \"into\", e.g. type \"cats\" into \"Search\".")]
    [InlineData("type \"cats\" into", "Put the name in quotes, e.g. type \"cats\" into \"Search\".")]
    [InlineData("press", "Say which keys to press, e.g. press enter or press ctrl+s.")]
    [InlineData("expect", "Put the name in quotes, e.g. expect \"Results\".")]
    [InlineData("expect \"Search\" has \"cats\"", "Expected \"contains\", e.g. expect field \"Search\" contains \"cats\".")]
    [InlineData("expect \"Search\" contains cats", "Put the text in quotes, e.g. expect field \"Search\" contains \"cats\".")]
    [InlineData("expect title", "Say what the title should contain, e.g. expect title contains \"cats\".")]
    public void A_wrong_line_says_what_is_wrong(string line, string message) =>
        Should.Throw<FormatException>(() => StepParser.Parse(line)).Message.ShouldBe(message);

    [Fact]
    public void An_unknown_role_lists_the_roles()
    {
        var error = Should.Throw<FormatException>(() => StepParser.Parse("click buton \"Search\""));

        error.Message.ShouldBe(
            "Unknown role \"buton\". Use one of: button, field, link, checkbox, radio, combo, menu, tab, item, slider. Or put the name in quotes, e.g. click \"Search\".");
    }

    [Fact]
    public void A_name_without_quotes_is_explained_too()
    {
        var error = Should.Throw<FormatException>(() => StepParser.Parse("click Search"));

        error.Message.ShouldStartWith("Unknown role \"Search\".");
        error.Message.ShouldEndWith("Or put the name in quotes, e.g. click \"Search\".");
    }
}
