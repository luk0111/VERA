using SkiaSharp;

using Vera.Core;

namespace Vera.Application.Tests;

public sealed class FindElementServiceTests : IDisposable
{
    private static readonly UiElement Window = new(0, "Window", "App", new ScreenRect(100, 100, 200, 100), true, "window");
    private static readonly UiElement LogIn = new(7, "Button", "Log in", new ScreenRect(120, 120, 80, 30), true, "login");

    private readonly string _output = Path.Combine(Path.GetTempPath(), "vera-find-tests", Guid.NewGuid().ToString("N"));
    private readonly IAppAdapter _adapter = Substitute.For<IAppAdapter>();
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();

    public FindElementServiceTests()
    {
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(Screen(Window, LogIn));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_click_answer_is_found_and_saved()
    {
        ModelAnswers(new AgentAction(AgentActionType.Click, 7, null, "It says Log in"));

        var result = await NewService().FindAsync(1234, "App", "Log in button", Ct);

        result.Found.ShouldBeTrue();
        result.Element.ShouldNotBeNull().Id.ShouldBe(7);
        File.Exists(result.ImagePath).ShouldBeTrue();
    }

    // #58: VERA's window comes back right after the reading, before the model is asked, and again after an action.
    [Fact]
    public async Task VERAs_window_comes_back_after_reading_and_after_acting()
    {
        var window = new FrontWindow(77);
        var front = Substitute.For<IFrontWindow>();
        front.Current().Returns(window);
        var order = new List<string>();
        front.BringBackAsync(window, Arg.Any<CancellationToken>()).Returns(_ => { order.Add("front"); return true; });
        _llm.DecideNextActionAsync(Arg.Any<TestTask>(), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("model"); return new AgentAction(AgentActionType.Click, 7, null, ""); });
        var service = new FindElementService(() => _adapter, _llm, _output, front);

        var found = await service.FindAsync(1234, "App", "Log in button", Ct);
        await service.ClickAsync(1234, "App", found.Element!, Ct);

        order.ShouldBe(["front", "model", "front"]);
    }

