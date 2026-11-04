using Microsoft.Extensions.Time.Testing;

using Vera.Input.Coordinates;
using Vera.Input.Injection;
using Vera.Input.Options;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace Vera.Input.Tests.Injection;

public sealed class MouseDriverTests
{
    private static readonly NormalizedPoint Point = new(32768, 32768);
    private static readonly DelayOptions NoDelays = new() { AfterMove = 0, MouseDownUp = 0, BetweenClicks = 0, AfterClick = 0 };

    private readonly RecordingSink _sink = new();

    [Fact]
    public async Task Move_sends_one_move()
    {
        await Driver(NoDelays).MoveAsync(Point, TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([InputEvent.MouseMove(Point)]);
    }

    [Fact]
    public async Task A_click_moves_then_presses_and_releases()
    {
        await Driver(NoDelays).ClickAsync(Point, MouseButton.Left, 1, TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([
            InputEvent.MouseMove(Point),
            InputEvent.MouseDown(MouseButton.Left),
            InputEvent.MouseUp(MouseButton.Left),
        ]);
    }

    [Fact]
    public async Task A_double_click_presses_twice_after_one_move()
    {
        await Driver(NoDelays).ClickAsync(Point, MouseButton.Left, 2, TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([
            InputEvent.MouseMove(Point),
            InputEvent.MouseDown(MouseButton.Left),
            InputEvent.MouseUp(MouseButton.Left),
            InputEvent.MouseDown(MouseButton.Left),
            InputEvent.MouseUp(MouseButton.Left),
        ]);
    }

    [Fact]
    public async Task A_right_click_uses_the_right_button()
    {
        await Driver(NoDelays).ClickAsync(Point, MouseButton.Right, 1, TestContext.Current.CancellationToken);

        _sink.Events.Skip(1).ShouldBe([InputEvent.MouseDown(MouseButton.Right), InputEvent.MouseUp(MouseButton.Right)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Clicks_outside_1_to_3_throw_and_send_nothing(int clicks)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => Driver(NoDelays).ClickAsync(Point, MouseButton.Left, clicks, TestContext.Current.CancellationToken));

        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_button_is_pressed_only_after_AfterMove()
    {
        var time = new FakeTimeProvider();
        var click = Driver(NoDelays with { AfterMove = 50 }, time).ClickAsync(Point, MouseButton.Left, 1, TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([InputEvent.MouseMove(Point)]);

        time.Advance(TimeSpan.FromMilliseconds(50));
        await click;
        _sink.Events.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Cancelling_while_the_button_is_down_still_releases_it()
    {
        using var cts = new CancellationTokenSource();
        var click = Driver(NoDelays with { MouseDownUp = 50 }, new FakeTimeProvider()).ClickAsync(Point, MouseButton.Left, 1, cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(click);
        _sink.Events.ShouldBe([
            InputEvent.MouseMove(Point),
            InputEvent.MouseDown(MouseButton.Left),
            InputEvent.MouseUp(MouseButton.Left),
        ]);
    }

    [Fact]
    public async Task An_already_cancelled_token_sends_nothing()
    {
        await Should.ThrowAsync<OperationCanceledException>(
            () => Driver(NoDelays).ClickAsync(Point, MouseButton.Left, 1, new CancellationToken(canceled: true)));

        _sink.Events.ShouldBeEmpty();
    }

    private MouseDriver Driver(DelayOptions delays, TimeProvider? time = null) =>
        new(_sink, MsOptions.Create(new VeraInputOptions { Delays = delays }), time ?? TimeProvider.System);
}
