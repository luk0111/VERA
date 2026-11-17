using Vera.Core;

namespace Vera.Application.Tests;

public sealed class ChatAppContextReaderTests
{
    private static readonly UiElement Window = new(0, "Window", "Fresh title", new ScreenRect(0, 0, 200, 100), true, "window");
    private static readonly UiElement Save = new(1, "Button", "Save", new ScreenRect(10, 10, 50, 20), true, "save");

    private readonly IAppAdapter _adapter = Substitute.For<IAppAdapter>();
    private readonly IRunningAppSource _runningApps = Substitute.For<IRunningAppSource>();

    public ChatAppContextReaderTests()
    {
        _runningApps.List().Returns([]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_window_is_read_by_process_id()
    {
        ScreenShows("Fresh title", Window, Save);

        var app = await NewReader().ReadAsync(1234, "Notepad", "Picker title", Ct);

        await _adapter.Received(1).StartAsync(Arg.Is<AppTarget>(t => t.ProcessId == 1234), Arg.Any<CancellationToken>());
        app.ProcessId.ShouldBe(1234);
        app.Name.ShouldBe("Notepad");
        app.Elements.ShouldBe([Window, Save]);
        app.ElementCount.ShouldBe(1);
        app.Problem.ShouldBeNull();
    }

    [Fact]
    public async Task The_title_read_now_wins_over_the_one_from_the_picker()
    {
        ScreenShows("Fresh title", Window);

        var app = await NewReader().ReadAsync(1234, "Notepad", "Picker title", Ct);

        app.WindowTitle.ShouldBe("Fresh title");
    }

    [Fact]
    public async Task Without_a_title_now_the_one_from_the_picker_stays()
    {
        ScreenShows("", Window);

        var app = await NewReader().ReadAsync(1234, "Notepad", "Picker title", Ct);

        app.WindowTitle.ShouldBe("Picker title");
    }

    [Fact]
    public async Task A_window_that_cannot_be_read_gives_no_elements_and_the_reason()
    {
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>())
            .Returns<ScreenState>(_ => throw new InvalidOperationException("The app's window is minimized."));

        var app = await NewReader().ReadAsync(1234, "Notepad", "Picker title", Ct);

        app.Elements.ShouldBeEmpty();
        app.WindowTitle.ShouldBe("Picker title");
        app.Problem.ShouldBe("The app's window is minimized.");
    }

    [Fact]
    public async Task The_adapter_is_disposed()
    {
        ScreenShows("Fresh title", Window);

        await NewReader().ReadAsync(1234, "Notepad", "Picker title", Ct);

        await _adapter.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task The_listed_title_is_kept_with_the_read()
    {
        ScreenShows("Gmail - Opera", Window);
        ListShows(1234, "Gmail");

        var app = await NewReader().ReadAsync(1234, "Opera", "Gmail", Ct);

        app.ListedTitle.ShouldBe("Gmail");
    }

    [Fact]
    public async Task Another_listed_title_means_the_window_changed()
    {
        ScreenShows("Gmail - Opera", Window);
        ListShows(1234, "Gmail");
        var reader = NewReader();
        var app = await reader.ReadAsync(1234, "Opera", "Gmail", Ct);

        ListShows(1234, "YouTube");

        reader.TitleChanged(app).ShouldBeTrue();
    }

    [Fact]
    public async Task The_same_listed_title_means_no_change()
    {
        ScreenShows("Gmail - Opera", Window);
        ListShows(1234, "Gmail");
        var reader = NewReader();

        var app = await reader.ReadAsync(1234, "Opera", "Gmail", Ct);

        reader.TitleChanged(app).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1234, "")]
    [InlineData(9999, "YouTube")]
    public async Task No_title_now_or_a_gone_app_is_not_a_change(int listedProcessId, string listedTitle)
    {
        ScreenShows("Gmail - Opera", Window);
        ListShows(1234, "Gmail");
        var reader = NewReader();
        var app = await reader.ReadAsync(1234, "Opera", "Gmail", Ct);

        ListShows(listedProcessId, listedTitle);

        reader.TitleChanged(app).ShouldBeFalse();
    }

    [Fact]
    public async Task A_title_that_appears_after_a_read_without_one_is_a_change()
    {
        ScreenShows("", Window);
        var reader = NewReader();
        var app = await reader.ReadAsync(1234, "Opera", "", Ct);

        ListShows(1234, "YouTube");

        reader.TitleChanged(app).ShouldBeTrue();
    }

    private void ListShows(int processId, string title) =>
        _runningApps.List().Returns([new RunningAppInfo(processId, "Opera", title)]);

    private void ScreenShows(string title, params UiElement[] elements) =>
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(new ScreenState(title, elements, ReadOnlyMemory<byte>.Empty, []));

    private ChatAppContextReader NewReader() => new(() => _adapter, _runningApps);
}