    [Fact]
    public async Task A_secret_typed_by_a_run_is_hidden_from_the_model()
    {
        var trail = new SecretTrail();
        trail.Typed(1234, LogIn, ["s3cret"]);
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(Screen(Window, LogIn with { Name = "Log in as s3cret" }));
        ModelAnswers(new AgentAction(AgentActionType.Click, 7, null, "It says Log in"));

        var result = await new FindElementService(() => _adapter, _llm, _output, trail: trail).FindAsync(1234, "App", "Log in button", Ct);

        result.Element.ShouldNotBeNull().Name.ShouldBe("Log in as ***");
        await _llm.Received(1).DecideNextActionAsync(
            Arg.Any<TestTask>(), Arg.Is<ScreenState>(s => s.Elements.All(e => !e.Name.Contains("s3cret"))), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_fail_answer_is_not_found_with_the_models_reason()
    {
        ModelAnswers(new AgentAction(AgentActionType.Fail, null, null, "No such button"));

        var result = await NewService().FindAsync(1234, "App", "Log in button", Ct);

        result.Found.ShouldBeFalse();
        result.Element.ShouldBeNull();
        result.Reason.ShouldBe("No such button");
        File.Exists(result.ImagePath).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Log in", true)]
    [InlineData("Cancel", false)]
    public async Task It_says_when_another_element_has_the_same_name(string otherName, bool expected)
    {
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(Screen(Window, LogIn, LogIn with { Id = 8, Name = otherName }));
        ModelAnswers(new AgentAction(AgentActionType.Click, 7, null, ""));

        var result = await NewService().FindAsync(1234, "App", "Log in button", Ct);

        result.SameNameElsewhere.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Delete file", true)]
    [InlineData("Log in", false)]
    public void Risky_names_are_recognised(string name, bool expected)
    {
        FindElementService.IsRisky(LogIn with { Name = name }).ShouldBe(expected);
    }

    [Fact]
    public async Task The_app_is_attached_by_process_id()
    {
        ModelAnswers(new AgentAction(AgentActionType.Fail, null, null, ""));

        await NewService().FindAsync(1234, "App", "Log in button", Ct);

        await _adapter.Received(1).StartAsync(Arg.Is<AppTarget>(t => t.ProcessId == 1234 && t.Platform == AppPlatform.Desktop), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_model_is_asked_to_click_what_the_tester_typed()
    {
        ModelAnswers(new AgentAction(AgentActionType.Fail, null, null, ""));

        await NewService().FindAsync(1234, "App", "Log in button", Ct);

        await _llm.Received(1).DecideNextActionAsync(
            Arg.Is<TestTask>(t => t.Description == "Click the Log in button"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_expected_result_is_a_click_so_seeing_the_element_is_not_already_done()
    {
        ModelAnswers(new AgentAction(AgentActionType.Fail, null, null, ""));

        await NewService().FindAsync(1234, "App", "Log in button", Ct);

        await _llm.Received(1).DecideNextActionAsync(
            Arg.Is<TestTask>(t => t.ExpectedResult == "The Log in button has been clicked"), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_window_without_elements_is_not_found_without_asking_the_model()
    {
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(Screen(Window));

        var result = await NewService().FindAsync(1234, "App", "Log in button", Ct);

        result.Found.ShouldBeFalse();
        result.Reason.ShouldBe("VERA can't see any buttons or fields in this window. Is it minimized?");
        await _llm.DidNotReceiveWithAnyArgs().DecideNextActionAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task A_closed_app_throws_and_saves_nothing()
    {
        _adapter.StartAsync(Arg.Any<AppTarget>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("The app is closed or has no window.")));

        await Should.ThrowAsync<InvalidOperationException>(() => NewService().FindAsync(1234, "App", "Log in button", Ct));

        Directory.Exists(_output).ShouldBeFalse();
    }

    [Fact]
    public async Task Clicking_reads_the_window_again_and_clicks_the_same_element_by_its_new_id()
    {
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(Screen(Window, LogIn with { Id = 3 }));

        var clicked = await NewService().ClickAsync(1234, "App", LogIn, Ct);

        clicked.Id.ShouldBe(3);
        await _adapter.Received(1).StartAsync(Arg.Is<AppTarget>(t => t.ProcessId == 1234), Arg.Any<CancellationToken>());
        await _adapter.Received(1).ClickAsync(3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_the_same_element_appears_twice_the_one_nearest_its_old_place_is_clicked()
    {
        var far = LogIn with { Id = 3, Bounds = new ScreenRect(400, 400, 80, 30) };
        var near = LogIn with { Id = 4, Bounds = new ScreenRect(125, 120, 80, 30) };
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(Screen(Window, far, near));

        await NewService().ClickAsync(1234, "App", LogIn, Ct);

        await _adapter.Received(1).ClickAsync(4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_element_with_another_name_role_or_locator_is_not_the_same()
    {
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(Screen(
            Window, LogIn with { Name = "Log out" }, LogIn with { Role = "Link" }, LogIn with { Locator = "other" }));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => NewService().ClickAsync(1234, "App", LogIn, Ct));

        error.Message.ShouldBe("\"Log in\" is no longer on the screen.");
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
    }

    [Fact]
    public async Task Clicking_does_not_ask_the_model()
    {
        await NewService().ClickAsync(1234, "App", LogIn, Ct);

        await _llm.DidNotReceiveWithAnyArgs().DecideNextActionAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task A_click_the_app_refuses_passes_its_message_on()
    {
        _adapter.ClickAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("The app's window is minimized. Restore it and try again.")));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => NewService().ClickAsync(1234, "App", LogIn, Ct));

        error.Message.ShouldBe("The app's window is minimized. Restore it and try again.");
    }

    private static readonly UiElement Email = new(8, "Edit", "Email", new ScreenRect(120, 160, 120, 20), true, "email");

    [Fact]
    public async Task Typing_reads_the_window_again_and_types_into_the_same_field_by_its_new_id()
    {
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(Screen(Window, LogIn, Email with { Id = 5 }));

        var typed = await NewService().TypeAsync(1234, "App", Email, "ana@example.com", Ct);

        typed.Id.ShouldBe(5);
        await _adapter.Received(1).TypeAsync(5, "ana@example.com", Arg.Any<CancellationToken>());
        await _adapter.DidNotReceiveWithAnyArgs().ClickAsync(default, Ct);
    }

    [Fact]
    public async Task Typing_into_a_field_that_is_gone_throws_and_types_nothing()
    {
        var error = await Should.ThrowAsync<InvalidOperationException>(() => NewService().TypeAsync(1234, "App", Email, "x", Ct));

        error.Message.ShouldBe("\"Email\" is no longer on the screen.");
        await _adapter.DidNotReceiveWithAnyArgs().TypeAsync(default, default!, Ct);
    }

    [Fact]
    public void Only_text_fields_areas_and_combos_accept_text()
    {
        FindElementService.AcceptsText(Email).ShouldBeTrue();
        FindElementService.AcceptsText(Email with { Role = "Document" }).ShouldBeTrue();
        FindElementService.AcceptsText(Email with { Role = "ComboBox" }).ShouldBeTrue();
        FindElementService.AcceptsText(LogIn).ShouldBeFalse();
        FindElementService.AcceptsText(LogIn with { Role = "TabItem" }).ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_output))
        {
            Directory.Delete(_output, recursive: true);
        }
    }

    private FindElementService NewService() => new(() => _adapter, _llm, _output);

    private void ModelAnswers(AgentAction action) =>
        _llm.DecideNextActionAsync(Arg.Any<TestTask>(), Arg.Any<ScreenState>(), Arg.Any<IReadOnlyList<AgentStep>>(), Arg.Any<CancellationToken>()).Returns(action);

    private static ScreenState Screen(params UiElement[] elements)
    {
        using var bitmap = new SKBitmap(200, 100);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ScreenState("App", elements, png.ToArray(), []);
    }
}
