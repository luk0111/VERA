using Microsoft.Extensions.Time.Testing;

using Vera.Input.Injection;
using Vera.Input.Options;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace Vera.Input.Tests.Injection;

public sealed class KeyboardDriverTests
{
    private static readonly DelayOptions NoDelays = new() { BetweenKeys = 0, AfterType = 0 };

    private readonly RecordingSink _sink = new();

    [Fact]
    public async Task Letters_are_sent_as_unicode_and_newline_presses_enter()
    {
        await Driver(NoDelays).TypeAsync("aă\n", TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([
            InputEvent.CharDown('a'), InputEvent.CharUp('a'),
            InputEvent.CharDown('ă'), InputEvent.CharUp('ă'),
            InputEvent.KeyDown(KeyboardDriver.EnterScanCode), InputEvent.KeyUp(KeyboardDriver.EnterScanCode),
        ]);
    }

    [Fact]
    public async Task Tab_presses_tab_and_carriage_return_is_skipped()
    {
        await Driver(NoDelays).TypeAsync("\t\r\n", TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([
            InputEvent.KeyDown(KeyboardDriver.TabScanCode), InputEvent.KeyUp(KeyboardDriver.TabScanCode),
            InputEvent.KeyDown(KeyboardDriver.EnterScanCode), InputEvent.KeyUp(KeyboardDriver.EnterScanCode),
        ]);
    }

    [Fact]
    public void An_emoji_is_one_keystroke_with_both_halves()
    {
        var keystrokes = KeyboardDriver.Keystrokes("😀");

        keystrokes.Count.ShouldBe(1);
        keystrokes[0].ShouldBe([
            InputEvent.CharDown('\uD83D'), InputEvent.CharUp('\uD83D'),
            InputEvent.CharDown('\uDE00'), InputEvent.CharUp('\uDE00'),
        ]);
    }

    [Fact]
    public async Task Empty_text_sends_nothing_and_does_not_wait()
    {
        var typing = Driver(NoDelays with { AfterType = 200 }, new FakeTimeProvider()).TypeAsync("", TestContext.Current.CancellationToken);

        typing.IsCompleted.ShouldBeTrue();
        await typing;
        _sink.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a\bc")]
    [InlineData("a\0")]
    [InlineData("\u007F")]
    public async Task Other_control_characters_throw_and_type_nothing(string text)
    {
        await Should.ThrowAsync<ArgumentException>(() => Driver(NoDelays).TypeAsync(text, TestContext.Current.CancellationToken));

        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public void A_lone_surrogate_throws()
    {
        // Built here, not in [InlineData]: attributes can't hold broken UTF-16, it turns into U+FFFD.
        Should.Throw<ArgumentException>(() => KeyboardDriver.Keystrokes("a" + '\uD83D'));
        Should.Throw<ArgumentException>(() => KeyboardDriver.Keystrokes('\uDE00' + "a"));
    }

    [Fact]
    public async Task Characters_are_spaced_by_BetweenKeys()
    {
        var time = new FakeTimeProvider();
        var typing = Driver(NoDelays with { BetweenKeys = 10 }, time).TypeAsync("ab", TestContext.Current.CancellationToken);

        _sink.Events.Count.ShouldBe(2);

        time.Advance(TimeSpan.FromMilliseconds(10));
        await typing;
        _sink.Events.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Cancelling_halfway_stops_between_characters_with_no_key_left_down()
    {
        using var cts = new CancellationTokenSource();
        var typing = Driver(NoDelays with { BetweenKeys = 10 }, new FakeTimeProvider()).TypeAsync("abc", cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(typing);
        _sink.Events.ShouldBe([InputEvent.CharDown('a'), InputEvent.CharUp('a')]);
    }

    [Fact]
    public async Task An_already_cancelled_token_sends_nothing()
    {
        await Should.ThrowAsync<OperationCanceledException>(
            () => Driver(NoDelays).TypeAsync("abc", new CancellationToken(canceled: true)));

        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_combo_presses_modifiers_first_and_releases_them_in_reverse_one_event_at_a_time()
    {
        await Driver(NoDelays).PressAsync(KeyCombo.Parse("ctrl+shift+s"), 1, TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([
            InputEvent.KeyDown(0x1D), InputEvent.KeyDown(0x2A), InputEvent.KeyDown(0x1F),
            InputEvent.KeyUp(0x1F), InputEvent.KeyUp(0x2A), InputEvent.KeyUp(0x1D),
        ]);
        _sink.Batches.Count.ShouldBe(6);
    }

    [Fact]
    public async Task An_extended_key_keeps_its_flag_down_and_up()
    {
        await Driver(NoDelays).PressAsync(KeyCombo.Parse("shift+left"), 1, TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([
            InputEvent.KeyDown(0x2A), InputEvent.KeyDown(0x4B, extended: true),
            InputEvent.KeyUp(0x4B, extended: true), InputEvent.KeyUp(0x2A),
        ]);
    }

    [Fact]
    public async Task Combo_events_are_spaced_by_BetweenKeys()
    {
        var time = new FakeTimeProvider();
        var pressing = Driver(NoDelays with { BetweenKeys = 10 }, time).PressAsync(KeyCombo.Parse("alt+f4"), 1, TestContext.Current.CancellationToken);

        _sink.Events.ShouldBe([InputEvent.KeyDown(0x38)]);

        for (var sent = 2; sent <= 4; sent++)
        {
            time.Advance(TimeSpan.FromMilliseconds(10));
            _sink.Events.Count.ShouldBe(sent);
        }

        await pressing;
        _sink.Events[^1].ShouldBe(InputEvent.KeyUp(0x38));
    }

    [Fact]
    public async Task Repeat_presses_the_combo_again()
    {
        await Driver(NoDelays).PressAsync(KeyCombo.Parse("down"), 3, TestContext.Current.CancellationToken);

        _sink.Events.Count.ShouldBe(6);
        _sink.Events.ShouldAllBe(e => e.ScanCode == 0x50 && e.Extended);
    }

    [Fact]
    public async Task Cancelling_halfway_releases_the_keys_already_down()
    {
        using var cts = new CancellationTokenSource();
        var time = new FakeTimeProvider();
        var pressing = Driver(NoDelays with { BetweenKeys = 10 }, time).PressAsync(KeyCombo.Parse("ctrl+shift+z"), 1, cts.Token);
        time.Advance(TimeSpan.FromMilliseconds(10));

        await cts.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(pressing);
        _sink.Events.ShouldBe([
            InputEvent.KeyDown(0x1D), InputEvent.KeyDown(0x2A),
            InputEvent.KeyUp(0x2A), InputEvent.KeyUp(0x1D),
        ]);
    }

    [Fact]
    public async Task When_windows_refuses_a_key_the_ones_already_down_are_released()
    {
        var sink = new RefusingSink(refuseAt: 2);
        var driver = new KeyboardDriver(sink, MsOptions.Create(new VeraInputOptions { Delays = NoDelays }), TimeProvider.System);

        await Should.ThrowAsync<InputRejectedException>(
            () => driver.PressAsync(KeyCombo.Parse("ctrl+alt+t"), 1, TestContext.Current.CancellationToken));

        sink.Events.ShouldBe([
            InputEvent.KeyDown(0x1D), InputEvent.KeyDown(0x38),
            InputEvent.KeyUp(0x38), InputEvent.KeyUp(0x1D),
        ]);
    }

    private KeyboardDriver Driver(DelayOptions delays, TimeProvider? time = null) =>
        new(_sink, MsOptions.Create(new VeraInputOptions { Delays = delays }), time ?? TimeProvider.System);

    /// <summary>Records events, but throws on the call with this index, like Windows refusing input.</summary>
    private sealed class RefusingSink(int refuseAt) : IInputSink
    {
        private int _calls;

        public List<InputEvent> Events { get; } = [];

        public void Send(params IReadOnlyList<InputEvent> events)
        {
            if (_calls++ == refuseAt)
            {
                throw new InputRejectedException("refused");
            }

            Events.AddRange(events);
        }
    }
}
