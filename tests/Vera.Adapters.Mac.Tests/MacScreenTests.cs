using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Vera.Input;

using static Vera.Adapters.Mac.MacInputEvent;

namespace Vera.Adapters.Mac.Tests;

public sealed class MacScreenTests
{
    private const ushort Command = 0x37;
    private const ushort Shift = 0x38;
    private const ushort S = 0x01;

    private readonly RecordingSink _sink = new();
    private readonly FakeCapture _capture = new();
    private bool _allowed = true;

    [Fact]
    public async Task A_click_lands_on_the_point_under_the_screenshot_pixel()
    {
        var screen = NewScreen();

        // A 300×200 point window at (100, 50), captured on a Retina screen: 600×400 pixels.
        await screen.ScreenshotAreaAsync(100, 50, 300, 200, TestContext.Current.CancellationToken);
        await screen.ClickAsync(200, 100, ct: TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([
            new MouseMove(200, 100),
            new MouseButtonEvent(200, 100, MouseButton.Left, Down: true, 1),
            new MouseButtonEvent(200, 100, MouseButton.Left, Down: false, 1),
        ]);
    }

    [Fact]
    public async Task A_double_click_counts_its_clicks_so_macos_sees_a_double_click()
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(0, 0, 300, 200, TestContext.Current.CancellationToken);

        await screen.ClickAsync(10, 10, MouseButton.Right, clicks: 2, TestContext.Current.CancellationToken);

        _sink.Events.OfType<MouseButtonEvent>().Select(e => (e.Button, e.Down, e.ClickState)).ShouldBe([
            (MouseButton.Right, true, 1), (MouseButton.Right, false, 1), (MouseButton.Right, true, 2), (MouseButton.Right, false, 2),
        ]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(600, 0)]
    [InlineData(0, 400)]
    public async Task Points_outside_the_screenshot_throw_and_nothing_moves(int x, int y)
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(0, 0, 300, 200, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => screen.ClickAsync(x, y, ct: TestContext.Current.CancellationToken));
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Clicking_before_any_screenshot_is_refused()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => NewScreen().ClickAsync(1, 1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_accessibility_permission_nothing_is_sent()
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(0, 0, 300, 200, TestContext.Current.CancellationToken);
        _allowed = false;

        var e = await Should.ThrowAsync<ScreenUnavailableException>(() => screen.ClickAsync(1, 1, ct: TestContext.Current.CancellationToken));
        e.Message.ShouldContain("Accessibility");
        await Should.ThrowAsync<ScreenUnavailableException>(() => screen.TypeTextAsync("a", ct: TestContext.Current.CancellationToken));
        await Should.ThrowAsync<ScreenUnavailableException>(() => screen.KeyAsync("cmd+s", ct: TestContext.Current.CancellationToken));
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Text_is_typed_one_character_at_a_time_with_return_and_tab_as_keys()
    {
        await NewScreen().TypeTextAsync("ă😀\r\n\t", ct: TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([
            new Text("ă", Down: true), new Text("ă", Down: false),
            new Text("😀", Down: true), new Text("😀", Down: false),
            new Key(MacKeyCombo.ReturnKey, Down: true, 0), new Key(MacKeyCombo.ReturnKey, Down: false, 0),
            new Key(MacKeyCombo.TabKey, Down: true, 0), new Key(MacKeyCombo.TabKey, Down: false, 0),
        ]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_with_a_bad_character_types_nothing(bool brokenEmoji)
    {
        // Built here: xUnit turns a lone surrogate in InlineData into "�" before the test sees it.
        var text = brokenEmoji ? "a" + '\ud83d' : "a\u0007b";

        await Should.ThrowAsync<ArgumentException>(() => NewScreen().TypeTextAsync(text, ct: TestContext.Current.CancellationToken));
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_combo_holds_its_modifiers_around_the_main_key()
    {
        await NewScreen().KeyAsync("ctrl+shift+s", ct: TestContext.Current.CancellationToken);

        const ulong both = MacKeyCombo.CommandFlag | MacKeyCombo.ShiftFlag;
        _sink.Events.ShouldBe([
            new Key(Command, Down: true, MacKeyCombo.CommandFlag),
            new Key(Shift, Down: true, both),
            new Key(S, Down: true, both),
            new Key(S, Down: false, both),
            new Key(Shift, Down: false, MacKeyCombo.CommandFlag),
            new Key(Command, Down: false, 0),
        ]);
    }

    [Fact]
    public async Task A_bad_combo_presses_nothing()
    {
        await Should.ThrowAsync<InvalidKeyComboException>(() => NewScreen().KeyAsync("cmd+hyper", ct: TestContext.Current.CancellationToken));
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_combo_stopped_halfway_releases_the_keys_still_down()
    {
        var time = new FakeTimeProvider();
        var screen = new MacScreen(_capture, _sink, () => true, MacScreen.MacDelays.None with { BetweenKeys = TimeSpan.FromMilliseconds(20) }, time, NullLogger.Instance);
        using var stop = new CancellationTokenSource();

        var press = screen.KeyAsync("cmd+s", ct: stop.Token);
        _sink.Events.Count.ShouldBe(1); // Command is down, waiting before S.
        await stop.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => press);
        _sink.Events.ShouldBe([new Key(Command, Down: true, MacKeyCombo.CommandFlag), new Key(Command, Down: false, 0)]);
    }

    [Fact]
    public async Task Scrolling_down_and_right_turns_the_mac_wheels_the_other_way()
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(100, 50, 300, 200, Ct);

        await screen.ScrollAsync(200, 100, deltaY: 2, deltaX: 1, Ct);

        _sink.Events.ShouldBe([new MouseMove(200, 100), new Scroll(-1, 0), new Scroll(-1, 0), new Scroll(0, -1)]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(51, 0)]
    [InlineData(0, -51)]
    public async Task A_scroll_of_nothing_or_too_much_throws_and_sends_nothing(int deltaY, int deltaX)
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(0, 0, 300, 200, Ct);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => screen.ScrollAsync(1, 1, deltaY, deltaX, Ct));
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_drag_presses_at_the_start_drags_and_releases_exactly_at_the_end()
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(100, 50, 300, 200, Ct);

        await screen.DragAsync(0, 0, 200, 100, MouseButton.Left, Ct);

        _sink.Events[0].ShouldBe(new MouseMove(100, 50));
        _sink.Events[1].ShouldBe(new MouseButtonEvent(100, 50, MouseButton.Left, Down: true, 1));
        var drags = _sink.Events.Skip(2).Take(MacScreen.GlideSteps).ToList();
        drags.ShouldAllBe(e => e is MouseDrag);
        drags[^1].ShouldBe(new MouseDrag(200, 100, MouseButton.Left));
        _sink.Events[^1].ShouldBe(new MouseButtonEvent(200, 100, MouseButton.Left, Down: false, 1));
        _sink.Events.Count.ShouldBe(MacScreen.GlideSteps + 3);
    }

    [Fact]
    public async Task A_drag_to_a_point_outside_the_screenshot_throws_and_sends_nothing()
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(0, 0, 300, 200, Ct);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => screen.DragAsync(0, 0, 600, 0, ct: Ct));
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_drag_stopped_halfway_releases_the_button_where_it_got_to()
    {
        var screen = new MacScreen(
            _capture, _sink, () => true, MacScreen.MacDelays.None with { BetweenMoveSteps = TimeSpan.FromMilliseconds(15) }, new FakeTimeProvider(), NullLogger.Instance);
        await screen.ScreenshotAreaAsync(0, 0, 300, 200, Ct);
        using var stop = new CancellationTokenSource();

        var drag = screen.DragAsync(0, 0, 300, 0, MouseButton.Left, stop.Token);
        var reached = _sink.Events.OfType<MouseDrag>().Single(); // The first step, then waiting.
        await stop.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => drag);
        _sink.Events[^1].ShouldBe(new MouseButtonEvent(reached.X, reached.Y, MouseButton.Left, Down: false, 1));
    }

    [Fact]
    public async Task A_hover_glides_from_the_pointer_to_the_point()
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(100, 50, 300, 200, Ct);
        _sink.Pointer = (400, 250);

        await screen.MoveAsync(0, 0, Ct);

        _sink.Events.Count.ShouldBe(MacScreen.GlideSteps);
        _sink.Events[0].ShouldBe(new MouseMove(380, 250 - (200.0 / MacScreen.GlideSteps)));
        _sink.Events[^1].ShouldBe(new MouseMove(100, 50));
    }

    [Fact]
    public async Task A_hover_jumps_when_macos_will_not_say_where_the_pointer_is()
    {
        var screen = NewScreen();
        await screen.ScreenshotAreaAsync(100, 50, 300, 200, Ct);
        _sink.Pointer = null;

        await screen.MoveAsync(0, 0, Ct);

        _sink.Events.ShouldAllBe(e => e == new MouseMove(100, 50));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MacScreen NewScreen() =>
        new(_capture, _sink, () => _allowed, MacScreen.MacDelays.None, TimeProvider.System, NullLogger.Instance);

    private sealed class RecordingSink : IMacInputSink
    {
        public List<MacInputEvent> Events { get; } = [];

        public (double X, double Y)? Pointer { get; set; } = (0, 0);

        public void Send(MacInputEvent e) => Events.Add(e);

        public (double X, double Y)? Cursor() => Pointer;
    }

    /// <summary>Pretends to be a Retina screen: two pixels per point.</summary>
    private sealed class FakeCapture : IScreenshotSource
    {
        public Task<ScreenFrame> ScreenshotAsync(double? scale = null, CancellationToken ct = default) =>
            Task.FromResult(Frame(1440, 900));

        public Task<ScreenFrame> ScreenshotAreaAsync(int left, int top, int width, int height, CancellationToken ct = default) =>
            Task.FromResult(Frame(width, height));

        private static ScreenFrame Frame(int width, int height) =>
            new(width * 2, height * 2, 2, width, height, [], DateTimeOffset.UnixEpoch, 1);
    }
}
