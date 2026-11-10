using Vera.Core;

namespace Vera.Application.Tests;

public sealed class ChatAppContextReaderTests
{
    private static readonly UiElement Window = new(0, "Window", "Fresh title", new ScreenRect(0, 0, 200, 100), true, "window");
    private static readonly UiElement Save = new(1, "Button", "Save", new ScreenRect(10, 10, 50, 20), true, "save");

    private readonly IAppAdapter _adapter = Substitute.For<IAppAdapter>();

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

    private void ScreenShows(string title, params UiElement[] elements) =>
        _adapter.GetScreenStateAsync(Arg.Any<CancellationToken>()).Returns(new ScreenState(title, elements, ReadOnlyMemory<byte>.Empty, []));

    private ChatAppContextReader NewReader() => new(() => _adapter);
}
