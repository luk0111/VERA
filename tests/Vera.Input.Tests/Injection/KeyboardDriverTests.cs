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

    private KeyboardDriver Driver(DelayOptions delays, TimeProvider? time = null) =>
        new(_sink, MsOptions.Create(new VeraInputOptions { Delays = delays }), time ?? TimeProvider.System);
}
