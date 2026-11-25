namespace Vera.Application.Tests;

public sealed class StepChainTests
{
    private static readonly StepCommand A = new ClickStep("A");
    private static readonly StepCommand B = new ClickStep("B");

    [Fact]
    public void An_empty_list_has_nothing_to_lose()
    {
        new StepChain().ShouldSatisfyAllConditions(c => c.Own.ShouldBeFalse(), c => c.Unsaved.ShouldBeFalse());
    }

    [Fact]
    public void Written_steps_are_unsaved_until_they_are_saved()
    {
        var chain = StepChain.Written([A, B]);
        chain.Unsaved.ShouldBeTrue();

        chain.MarkSaved("log in");

        chain.Unsaved.ShouldBeFalse();
        chain.Name.ShouldBe("log in");
    }

    [Fact]
    public void Saved_or_loaded_steps_are_unsaved_again_after_a_change()
    {
        var chain = StepChain.Loaded("log in", [A]);
        chain.Unsaved.ShouldBeFalse();

        chain.Steps.Add(B);

        chain.Unsaved.ShouldBeTrue();
    }

    [Fact]
    public void Proposed_steps_are_the_testers_only_after_a_change()
    {
        var chain = StepChain.Proposed([A]);
        chain.Unsaved.ShouldBeFalse();

        chain.Steps.Add(B);

        chain.ShouldSatisfyAllConditions(c => c.Own.ShouldBeTrue(), c => c.Unsaved.ShouldBeTrue());
    }

    [Fact]
    public void Changes_to_a_copy_leave_the_first_one_alone()
    {
        var chain = StepChain.Loaded("log in", [A]);

        var copy = chain.Copy();
        copy.Steps.Clear();
        copy.Steps.Add(B);

        chain.Steps.ShouldBe([A]);
        copy.Name.ShouldBe("log in");
        copy.Unsaved.ShouldBeTrue();
    }
}
