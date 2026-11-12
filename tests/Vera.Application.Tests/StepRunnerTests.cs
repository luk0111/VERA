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
    public async Task A_disabled_element_is_not_clicked()
    {
        ScreenShows(Window, SearchButton with { IsEnabled = false });

        var result = await RunAsync("click the search button");

        result.Steps[0].Message.ShouldBe("button \"Search\" is disabled.");
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
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
        UiElement? asked = null;

        var result = await RunAsync(["click the delete button"], e => { asked = e; return Task.FromResult(false); });

        asked.ShouldBe(Delete);
        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps[0].Message.ShouldBe("Stopped: clicking button \"Delete account\" wasn't approved.");
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
        result.Steps[0].Message.ShouldBe("Unknown key \"entr\".");
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
        var reported = new ListProgress();

        await NewRunner().RunAsync(1234, "Shop", Steps("click the search button", "press enter"), null, reported, Ct);

        reported.Items.Select(s => s.Number).ShouldBe([1, 2]);
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

    private StepRunner NewRunner() => new(() => _adapter, _llm, _secrets, _output, TimeSpan.Zero);

    private Task<StepRunResult> RunAsync(params string[] lines) => RunAsync(lines, null);

    private Task<StepRunResult> RunAsync(string[] lines, Func<UiElement, Task<bool>>? approveRisky) =>
        NewRunner().RunAsync(1234, "Shop", Steps(lines), approveRisky, null, Ct);

    private void ModelPoints(string description, int id) =>
        _llm.DecideNextActionAsync(Arg.Is<TestTask>(t => t.Description == $"Click {description}"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentAction(AgentActionType.Click, id, null, "Found it"));

    private void ScreenShows(params UiElement[] elements)
    {
        using var bitmap = new SKBitmap(400, 300);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(new ScreenState("Shop - cats", elements, png.ToArray(), []));
    }

    private sealed class ListProgress : IProgress<StepResult>
    {
        public List<StepResult> Items { get; } = [];

        public void Report(StepResult value) => Items.Add(value);
    }
}
