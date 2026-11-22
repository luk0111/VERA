using Microsoft.Extensions.Time.Testing;

using Vera.Input.Coordinates;
using Vera.Input.Injection;
using Vera.Input.Options;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace Vera.Input.Tests.Injection;

/// <summary>Milestone 7: scroll, drag and hover.</summary>
public sealed class MouseDriverScrollDragTests
{
    private static readonly NormalizedPoint From = new(1000, 2000);
    private static readonly NormalizedPoint To = new(31000, 17000);

    private static readonly DelayOptions NoDelays = new()
    {
        AfterMove = 0, MouseDownUp = 0, BetweenClicks = 0, AfterClick = 0, BetweenNotches = 0, BetweenMoveSteps = 0, HoverDwell = 0,
    };

    private readonly RecordingSink _sink = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scrolling_down_moves_there_then_turns_the_wheel_towards_the_user_once_per_notch()
    {
        await Driver(NoDelays).ScrollAsync(From, deltaY: 3, deltaX: 0, Ct);

        _sink.Events.ShouldBe([
            InputEvent.MouseMove(From),
            InputEvent.MouseWheel(-120, horizontal: false),
            InputEvent.MouseWheel(-120, horizontal: false),
            InputEvent.MouseWheel(-120, horizontal: false),
        ]);
    }

    [Fact]
    public async Task Scrolling_up_and_right_use_windows_positive_directions()
    {
        await Driver(NoDelays).ScrollAsync(From, deltaY: -1, deltaX: 2, Ct);

        _sink.Events.Skip(1).ShouldBe([
            InputEvent.MouseWheel(120, horizontal: false),
            InputEvent.MouseWheel(120, horizontal: true),
            InputEvent.MouseWheel(120, horizontal: true),
        ]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(51, 0)]
    [InlineData(0, -51)]
    public async Task A_scroll_of_nothing_or_too_much_throws_and_sends_nothing(int deltaY, int deltaX)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Driver(NoDelays).ScrollAsync(From, deltaY, deltaX, Ct));
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Notches_are_BetweenNotches_apart()
    {
        var time = new FakeTimeProvider();
        var scroll = Driver(NoDelays with { BetweenNotches = 50 }, time).ScrollAsync(From, 2, 0, Ct);
        _sink.Events.Count.ShouldBe(2); // The move and the first notch.

        time.Advance(TimeSpan.FromMilliseconds(50));
        await scroll;
        _sink.Events.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_drag_presses_at_the_start_glides_and_releases_exactly_at_the_end()
    {
        await Driver(NoDelays).DragAsync(From, To, MouseButton.Left, Ct);

        _sink.Events[0].ShouldBe(InputEvent.MouseMove(From));
        _sink.Events[1].ShouldBe(InputEvent.MouseDown(MouseButton.Left));
        var moves = _sink.Events.Skip(2).Take(MouseDriver.GlideSteps).ToList();
        moves.ShouldAllBe(e => e.Kind == InputEventKind.MouseMove);
        moves[^1].ShouldBe(InputEvent.MouseMove(To));
        _sink.Events[^1].ShouldBe(InputEvent.MouseUp(MouseButton.Left));
        _sink.Events.Count.ShouldBe(MouseDriver.GlideSteps + 3);
    }

    [Fact]
    public void A_glide_is_evenly_spaced_and_ends_on_the_point()
    {
        var points = MouseDriver.Glide(new NormalizedPoint(0, 0), new NormalizedPoint(1500, -300)).ToList();

        points.Count.ShouldBe(MouseDriver.GlideSteps);
        points[0].ShouldBe(new NormalizedPoint(100, -20));
        points[^1].ShouldBe(new NormalizedPoint(1500, -300));
    }

    [Fact]
    public async Task A_drag_stopped_halfway_still_releases_the_button()
    {
        var time = new FakeTimeProvider();
        using var stop = new CancellationTokenSource();
        var drag = Driver(NoDelays with { BetweenMoveSteps = 15 }, time).DragAsync(From, To, MouseButton.Right, stop.Token);

        _sink.Events.Last().Kind.ShouldBe(InputEventKind.MouseMove); // Down, first glide step, then waiting.
        await stop.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => drag);
        _sink.Events[^1].ShouldBe(InputEvent.MouseUp(MouseButton.Right));
    }

    [Fact]
    public async Task A_hover_glides_from_the_pointer_and_then_waits_HoverDwell()
    {
        var time = new FakeTimeProvider();
        var hover = Driver(NoDelays with { HoverDwell = 500 }, time).HoverAsync(From, To, Ct);

        _sink.Events.Count.ShouldBe(MouseDriver.GlideSteps);
        _sink.Events[^1].ShouldBe(InputEvent.MouseMove(To));
        hover.IsCompleted.ShouldBeFalse();

        time.Advance(TimeSpan.FromMilliseconds(500));
        await hover;
    }

    private MouseDriver Driver(DelayOptions delays, TimeProvider? time = null) =>
        new(_sink, MsOptions.Create(new VeraInputOptions { Delays = delays }), time ?? TimeProvider.System);
}
