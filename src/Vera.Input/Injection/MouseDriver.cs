using Microsoft.Extensions.Options;

using Vera.Input.Coordinates;
using Vera.Input.Options;

namespace Vera.Input.Injection;

/// <summary>
/// Builds the event sequences for mouse actions and sends them through <see cref="IInputSink"/>, with the pauses from
/// <see cref="DelayOptions"/> in between. Takes points already mapped to Windows mouse units.
/// </summary>
internal sealed class MouseDriver(IInputSink sink, IOptions<VeraInputOptions> options, TimeProvider time)
{
    public const int MaxClicks = 3;

    /// <summary>How many small moves a drag or a hover glide makes, <c>BetweenMoveSteps</c> apart.</summary>
    public const int GlideSteps = 15;

    /// <summary>The most wheel notches one scroll can turn each way, so a wrong number can't scroll for minutes.</summary>
    public const int MaxNotches = 50;

    public async Task MoveAsync(NormalizedPoint point, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        sink.Send(InputEvent.MouseMove(point));
        await Delay(options.Value.Delays.AfterMove, ct);
    }

    /// <summary>Moves to <paramref name="point"/>, then presses and releases <paramref name="button"/> <paramref name="clicks"/> times.</summary>
    /// <remarks>If cancelled while the button is down, it is still released, so it never stays stuck.</remarks>
    public async Task ClickAsync(NormalizedPoint point, MouseButton button, int clicks, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(clicks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clicks, MaxClicks);

        var delays = options.Value.Delays;
        await MoveAsync(point, ct);
        for (var i = 0; i < clicks; i++)
        {
            if (i > 0)
            {
                await Delay(delays.BetweenClicks, ct);
            }

            sink.Send(InputEvent.MouseDown(button));
            try
            {
                await Delay(delays.MouseDownUp, ct);
            }
            finally
            {
                sink.Send(InputEvent.MouseUp(button));
            }
        }

        await Delay(delays.AfterClick, ct);
    }

    /// <summary>Glides from <paramref name="from"/> (where the pointer is now) to <paramref name="to"/>, then waits <c>HoverDwell</c>.</summary>
    public async Task HoverAsync(NormalizedPoint from, NormalizedPoint to, CancellationToken ct)
    {
        await GlideAsync(from, to, ct);
        await Delay(options.Value.Delays.HoverDwell, ct);
    }

    /// <summary>
    /// Moves to <paramref name="point"/>, then turns the wheel one notch at a time, <c>BetweenNotches</c> apart: first
    /// <paramref name="deltaY"/> notches (positive scrolls down), then <paramref name="deltaX"/> (positive scrolls right).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Both are 0, or one is beyond <see cref="MaxNotches"/>. Nothing moves.</exception>
    public async Task ScrollAsync(NormalizedPoint point, int deltaY, int deltaX, CancellationToken ct)
    {
        CheckScroll(deltaY, deltaX);
        await MoveAsync(point, ct);

        var notches = Notches(deltaY, horizontal: false).Concat(Notches(deltaX, horizontal: true)).ToList();
        for (var i = 0; i < notches.Count; i++)
        {
            if (i > 0)
            {
                await Delay(options.Value.Delays.BetweenNotches, ct);
            }

            sink.Send(notches[i]);
        }

        await Delay(options.Value.Delays.AfterClick, ct);
    }

    /// <exception cref="ArgumentOutOfRangeException">Both are 0, or one is beyond <see cref="MaxNotches"/>.</exception>
    public static void CheckScroll(int deltaY, int deltaX)
    {
        if (deltaY == 0 && deltaX == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaY), "Scroll by at least one notch.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs(deltaY), MaxNotches, nameof(deltaY));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs(deltaX), MaxNotches, nameof(deltaX));
    }

    /// <summary>
    /// Moves to <paramref name="from"/>, presses <paramref name="button"/>, glides to <paramref name="to"/> in small moves (so apps
    /// notice the drag), waits <c>MouseDownUp</c> so the target sees the pointer, then releases.
    /// </summary>
    /// <remarks>If cancelled while the button is down, it is still released, so it never stays stuck.</remarks>
    public async Task DragAsync(NormalizedPoint from, NormalizedPoint to, MouseButton button, CancellationToken ct)
    {
        var delays = options.Value.Delays;
        await MoveAsync(from, ct);
        sink.Send(InputEvent.MouseDown(button));
        try
        {
            await Delay(delays.MouseDownUp, ct);
            await GlideAsync(from, to, ct);
            await Delay(delays.MouseDownUp, ct);
        }
        finally
        {
            sink.Send(InputEvent.MouseUp(button));
        }

        await Delay(delays.AfterClick, ct);
    }

    /// <summary>The points of a glide: <see cref="GlideSteps"/> moves, evenly spaced, the last one exactly on <paramref name="to"/>.</summary>
    internal static IEnumerable<NormalizedPoint> Glide(NormalizedPoint from, NormalizedPoint to)
    {
        for (var i = 1; i <= GlideSteps; i++)
        {
            yield return new NormalizedPoint(
                from.X + (int)Math.Round((long)(to.X - from.X) * i / (double)GlideSteps),
                from.Y + (int)Math.Round((long)(to.Y - from.Y) * i / (double)GlideSteps));
        }
    }

    private async Task GlideAsync(NormalizedPoint from, NormalizedPoint to, CancellationToken ct)
    {
        var first = true;
        foreach (var point in Glide(from, to))
        {
            if (!first)
            {
                await Delay(options.Value.Delays.BetweenMoveSteps, ct);
            }

            ct.ThrowIfCancellationRequested();
            sink.Send(InputEvent.MouseMove(point));
            first = false;
        }
    }

    // Windows counts the vertical wheel positive away from the user (up); VERA's positive deltaY scrolls down.
    private static IEnumerable<InputEvent> Notches(int delta, bool horizontal)
    {
        var wheel = horizontal ? Math.Sign(delta) * InputEvent.WheelNotch : -Math.Sign(delta) * InputEvent.WheelNotch;
        return Enumerable.Repeat(InputEvent.MouseWheel(wheel, horizontal), Math.Abs(delta));
    }

    private Task Delay(int milliseconds, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromMilliseconds(milliseconds), time, ct);
}
