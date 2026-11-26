using Vera.Core;

namespace Vera.Application.Tests;

public sealed class FoundStepTests
{
    [Fact]
    public void A_found_button_becomes_a_click_step_by_its_name()
    {
        FoundStep.Click(Found("Button", "Search"), "the search thing").ShouldNotBeNull().ToString().ShouldBe("click button \"Search\"");
    }

    [Fact]
    public void A_found_field_becomes_a_type_step_and_keeps_secret_placeholders()
    {
        FoundStep.Type(Found("Edit", "Password"), "the password box", "{{secret:gmail}}").ShouldNotBeNull().ToString()
            .ShouldBe("type \"{{secret:gmail}}\" into field \"Password\"");
    }

    [Fact]
    public void A_found_element_becomes_an_expect_step()
    {
        FoundStep.Expect(Found("Hyperlink", "Contact"), "contact").ShouldNotBeNull().ToString().ShouldBe("expect link \"Contact\"");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Delete", true)]
    [InlineData("First line\nSecond line", false)]
    public void When_the_name_does_not_point_at_one_element_the_testers_words_are_used(string name, bool sameNameElsewhere)
    {
        FoundStep.Click(Found("Button", name, sameNameElsewhere), "the delete button of the second file").ShouldNotBeNull().ToString()
            .ShouldBe("click the delete button of the second file");
    }

    [Fact]
    public void A_name_with_the_word_contains_is_used_as_the_name()
    {
        FoundStep.Expect(Found("Button", "Box contains eggs"), "the egg button").ShouldNotBeNull().ToString().ShouldBe("expect button \"Box contains eggs\"");
    }

    [Fact]
    public void Nothing_is_written_when_neither_can_be_a_step()
    {
        FoundStep.Expect(Found("Button", ""), "the box that contains eggs").ShouldBeNull();
        FoundStep.Click(new FindResult(false, null, "", "x.png"), "the search button").ShouldBeNull();
    }

    [Theory]
    [InlineData("Say \"hi\"")]
    [InlineData("C:\\Temp")]
    [InlineData("Go to page")]
    [InlineData("Salvează ca…")]
    [InlineData("# heading")]
    public void Every_step_reads_back_from_a_saved_file_as_it_was(string name)
    {
        foreach (var step in new[] { FoundStep.Click(Found("Button", name), "it"), FoundStep.Expect(Found("Button", name), "it"), FoundStep.Type(Found("Edit", name), "it", "a \"b\" \\ c") })
        {
            StepParser.Parse(step.ShouldNotBeNull().ToString()).ShouldBe(step);
        }
    }

    private static FindResult Found(string role, string name, bool sameNameElsewhere = false) =>
        new(true, new UiElement(1, role, name, new ScreenRect(0, 0, 10, 10), true, "loc"), "", "x.png", sameNameElsewhere);
}
