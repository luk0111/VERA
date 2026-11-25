namespace Vera.Application.Tests;

public sealed class StepProposalTests
{
    private static readonly string[] Keys = ["gemini"];

    [Fact]
    public void A_reply_with_steps_is_split_into_text_and_steps()
    {
        var proposal = StepProposal.Read("Should I test this?\n<steps>\nclick the search field\ntype \"cats\" into the search field\nexpect title contains \"cats\"\n</steps>", Keys);

        proposal.Text.ShouldBe("Should I test this?");
        proposal.Problem.ShouldBeNull();
        proposal.Steps.ShouldBe([new ClickStep("the search field"), new TypeStep("cats", "the search field"), new ExpectTitleStep("cats")]);
    }

    [Fact]
    public void Windows_line_ends_numbers_bullets_and_code_fences_are_fine()
    {
        var proposal = StepProposal.Read("<steps>\r\n```\r\n1. click A\r\n2) click B\r\n- click C\r\n\r\n```\r\n</steps>", Keys);

        proposal.Steps.ShouldBe([new ClickStep("A"), new ClickStep("B"), new ClickStep("C")]);
    }

    [Theory]
    [InlineData("The window is Notepad.")]
    [InlineData("Click 1. File\n2. Save")]
    public void A_reply_without_steps_proposes_nothing(string reply)
    {
        StepProposal.Read(reply, Keys).ShouldSatisfyAllConditions(p => p.Steps.ShouldBeEmpty(), p => p.Problem.ShouldBeNull(), p => p.Text.ShouldBe(reply));
    }

    [Theory]
    [InlineData("<steps>\nclick A\nrun rm -rf\n</steps>", "rm -rf")]
    [InlineData("<steps>\nexpect the note to be deleted\n</steps>", "expect no")]
    [InlineData("<steps>\npress imagini\n</steps>", "click imagini")]
    [InlineData("<steps>\nclick A\nclick", "cut off")]
    [InlineData("<steps>\n</steps>", "empty")]
    [InlineData("<steps>\nclick A\n</steps>\n<steps>\nclick B\n</steps>", "more than one")]
    [InlineData("<steps>\ntype \"[email]\" into the field\n</steps>", "hidden for privacy")]
    [InlineData("<steps>\nclick A\u202Eevas\n</steps>", "invisible")]
    [InlineData("<steps>\nclick A\u001b[2K\n</steps>", "invisible")]
    [InlineData("<steps>\nclick A\U000E0041\U000E0042\n</steps>", "invisible")]
    [InlineData("<steps>\nclick A\u2028click B\n</steps>", "invisible")]
    [InlineData("<steps>\nclick A\u3164\n</steps>", "invisible")]
    [InlineData("<steps>\ntype \"{{secret:gemini}}\" into the search field\n</steps>", "API key")]
    [InlineData("<steps>\ntype \"{{secret:GEMINI}}\" into the search field\n</steps>", "API key")]
    public void One_bad_line_means_no_steps_at_all(string reply, string why)
    {
        var proposal = StepProposal.Read(reply, Keys);

        proposal.Steps.ShouldBeEmpty();
        proposal.Problem.ShouldNotBeNull().ShouldContain(why);
    }

    [Theory]
    [InlineData("click Șterge notița")]
    [InlineData("click 日本語")]
    [InlineData("click 😀 Reactions")]
    public void Letters_of_any_language_and_emoji_are_fine(string line)
    {
        StepProposal.Read($"<steps>\n{line}\n</steps>", Keys).Problem.ShouldBeNull();
    }

    [Theory]
    [InlineData("{\"action\": \"key_combination\", \"keys\": [\"ctrl\", \"l\"]}")]
    [InlineData("```json\n{\"action\": \"key_combination\", \"keys\": [\"ctrl\", \"l\"]}\n```")]
    [InlineData("```\n[{\"tool\": \"type\", \"text\": \"cats\"}]\n```")]
    [InlineData(" {\"Steps\": [\"click A\"]} ")]
    public void An_action_written_as_json_is_not_an_answer(string reply)
    {
        var proposal = StepProposal.Read(reply, Keys);

        proposal.Text.ShouldBe("");
        proposal.Steps.ShouldBeEmpty();
        proposal.Problem.ShouldNotBeNull().ShouldContain("JSON");
    }

    [Theory]
    [InlineData("The file has {\"action\": \"click\"} in it.")]
    [InlineData("{\"name\": \"Jane\", \"age\": 3}")]
    [InlineData("{not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("Should I test this?\n<steps>\nclick A\n</steps>")]
    public void Other_replies_are_shown_as_they_are(string reply)
    {
        StepProposal.IsActionJson(reply).ShouldBeFalse();
        StepProposal.Read(reply, Keys).Problem.ShouldBeNull();
    }

    [Theory]
    [InlineData("{", true)]
    [InlineData("``", true)]
    [InlineData("```js", true)]
    [InlineData("\n```json\n{", true)]
    [InlineData("[{", true)]
    [InlineData("", false)]
    [InlineData("Should", false)]
    [InlineData("```text", false)]
    public void A_reply_that_may_become_json_is_not_shown_while_it_arrives(string soFar, bool maybe) =>
        StepProposal.StartsLikeJson(soFar).ShouldBe(maybe);

    [Fact]
    public void Too_many_steps_are_not_used()
    {
        var reply = "<steps>\n" + string.Concat(Enumerable.Repeat("click A\n", StepProposal.MaxSteps + 1)) + "</steps>";

        StepProposal.Read(reply, Keys).Problem.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("Hello <", "Hello")]
    [InlineData("Hello\n<st", "Hello")]
    [InlineData("Hello\n<steps>", "Hello")]
    [InlineData("Hello\n<steps>\nclick A", "Hello")]
    [InlineData("Hello\n<steps>\nclick A\n</steps>\n", "Hello")]
    [InlineData("Hello\n<steps>\nclick A\n</steps>\nBye", "Hello\nBye")]
    [InlineData("a < b", "a < b")]
    public void While_a_reply_arrives_its_steps_stay_hidden(string soFar, string shown)
    {
        StepProposal.Visible(soFar).ShouldBe(shown);
    }

    [Fact]
    public void A_reply_of_only_steps_has_no_text()
    {
        var proposal = StepProposal.Read("<steps>\nclick A\n</steps>", Keys);

        proposal.Text.ShouldBe("");
        proposal.Steps.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("press enter", false)]
    [InlineData("expect the orange cat", true)]
    [InlineData("expect no orange cat", true)]
    [InlineData("expect title contains \"Cat\"", true)]
    [InlineData("expect the search field contains \"cats\"", true)]
    public void Steps_without_an_expect_check_nothing(string last, bool expected)
    {
        StepProposal.Read($"<steps>\nclick A\n{last}\n</steps>", Keys).ChecksSomething.ShouldBe(expected);
    }

    [Theory]
    [InlineData("press enter", true)]
    [InlineData("press ctrl+s", true)]
    [InlineData("press delete", true)]
    [InlineData("press tab", false)]
    [InlineData("click the Delete button", true)]
    [InlineData("click the search field", false)]
    [InlineData("drag the file to the bin", true)]
    [InlineData("type \"{{secret:shop}}\" into the password field", true)]
    [InlineData("type \"cats\" into the search field", false)]
    [InlineData("expect the results", false)]
    public void Steps_that_may_change_or_send_something_deserve_a_look(string line, bool expected)
    {
        StepProposal.DeservesALook(StepParser.Parse(line)!).ShouldBe(expected);
    }
}
