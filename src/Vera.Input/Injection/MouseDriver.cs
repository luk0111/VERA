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

    private Task Delay(int milliseconds, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromMilliseconds(milliseconds), time, ct);
}
