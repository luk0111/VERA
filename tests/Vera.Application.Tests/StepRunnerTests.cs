using SkiaSharp;

using Vera.Core;
using Vera.Input;
using Vera.Llm;

namespace Vera.Application.Tests;

public sealed class StepRunnerTests : IDisposable
{
    private static readonly UiElement Window = new(0, "Window", "Shop - cats", new ScreenRect(100, 100, 400, 300), true, "window");
    private static readonly UiElement SearchBox = new(1, "Edit", "Search", new ScreenRect(110, 110, 200, 20), true, "q", "");
    private static readonly UiElement SearchButton = new(2, "Button", "Search", new ScreenRect(320, 110, 60, 20), true, "go");
    private static readonly UiElement Results = new(3, "Hyperlink", "Results for cats", new ScreenRect(110, 150, 200, 20), true, "r");
    private static readonly UiElement Delete = new(4, "Button", "Delete account", new ScreenRect(110, 200, 120, 20), true, "del");

    private readonly string _output = Path.Combine(Path.GetTempPath(), "vera-steps-tests", Guid.NewGuid().ToString("N"));
    private readonly IAppAdapter _adapter = Substitute.For<IAppAdapter>();
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();

    public StepRunnerTests()
    {
        ScreenShows(Window, SearchBox, SearchButton, Results, Delete);

        // The model finds nothing unless a test says what it points at.
        _llm.DecideNextActionAsync(Arg.Any<TestTask>(), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentAction(AgentActionType.Fail, null, null, "There is no such element."));
        ModelPoints("the search box", 1);
        ModelPoints("the search button", 2);
        ModelPoints("the results", 3);
        ModelPoints("the delete button", 4);

        // Like a real store, no secret is saved unless a test saves one (NSubstitute would answer "").
        _secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Steps_that_all_work_pass()
    {
        var result = await RunAsync("type \"cats\" into the search box", "click the search button", "expect the results");

        result.Verdict.ShouldBe(Verdict.Passed);
        result.Steps.Select(s => s.Outcome).ShouldBe([StepOutcome.Done, StepOutcome.Done, StepOutcome.Done]);
        result.Steps.Select(s => s.Message).ShouldBe([
            "typed into field \"Search\" (element 1)",
            "clicked button \"Search\" (element 2)",
            "link \"Results for cats\" is shown"]);
    }

    [Fact]
    public async Task The_model_is_asked_to_find_the_element_as_in_Find()
    {
        await RunAsync("click the search button");

        await _llm.Received(1).DecideNextActionAsync(
            Arg.Is<TestTask>(t => t.Description == "Click the search button" && t.ExpectedResult == "The search button has been clicked" && t.Target.ProcessId == 1234),
            Arg.Is<ScreenState>(s => s.Elements.Count == 5),
            Arg.Is<IReadOnlyList<AgentStep>>(h => h.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_command_decides_the_action_not_the_model()
    {
        // The model answers "type" for the button, but the step says click.
        _llm.DecideNextActionAsync(Arg.Is<TestTask>(t => t.Description == "Click the search button"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentAction(AgentActionType.Type, 2, "something", "It's the button"));

        await RunAsync("click the search button");

        await _adapter.Received(1).ClickAsync(2, Arg.Any<CancellationToken>());
        await _adapter.DidNotReceiveWithAnyArgs().TypeAsync(default, default!, Ct);
    }

    [Fact]
    public async Task Pressing_keys_and_checking_the_title_need_no_model()
    {
        await RunAsync("press enter", "expect title contains \"cats\"");

        await _llm.DidNotReceiveWithAnyArgs().DecideNextActionAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task The_window_is_read_before_each_step()
    {
        await RunAsync("type \"cats\" into the search box", "click the search button", "press enter");

        Received.InOrder(() =>
        {
            _adapter.StartAsync(Arg.Is<AppTarget>(t => t.ProcessId == 1234), Arg.Any<CancellationToken>());
            _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>());
            _adapter.TypeAsync(1, "cats", Arg.Any<CancellationToken>());
            _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>());
            _adapter.ClickAsync(2, Arg.Any<CancellationToken>());
            _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>());
            _adapter.PressKeyAsync("enter", Arg.Any<CancellationToken>());
        });
    }

    [Theory]
    [InlineData("scroll down 3 in the results", 3, 0)]
    [InlineData("scroll up 2 in the results", -2, 0)]
    [InlineData("scroll right 4 in the results", 0, 4)]
    [InlineData("scroll left 1 in the results", 0, -1)]
    public async Task A_scroll_step_scrolls_in_the_element_the_model_found(string line, int down, int right)
    {
        var result = await RunAsync(line);

        result.Verdict.ShouldBe(Verdict.Passed);
        await _adapter.Received(1).ScrollAsync(3, down, right, Arg.Any<CancellationToken>());
        result.Steps[0].Message.ShouldStartWith("scrolled ");
        result.Steps[0].Message.ShouldEndWith("in link \"Results for cats\" (element 3)");
    }

    [Theory]
    [InlineData("scroll down 6 in the page")]
    [InlineData("scroll down 6 in the window")]
    [InlineData("scroll down 6 in the browser window")]
    public async Task Scrolling_the_whole_page_needs_no_element(string line)
    {
        ScreenShows(Window);

        var result = await RunAsync(line);

        result.Verdict.ShouldBe(Verdict.Passed);
        await _adapter.Received(1).ScrollAsync(0, 6, 0, Arg.Any<CancellationToken>());
        result.Steps[0].Message.ShouldBe("scrolled down 6 in the window");
    }

    [Theory]
    [InlineData("click the page")]
    [InlineData("hover over the window")]
    public async Task Only_a_scroll_takes_the_whole_window(string line)
    {
        ScreenShows(Window);

        (await RunAsync(line)).Verdict.ShouldBe(Verdict.Inconclusive);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(700, 10)]
    [InlineData(1500, 40)]
    [InlineData(350, 100)]
    [InlineData(350, 120)]
    [InlineData(480, 200)]
    [InlineData(900, 100)]
    public async Task Scroll_until_stops_when_the_element_shows(int catAt, int pixelsPerNotch)
    {
        var page = new Page(pageHeight: 2400, pixelsPerNotch, catAt, _adapter, _llm);

        var result = await RunAsync("scroll down until the orange cat", "hover over the orange cat");

        result.Verdict.ShouldBe(Verdict.Passed, result.Steps[^1].Message);
        result.Steps[0].Message.ShouldStartWith("found link \"Orange cat\" ");
        page.Scrolls.ShouldBeLessThanOrEqualTo(StepRunner.MaxScrolls);
        await _adapter.ReceivedWithAnyArgs(1).HoverAsync(default, Ct);
    }

    [Fact]
    public async Task Scroll_until_moves_less_than_a_window_at_a_time_once_it_knows_the_distance()
    {
        var page = new Page(pageHeight: 2400, pixelsPerNotch: 10, catAt: 1500, _adapter, _llm);

        await RunAsync("scroll down until the orange cat");

        page.Notches[0].ShouldBe(StepRunner.FirstNotches);
        page.Notches.Take(4).ShouldBe([3, 6, 12, 18]);
        page.Notches.Skip(4).ShouldAllBe(n => n == 18);
    }

    [Fact]
    public async Task Scroll_until_first_waits_for_a_page_that_is_still_opening()
    {
        ScreensShow(Screen("Old", Window, Delete), Screen("Shop", Window, SearchBox), Screen("Shop", Window, SearchBox, SearchButton, Results));

        var result = await RunWaitingAsync("scroll down until the results");

        result.Verdict.ShouldBe(Verdict.Passed);
        await ModelWasAsked(1);
        await _adapter.DidNotReceiveWithAnyArgs().ScrollAsync(default, default, default, Ct);
    }

    [Theory]
    [InlineData(AgentActionType.Done)]
    [InlineData(AgentActionType.Wait)]
    public async Task A_field_that_already_has_the_focus_is_found_when_the_model_first_says_done(AgentActionType first)
    {
        ModelAnswers("the search box", new AgentAction(first, null, null, "It is already focused"));
        ModelPoints("the search box, even if it is already selected or focused", 1);

        var result = await RunAsync("type \"cats\" into the search box");

        result.Verdict.ShouldBe(Verdict.Passed);
        await _adapter.Received(1).TypeAsync(1, "cats", Arg.Any<CancellationToken>());
        await ModelWasAsked(2);
    }

    [Theory]
    [InlineData("expect the results", true)]
    [InlineData("scroll down until the results", true)]
    [InlineData("click the results", false)]
    public async Task A_check_or_a_search_must_match_and_not_be_the_closest_thing(string line, bool mustMatch)
    {
        await RunAsync(line);

        await _llm.Received(1).DecideNextActionAsync(
            Arg.Is<TestTask>(t => t.ExpectedResult.Contains("don't pick the closest thing", StringComparison.Ordinal) == mustMatch),
            Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expect_no_fails_when_it_is_still_there()
    {
        var result = await RunAsync("expect no results");

        result.Verdict.ShouldBe(Verdict.Failed);
        result.Steps[0].Message.ShouldBe("link \"Results for cats\" is still shown.");
    }

    [Fact]
    public async Task Expect_no_passes_when_the_model_sees_none()
    {
        var result = await RunAsync("expect no moon");

        result.Verdict.ShouldBe(Verdict.Passed);
        result.Steps[0].Message.ShouldBe("VERA sees no moon on this screen");
        await _llm.Received(1).DecideNextActionAsync(
            Arg.Is<TestTask>(t => t.ExpectedResult.Contains("Answer fail only if", StringComparison.Ordinal)),
            Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expect_no_waits_for_it_to_go()
    {
        ScreensShow(Screen("Shop", Window, Results), Screen("Shop", Window, Results), Screen("Shop", Window, SearchBox));
        ModelPointsWhenShown("the results", Results);

        (await RunWaitingAsync("expect no results")).Verdict.ShouldBe(Verdict.Passed);
        await ModelWasAsked(2);
    }

    [Fact]
    public async Task Expect_no_does_not_pass_when_nothing_can_be_read()
    {
        ScreenShows(Window);

        (await RunAsync("expect no results")).Verdict.ShouldBe(Verdict.Inconclusive);
    }

    [Fact]
    public async Task A_check_that_something_is_deleted_never_passes_on_the_thing_itself()
    {
        ModelPoints("the results to be deleted", 3);

        var result = await RunAsync("expect the results to be deleted");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Message.ShouldContain("expect no");
    }

    [Fact]
    public async Task An_element_that_is_not_there_is_asked_about_once()
    {
        await RunAsync("click the moon");

        await ModelWasAsked(1);
    }

    [Fact]
    public async Task Scroll_until_stops_at_the_end_of_the_page()
    {
        var page = new Page(pageHeight: 700, pixelsPerNotch: 50, catAt: null, _adapter, _llm);

        var result = await RunAsync("scroll down until the orange cat", "press enter");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps.ShouldHaveSingleItem().Message.ShouldBe("I scrolled down to the end and can't find \"the orange cat\". The model's last answer: No cat");
        page.Scrolls.ShouldBeLessThan(StepRunner.MaxScrolls);
    }

    [Fact]
    public async Task Scroll_until_gives_up_on_a_page_without_end()
    {
        var page = new Page(pageHeight: 1_000_000, pixelsPerNotch: 10, catAt: null, _adapter, _llm);

        var result = await RunAsync("scroll down until the orange cat");

        result.Steps[0].Message.ShouldBe("I scrolled down 10 times and can't find \"the orange cat\". The model's last answer: No cat");
        page.Scrolls.ShouldBe(StepRunner.MaxScrolls);
        await ModelWasAsked(StepRunner.MaxScrolls + 1);
    }

    [Fact]
    public async Task Scroll_until_says_when_the_window_does_not_scroll_and_asks_the_model_once()
    {
        var result = await RunAsync("scroll up until the orange cat");

        result.Steps[0].Message.ShouldBe("I can't find \"the orange cat\", and the window doesn't scroll up. The model's last answer: There is no such element.");
        await _adapter.Received(1).ScrollAsync(0, -StepRunner.FirstNotches, 0, Arg.Any<CancellationToken>());
        await ModelWasAsked(1);
    }

    [Fact]
    public async Task A_drag_step_drags_one_found_element_onto_the_other()
    {
        var result = await RunAsync("drag the results to the search box");

        result.Verdict.ShouldBe(Verdict.Passed);
        await _adapter.Received(1).DragAsync(3, 1, Arg.Any<CancellationToken>());
        result.Steps[0].Message.ShouldBe("dragged link \"Results for cats\" (element 3) to field \"Search\" (element 1)");
    }

    [Fact]
    public async Task A_drag_onto_the_element_itself_is_not_done()
    {
        ModelPoints("the results link", 3);

        var result = await RunAsync("drag the results to the results link");

        result.Steps[0].Outcome.ShouldBe(StepOutcome.NotDone);
        await _adapter.DidNotReceiveWithAnyArgs().DragAsync(default, default, Ct);
    }

    [Fact]
    public async Task A_drop_onto_a_risky_element_is_asked_first_like_a_click()
    {
        string? asked = null;

        var result = await RunAsync(["drag the results to the delete button"], e =>
        {
            asked = e;
            return Task.FromResult(false);
        });

        asked.ShouldBe("drop onto button \"Delete account\"");
        result.Steps[0].Message.ShouldBe("Stopped: dropping onto button \"Delete account\" wasn't approved.");
        await _adapter.DidNotReceiveWithAnyArgs().DragAsync(default, default, Ct);
    }

    [Fact]
    public async Task A_hover_step_rests_on_the_element_the_model_found()
    {
        var result = await RunAsync("hover over the search button");

        result.Verdict.ShouldBe(Verdict.Passed);
        await _adapter.Received(1).HoverAsync(2, Arg.Any<CancellationToken>());
        result.Steps[0].Message.ShouldBe("hovered over button \"Search\" (element 2)");
    }

    [Fact]
    public async Task A_secret_is_typed_but_never_shown()
    {
        _secrets.GetAsync("pw", Arg.Any<CancellationToken>()).Returns("s3cret");

        var result = await RunAsync("type \"{{secret:pw}}\" into the search box");

        await _adapter.Received(1).TypeAsync(1, "s3cret", Arg.Any<CancellationToken>());
        result.Steps[0].Step.ToString().ShouldBe("type \"{{secret:pw}}\" into the search box");
        result.Steps[0].Message.ShouldNotContain("s3cret");
    }

    [Fact]
    public async Task A_secret_that_is_not_saved_stops_the_run_before_typing()
    {
        var result = await RunAsync("type \"{{secret:pw}}\" into the search box", "click the search button");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps.Count.ShouldBe(1);
        result.Steps[0].Message.ShouldBe("Secret \"pw\" isn't saved. Save it in Settings.");
        await _adapter.DidNotReceiveWithAnyArgs().TypeAsync(default, default!, Ct);
    }

    [Fact]
    public async Task An_element_the_model_cannot_find_is_inconclusive_and_stops_the_run()
    {
        var result = await RunAsync("click the log in button", "click the search button");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps.Count.ShouldBe(1);
        result.Steps[0].Outcome.ShouldBe(StepOutcome.NotDone);
        result.Steps[0].Message.ShouldBe("I can't find \"the log in button\" on this screen. There is no such element.");
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
    }

    [Fact]
    public async Task An_unreadable_model_answer_is_inconclusive()
    {
        _llm.DecideNextActionAsync(Arg.Any<TestTask>(), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AgentAction>(new UnreadableAnswerException()));

        var result = await RunAsync("click the search button");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
    }

    [Fact]
    public async Task A_model_that_cannot_be_reached_stops_the_run_and_keeps_the_steps_done()
    {
        var unreachable = new HttpRequestException("Connection refused");
        _llm.DecideNextActionAsync(Arg.Is<TestTask>(t => t.Description == "Click the search button"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AgentAction>(unreachable));

        var result = await RunAsync("press enter", "click the search button", "press tab");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.StoppedBy.ShouldBeSameAs(unreachable);
        result.Steps.Select(s => s.Step.ToString()).ShouldBe(["press enter"]);
        await _adapter.DidNotReceive().PressKeyAsync("tab", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_run_that_is_not_stopped_has_no_error()
    {
        (await RunAsync("press enter")).StoppedBy.ShouldBeNull();
    }

    [Fact]
    public async Task A_stop_by_the_tester_ends_the_run_and_keeps_the_steps_done()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // The tester stops while the model looks for the button.
        _llm.DecideNextActionAsync(Arg.Is<TestTask>(t => t.Description == "Click the search button"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stop.Cancel();
                return Task.FromCanceled<AgentAction>(stop.Token);
            });

        var result = await NewRunner().RunAsync(1234, "Shop", Steps("press enter", "click the search button", "press tab"), null, null, null, stop.Token);

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.StoppedByTester.ShouldBeTrue();
        result.StoppedBy.ShouldBeNull();
        result.Steps.Select(s => s.Step.ToString()).ShouldBe(["press enter"]);
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
        await _adapter.DidNotReceive().PressKeyAsync("tab", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stop_that_a_step_turns_into_its_own_failure_is_still_the_testers_stop()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _adapter.PressKeyAsync("enter", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stop.Cancel();
            return Task.FromException(new InvalidOperationException("The window moved since VERA last looked at it. Read it again."));
        });

        var result = await NewRunner().RunAsync(1234, "Shop", Steps("press enter"), null, null, null, stop.Token);

        result.StoppedByTester.ShouldBeTrue();
        result.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stop_before_the_run_starts_runs_no_step()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();
        _adapter.StartAsync(Arg.Any<AppTarget>(), Arg.Any<CancellationToken>()).Returns(Task.FromCanceled(stop.Token));

        var result = await NewRunner().RunAsync(1234, "Shop", Steps("press enter"), null, null, null, stop.Token);

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.StoppedByTester.ShouldBeTrue();
        result.Steps.ShouldBeEmpty();
        await _adapter.DidNotReceiveWithAnyArgs().PressKeyAsync(default!, Ct);
    }

    [Fact]
    public async Task A_run_that_is_not_stopped_is_not_stopped_by_the_tester()
    {
        (await RunAsync("press enter")).StoppedByTester.ShouldBeFalse();
    }

    [Fact]
    public async Task A_disabled_element_is_not_clicked()
    {
        ScreenShows(Window, SearchButton with { IsEnabled = false });

        var result = await RunAsync("click the search button");

        result.Steps[0].Message.ShouldBe("button \"Search\" is disabled.");
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
    }

    [Fact]
    public async Task Typing_into_something_that_cannot_take_text_is_not_done()
    {
        ModelPoints("the notepad", 2);

        var result = await RunAsync("type \"cats\" into notepad");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Message.ShouldBe("button \"Search\" can't take text. Describe the field or text area to type into.");
        await _adapter.DidNotReceiveWithAnyArgs().TypeAsync(default, default!, Ct);
    }

    [Fact]
    public async Task A_text_area_can_be_typed_into()
    {
        var page = new UiElement(5, "Document", "Text editor", new ScreenRect(110, 240, 380, 150), true, "page");
        ScreenShows(Window, page);
        ModelPoints("the notepad", 5);

        var result = await RunAsync("type \"cats\" into notepad");

        result.Steps[0].Message.ShouldBe("typed into text area \"Text editor\" (element 5)");
        await _adapter.Received(1).TypeAsync(5, "cats", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_expect_the_model_cannot_find_fails_the_run()
    {
        var result = await RunAsync("expect the results for dogs");

        result.Verdict.ShouldBe(Verdict.Failed);
        result.Steps[0].Outcome.ShouldBe(StepOutcome.CheckFailed);
        result.Steps[0].Message.ShouldBe("I can't find \"the results for dogs\" on this screen. There is no such element.");
    }

    [Fact]
    public async Task An_expect_in_a_window_without_elements_is_inconclusive_not_failed()
    {
        ScreenShows(Window);

        var result = await RunAsync("expect the results list");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Outcome.ShouldBe(StepOutcome.NotDone);
        result.Steps[0].Message.ShouldBe("VERA can't see any buttons or fields in this window. Is it minimized?");
        await ModelWasAsked(0);
    }

    [Fact]
    public async Task An_expect_waits_for_a_slow_app()
    {
        ModelPoints("the results list", 3);
        ScreensShow(Screen("Shop", Window, SearchBox), Screen("Shop", Window), Screen("Shop", Window, SearchBox, Results));

        var result = await RunWaitingAsync("expect the results list");

        result.Verdict.ShouldBe(Verdict.Passed);
        await ModelWasAsked(2);
    }

    [Fact]
    public async Task The_model_is_asked_again_only_when_the_window_changed()
    {
        ScreenShows(Window, SearchBox);

        var result = await RunWaitingAsync("expect the results for dogs");

        result.Verdict.ShouldBe(Verdict.Failed);
        await ModelWasAsked(1);
        // Read again at least once; how often depends on the machine's speed (a slow CI runner manages only a few reads in 0.5 s).
        _adapter.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAppAdapter.GetScreenStateAsync)).ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Expect_contains_waits_for_the_text_without_asking_the_model_again()
    {
        ScreensShow(Screen("Shop", Window, SearchBox with { Value = "" }), Screen("Shop", Window, SearchBox with { Value = "cats" }));

        var result = await RunWaitingAsync("expect the search box contains \"cats\"");

        result.Verdict.ShouldBe(Verdict.Passed);
        await ModelWasAsked(1);
    }

    [Fact]
    public async Task Expect_title_waits_for_the_title()
    {
        ScreensShow(Screen("Shop", Window), Screen("Shop - cats", Window));

        (await RunWaitingAsync("expect title contains \"cats\"")).Verdict.ShouldBe(Verdict.Passed);
    }

    [Fact]
    public async Task A_click_waits_for_a_button_to_be_enabled()
    {
        ScreensShow(Screen("Shop", Window, SearchButton with { IsEnabled = false }), Screen("Shop", Window, SearchButton));

        var result = await RunWaitingAsync("click the search button");

        result.Verdict.ShouldBe(Verdict.Passed);
        await _adapter.Received(1).ClickAsync(2, Arg.Any<CancellationToken>());
        await ModelWasAsked(1);
    }

    [Fact]
    public async Task Expect_contains_checks_the_text_in_code()
    {
        ScreenShows(Window, SearchBox with { Value = "cats\r" });

        (await RunAsync("expect the search box contains \"CATS\"")).Verdict.ShouldBe(Verdict.Passed);
        var failed = await RunAsync("expect the search box contains \"dogs\"");

        failed.Verdict.ShouldBe(Verdict.Failed);
        failed.Steps[0].Message.ShouldBe("field \"Search\" has \"cats\", which doesn't contain \"dogs\".");
    }

    [Fact]
    public async Task A_field_whose_text_cannot_be_read_is_inconclusive()
    {
        ScreenShows(Window, SearchBox with { Value = null });

        var result = await RunAsync("expect the search box contains \"cats\"");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Message.ShouldBe("VERA can't read the text of field \"Search\".");
    }

    [Fact]
    public async Task Expect_title_checks_the_window_title()
    {
        (await RunAsync("expect title contains \"cats\"")).Verdict.ShouldBe(Verdict.Passed);
        var failed = await RunAsync("expect title contains \"dogs\"");

        failed.Steps[0].Message.ShouldBe("The title is \"Shop - cats\", which doesn't contain \"dogs\".");
    }

    [Fact]
    public async Task A_risky_click_is_asked_first_and_a_no_stops_the_run()
    {
        string? asked = null;

        var result = await RunAsync(["click the delete button"], e => { asked = e; return Task.FromResult(false); });

        asked.ShouldBe("click button \"Delete account\"");
        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Message.ShouldBe("Stopped: clicking button \"Delete account\" wasn't approved.");
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
    }

    [Theory]
    [InlineData("press delete", true)]
    [InlineData("press the delete key", true)]
    [InlineData("press cmd+backspace", true)]
    [InlineData("press shift+delete", true)]
    [InlineData("press alt+f4", true)]
    [InlineData("press ctrl+w", true)]
    [InlineData("press backspace", false)]
    [InlineData("press enter", false)]
    public async Task A_key_that_deletes_is_asked_first(string line, bool asks)
    {
        var asked = new List<string>();

        var result = await RunAsync([line], what => { asked.Add(what); return Task.FromResult(false); });

        asked.Count.ShouldBe(asks ? 1 : 0);
        result.Verdict.ShouldBe(asks ? Verdict.Inconclusive : Verdict.Passed);
        await _adapter.Received(asks ? 0 : 1).PressKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task After_asking_a_closed_menu_is_opened_again_and_only_then_clicked()
    {
        var menuOpen = false;
        MenuOpens(() => menuOpen, open => menuOpen = open);

        var result = await RunAsync(["click the more button", "click delete note"], _ => { menuOpen = false; return Task.FromResult(true); });

        result.Verdict.ShouldBe(Verdict.Passed, result.Steps[^1].Message);
        await _adapter.Received(2).ClickAsync(5, Arg.Any<CancellationToken>());
        await _adapter.Received(1).ClickAsync(6, Arg.Any<CancellationToken>());
        await ModelWasAsked(2);
    }

    [Fact]
    public async Task A_menu_that_stayed_open_is_not_opened_twice()
    {
        var menuOpen = false;
        MenuOpens(() => menuOpen, open => menuOpen = open);

        var result = await RunAsync(["click the more button", "expect delete note", "click delete note"], _ => Task.FromResult(true));

        result.Verdict.ShouldBe(Verdict.Passed, result.Steps[^1].Message);
        await _adapter.Received(1).ClickAsync(5, Arg.Any<CancellationToken>());
        await _adapter.Received(1).ClickAsync(6, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_menu_that_does_not_open_again_stops_without_a_click()
    {
        var menuOpen = false;
        MenuOpens(() => menuOpen, open => menuOpen = open);

        var result = await RunAsync(["click the more button", "press tab", "click delete note"], _ => { menuOpen = false; return Task.FromResult(true); });

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[^1].Message.ShouldBe("menu \"Delete Note\" isn't there any more after the question, so VERA did nothing.");
        await _adapter.DidNotReceive().ClickAsync(6, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Something_else_that_is_gone_after_the_question_is_not_brought_back_by_repeating_a_step()
    {
        var asked = false;
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(_ => asked ? Screen("Shop", Window, SearchButton) : Screen("Shop", Window, SearchButton, Delete));

        var result = await RunAsync(["click the search button", "click the delete button"], _ => { asked = true; return Task.FromResult(true); });

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        await _adapter.Received(1).ClickAsync(2, Arg.Any<CancellationToken>());
        await _adapter.DidNotReceive().ClickAsync(4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_element_that_moved_while_asking_is_clicked_by_its_new_number()
    {
        var asked = false;
        var moved = Delete with { Id = 1, Bounds = new ScreenRect(300, 300, 120, 20) };
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(_ => asked ? Screen("Shop", Window, moved) : Screen("Shop", Window, SearchButton, Delete));

        await RunAsync(["click the delete button"], _ => { asked = true; return Task.FromResult(true); });

        await _adapter.Received(1).ClickAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VERAs_window_comes_back_before_the_question()
    {
        var window = new FrontWindow(77);
        var front = Substitute.For<IFrontWindow>();
        front.Current().Returns(window);
        var order = new List<string>();
        front.BringBackAsync(window, Arg.Any<CancellationToken>()).Returns(_ => { order.Add("front"); return true; });
        var runner = new StepRunner(() => _adapter, _llm, _secrets, _output, TimeSpan.Zero, TimeSpan.Zero, front);

        await runner.RunAsync(1234, "Shop", Steps("click the search button", "click the delete button"), _ => { order.Add("asked"); return Task.FromResult(true); }, null, null, Ct);

        order.ShouldBe(["front", "asked"]);
    }

    [Fact]
    public async Task A_never_do_click_in_steps_the_AI_proposed_is_not_done_even_when_approved()
    {
        var result = await NewRunner().RunAsync(1234, "Shop", Steps("click the delete button"), _ => Task.FromResult(true), null, null, Ct, proposedByVera: true);

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Message.ShouldBe(
            "Stopped: VERA doesn't act on button \"Delete account\" (\"delete account\") in steps the AI proposed. Write the step yourself to run it.");
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
    }

    [Fact]
    public async Task A_never_do_click_in_steps_the_AI_proposed_is_refused_without_approval_too()
    {
        var result = await NewRunner().RunAsync(1234, "Shop", Steps("click the delete button"), null, null, null, Ct, proposedByVera: true);

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
    }

    [Fact]
    public async Task A_risky_click_that_is_approved_is_done()
    {
        var result = await RunAsync(["click the delete button"], _ => Task.FromResult(true));

        result.Verdict.ShouldBe(Verdict.Passed);
        await _adapter.Received(1).ClickAsync(4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Safe_clicks_and_runs_without_approval_ask_nothing()
    {
        var asks = 0;

        await RunAsync(["click the search button"], _ => { asks++; return Task.FromResult(true); });
        await RunAsync("click the delete button");

        asks.ShouldBe(0);
        await _adapter.Received(1).ClickAsync(4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_error_from_the_app_is_the_steps_message()
    {
        _adapter.ClickAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("The window moved since VERA last looked at it. Read it again.")));

        var result = await RunAsync("click the search button");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Message.ShouldBe("The window moved since VERA last looked at it. Read it again.");
    }

    [Fact]
    public async Task Unknown_keys_are_reported_when_the_step_runs()
    {
        _adapter.PressKeyAsync("entr", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidKeyComboException("Unknown key \"entr\".")));

        var result = await RunAsync("press entr");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Message.ShouldBe("Unknown key \"entr\". To press something on the screen, write click entr.");
    }

    [Fact]
    public async Task Each_step_saves_a_screenshot_in_the_runs_folder()
    {
        var result = await RunAsync("click the search button", "expect the results");

        result.Folder.ShouldStartWith(Path.GetFullPath(_output));
        result.Steps.Select(s => Path.GetFileName(s.ScreenshotPath)).ShouldBe(["01.png", "02.png"]);
        result.Steps.ShouldAllBe(s => File.Exists(s.ScreenshotPath));
    }

    [Fact]
    public async Task Each_step_is_reported_as_soon_as_it_is_finished()
    {
        var reported = new ListProgress<StepResult>();

        await NewRunner().RunAsync(1234, "Shop", Steps("click the search button", "press enter"), null, reported, null, Ct);

        reported.Items.Select(s => s.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task A_running_step_tells_what_VERA_is_doing()
    {
        var told = new ListProgress<StepActivity>();

        await NewRunner().RunAsync(1234, "Shop", Steps("click the search button", "press enter"), null, null, told, Ct);

        told.Items.Select(a => (a.Number, a.Phase)).ShouldBe([
            (1, StepPhase.Reading), (1, StepPhase.Finding), (1, StepPhase.Acting),
            (2, StepPhase.Reading), (2, StepPhase.Acting)]);
        told.Items[0].Step.ToString().ShouldBe("click the search button");
    }

    [Fact]
    public async Task A_step_waiting_for_a_slow_app_tells_it_is_waiting()
    {
        ScreensShow(Screen("Shop", Window, SearchButton with { IsEnabled = false }), Screen("Shop", Window, SearchButton));
        var told = new ListProgress<StepActivity>();

        await NewRunner(TimeSpan.FromMilliseconds(500)).RunAsync(1234, "Shop", Steps("click the search button"), null, null, told, Ct);

        told.Items.Select(a => a.Phase).ShouldBe([StepPhase.Reading, StepPhase.Finding, StepPhase.Waiting, StepPhase.Acting]);
    }

    [Fact]
    public async Task Checking_the_title_asks_no_model_so_it_never_tells_finding()
    {
        var told = new ListProgress<StepActivity>();

        await NewRunner().RunAsync(1234, "Shop", Steps("expect title contains \"cats\""), null, null, told, Ct);

        told.Items.Select(a => a.Phase).ShouldBe([StepPhase.Reading]);
    }

    [Fact]
    public async Task Each_step_says_how_long_it_took()
    {
        var result = await NewRunner(pause: TimeSpan.FromMilliseconds(50)).RunAsync(1234, "Shop", Steps("press enter"), null, null, null, Ct);

        result.Steps[0].Duration.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task A_closed_app_throws_before_any_step()
    {
        _adapter.StartAsync(Arg.Any<AppTarget>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("The app is closed or has no window.")));

        await Should.ThrowAsync<InvalidOperationException>(() => RunAsync("press enter"));
        await _adapter.DidNotReceiveWithAnyArgs().PressKeyAsync(default!, Ct);
    }

    public void Dispose()
    {
        if (Directory.Exists(_output))
        {
            Directory.Delete(_output, recursive: true);
        }
    }

    private static List<StepCommand> Steps(params string[] lines) => [.. lines.Select(l => StepParser.Parse(l)!)];

    // No pause, and no waiting unless a test asks for it: each step reads once.
    private StepRunner NewRunner(TimeSpan? wait = null, TimeSpan? pause = null) =>
        new(() => _adapter, _llm, _secrets, _output, pause ?? TimeSpan.Zero, wait ?? TimeSpan.Zero);

    private Task<StepRunResult> RunAsync(params string[] lines) => RunAsync(lines, null);

    private Task<StepRunResult> RunAsync(string[] lines, Func<string, Task<bool>>? approveRisky) =>
        NewRunner().RunAsync(1234, "Shop", Steps(lines), approveRisky, null, null, Ct);

    // Waits up to 0.5 s, reading every 50 ms.
    private Task<StepRunResult> RunWaitingAsync(string line) =>
        NewRunner(TimeSpan.FromMilliseconds(500)).RunAsync(1234, "Shop", Steps(line), null, null, null, Ct);

    // Each reading shows the next screen; the last one stays.
    private void ScreensShow(params ScreenState[] screens) =>
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(screens[0], screens[1..]);

    private static ScreenState Screen(string title, params UiElement[] elements) => new(title, elements, ReadOnlyMemory<byte>.Empty, []);

    private Task<AgentAction> ModelWasAsked(int times) =>
        _llm.Received(times).DecideNextActionAsync(Arg.Any<TestTask>(), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>());

    // "More" (5) opens a menu with "Delete Note" (6).
    private void MenuOpens(Func<bool> isOpen, Action<bool> open)
    {
        var more = new UiElement(5, "SplitButton", "More", new ScreenRect(400, 110, 20, 20), true, "more");
        var item = new UiElement(6, "MenuItem", "Delete Note", new ScreenRect(400, 200, 80, 20), true, "deleteNote");
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(_ => isOpen() ? Screen("Notes", Window, more, item) : Screen("Notes", Window, more));
        _adapter.ClickAsync(5, Arg.Any<CancellationToken>()).Returns(_ => { open(true); return Task.CompletedTask; });
        ModelPoints("the more button", 5);
        ModelPoints("the delete note", 6);
    }

    private void ModelPointsWhenShown(string description, UiElement element) =>
        _llm.DecideNextActionAsync(Arg.Is<TestTask>(t => t.Description == $"Click {description}"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<ScreenState>(1).Elements.Contains(element)
                ? new AgentAction(AgentActionType.Click, element.Id, null, "Found it")
                : new AgentAction(AgentActionType.Fail, null, null, "Gone"));

    private void ModelPoints(string description, int id) => ModelAnswers(description, new AgentAction(AgentActionType.Click, id, null, "Found it"));

    private void ModelAnswers(string description, AgentAction action) =>
        _llm.DecideNextActionAsync(Arg.Is<TestTask>(t => t.Description == $"Click {description}"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(action);

    private void ScreenShows(params UiElement[] elements)
    {
        using var bitmap = new SKBitmap(400, 300);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(new ScreenState("Shop - cats", elements, png.ToArray(), []));
    }

    // A page taller than the window (300), with a toolbar that stays, a link every 50 pixels, two links that can be mixed up, and perhaps the cat.
    private sealed class Page
    {
        private int _offset;

        public Page(int pageHeight, int pixelsPerNotch, int? catAt, IAppAdapter adapter, ILlmClient llm)
        {
            adapter.ScrollAsync(Arg.Is(0), Arg.Any<int>(), Arg.Is(0), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Notches.Add(call.ArgAt<int>(1));
                _offset = Math.Clamp(_offset + (call.ArgAt<int>(1) * pixelsPerNotch), 0, pageHeight - Window.Bounds.Height);
                return Task.CompletedTask;
            });
            adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                List<UiElement> elements = [Window, Link(1, "Toolbar", 0)];
                for (var y = 0; y < pageHeight; y += 50)
                {
                    Add(elements, y % 200 == 0 ? "edit" : $"Link {y}", y - _offset);
                }

                if (catAt is { } at)
                {
                    Add(elements, "Orange cat", at - _offset);
                }

                return new ScreenState("Shop - cats", elements, ReadOnlyMemory<byte>.Empty, []);
            });
            llm.DecideNextActionAsync(
                    Arg.Is<TestTask>(t => t.Description == "Click the orange cat"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<ScreenState>(1).Elements.FirstOrDefault(e => e.Name == "Orange cat") is { } cat
                    ? new AgentAction(AgentActionType.Click, cat.Id, null, "Found it")
                    : new AgentAction(AgentActionType.Fail, null, null, "No cat"));
        }

        public List<int> Notches { get; } = [];

        public int Scrolls => Notches.Count;

        private static void Add(List<UiElement> elements, string name, int y)
        {
            if (y is >= 0 and < 300)
            {
                elements.Add(Link(elements.Count, name, y));
            }
        }

        private static UiElement Link(int id, string name, int y) =>
            new(id, "Hyperlink", name, new ScreenRect(110, 100 + y, 200, 20), true, $"Hyperlink:{name}");
    }

    private sealed class ListProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public void Report(T value) => Items.Add(value);
    }
}
