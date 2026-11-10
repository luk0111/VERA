using Microsoft.Extensions.Options;

using Vera.Input.Options;

namespace Vera.Input.Injection;

/// <summary>
/// Types text and presses key combos through <see cref="IInputSink"/>, with the pauses from <see cref="DelayOptions"/> in between.
/// Characters are sent as Unicode events, so they arrive as written whatever the keyboard layout (ă, ș, €, emoji).
/// </summary>
internal sealed class KeyboardDriver(IInputSink sink, IOptions<VeraInputOptions> options, TimeProvider time)
{
    internal const ushort EnterScanCode = 0x1C;
    internal const ushort TabScanCode = 0x0F;

    /// <summary>
    /// Types <paramref name="text"/> one character at a time. <c>\n</c> presses Enter, <c>\t</c> presses Tab and
    /// <c>\r</c> is skipped (so <c>\r\n</c> is one Enter).
    /// </summary>
    /// <remarks>
    /// Each character goes down and up in one <c>SendInput</c> call, so stopping halfway never leaves a key pressed.
    /// </remarks>
    /// <exception cref="ArgumentException">The text has another control character, or a broken emoji (lone surrogate). Nothing is typed.</exception>
    public async Task TypeAsync(string text, CancellationToken ct)
    {
        var keystrokes = Keystrokes(text);
        if (keystrokes.Count == 0)
        {
            return;
        }

        var delays = options.Value.Delays;
        for (var i = 0; i < keystrokes.Count; i++)
        {
            if (i > 0)
            {
                await Delay(delays.BetweenKeys, ct);
            }

            ct.ThrowIfCancellationRequested();
            sink.Send(keystrokes[i]);
        }

        await Delay(delays.AfterType, ct);
    }

    /// <summary>
    /// Presses <paramref name="combo"/> <paramref name="repeat"/> times: modifiers down, then the main key down and
    /// up, then the modifiers up in reverse, with <c>BetweenKeys</c> between every event.
    /// </summary>
    /// <remarks>
    /// The pauses are needed: the Windows 11 Notepad ignores Alt+F4 when all four events arrive at once. If the press
    /// stops halfway (cancelled, or Windows refuses an event), every key still down is released, so no Ctrl or Alt
    /// stays pressed.
    /// </remarks>
    public async Task PressAsync(KeyCombo combo, int repeat, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(combo);
        ArgumentOutOfRangeException.ThrowIfLessThan(repeat, 1);

        var delays = options.Value.Delays;
        for (var i = 0; i < repeat; i++)
        {
            if (i > 0)
            {
                await Delay(delays.BetweenKeys, ct);
            }

            await PressOnceAsync(combo.Keys, delays.BetweenKeys, ct);
        }

        await Delay(delays.AfterType, ct);
    }

    private async Task PressOnceAsync(IReadOnlyList<ScanKey> keys, int pause, CancellationToken ct)
    {
        var down = 0;
        try
        {
            while (down < keys.Count)
            {
                if (down > 0)
                {
                    await Delay(pause, ct);
                }

                ct.ThrowIfCancellationRequested();
                sink.Send(Down(keys[down]));
                down++;
            }

            while (down > 0)
            {
                await Delay(pause, ct);
                sink.Send(Up(keys[down - 1]));
                down--;
            }
        }
        finally
        {
            if (down > 0)
            {
                ReleaseQuietly(keys, down);
            }
        }
    }

    /// <summary>Lets go of the first <paramref name="count"/> keys, last pressed first, in one batch.</summary>
    private void ReleaseQuietly(IReadOnlyList<ScanKey> keys, int count)
    {
        var ups = new InputEvent[count];
        for (var i = 0; i < count; i++)
        {
            ups[i] = Up(keys[count - 1 - i]);
        }

        try
        {
            sink.Send(ups);
        }
        catch (InputRejectedException)
        {
            // Windows refused input already (e.g. the PC got locked). The exception that stopped the press says more.
        }
    }

    private static InputEvent Down(ScanKey key) => InputEvent.KeyDown(key.ScanCode, key.Extended);

    private static InputEvent Up(ScanKey key) => InputEvent.KeyUp(key.ScanCode, key.Extended);

    /// <summary>The events for each keystroke, built before anything is sent so bad text types nothing.</summary>
    internal static List<InputEvent[]> Keystrokes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var keystrokes = new List<InputEvent[]>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\r':
                    continue;
                case '\n':
                    keystrokes.Add(Key(EnterScanCode));
                    continue;
                case '\t':
                    keystrokes.Add(Key(TabScanCode));
                    continue;
            }

            if (char.IsControl(c))
            {
                throw new ArgumentException(
                    $"Can't type the control character U+{(int)c:X4} at position {i}. Only \\n (Enter) and \\t (Tab) are allowed.",
                    nameof(text));
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                // An emoji or other character outside the BMP: both halves in one call, so they arrive together.
                var low = text[++i];
                keystrokes.Add([InputEvent.CharDown(c), InputEvent.CharUp(c), InputEvent.CharDown(low), InputEvent.CharUp(low)]);
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw new ArgumentException($"Broken character (lone surrogate U+{(int)c:X4}) at position {i}.", nameof(text));
            }

            keystrokes.Add([InputEvent.CharDown(c), InputEvent.CharUp(c)]);
        }

        return keystrokes;
    }

    private static InputEvent[] Key(ushort scanCode) => [InputEvent.KeyDown(scanCode), InputEvent.KeyUp(scanCode)];

    private Task Delay(int milliseconds, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromMilliseconds(milliseconds), time, ct);
}
