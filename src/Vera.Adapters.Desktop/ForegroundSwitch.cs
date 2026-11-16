namespace Vera.Adapters.Desktop;

/// <summary>
/// Brings an app's window to the front and checks that it got there.
/// Windows only lets a process do this when it owns the window in front or got the last input. VERA's keystrokes go to its terminal's process,
/// so a plain request is often refused: the app's taskbar button flashes and the window stays behind. Each try here is stronger than the one before.
/// </summary>
/// <param name="waitPerTry">How long Windows gets to switch windows after each try. 300 ms if not given.</param>
internal sealed class ForegroundSwitch(IForegroundApi api, TimeSpan? waitPerTry = null)
{
    internal const string Refused = "VERA couldn't bring the app's window to the front. Click the window and try again.";

    private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(25);

    private readonly TimeSpan _waitPerTry = waitPerTry ?? TimeSpan.FromMilliseconds(300);

    /// <returns>False when the app was already in front, true when it was brought there.</returns>
    /// <exception cref="InvalidOperationException">Windows refused every try, e.g. the app runs as administrator and VERA doesn't.</exception>
    public async Task<bool> BringToFrontAsync(nint window, CancellationToken ct)
    {
        if (api.IsInFront(window))
        {
            return false;
        }

        Action[] tries =
        [
            () => api.Request(window),
            () =>
            {
                api.SendEmptyInput();
                api.Request(window);
            },
            () => api.RequestAttached(window),
        ];

        foreach (var attempt in tries)
        {
            attempt();
            if (await IsInFrontSoonAsync(window, ct).ConfigureAwait(false))
            {
                return true;
            }
        }

        throw new InvalidOperationException(Refused);
    }

    private async Task<bool> IsInFrontSoonAsync(nint window, CancellationToken ct)
    {
        for (var waited = TimeSpan.Zero; ; waited += CheckEvery)
        {
            if (api.IsInFront(window))
            {
                return true;
            }

            if (waited >= _waitPerTry)
            {
                return false;
            }

            await Task.Delay(CheckEvery, ct).ConfigureAwait(false);
        }
    }
}
