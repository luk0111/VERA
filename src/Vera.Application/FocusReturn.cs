using Vera.Core;

namespace Vera.Application;

/// <summary>
/// Reading or acting on the app brings it to the front. Afterwards the window that was in front before, VERA's own, gets the front back,
/// so the tester can type the next message without clicking or alt-tabbing back (#58).
/// </summary>
internal static class FocusReturn
{
    /// <param name="front">Null leaves the app in front.</param>
    public static async Task<T> AroundAsync<T>(IFrontWindow? front, Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var back = front?.Current();
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            await BringBackAsync(front, back).ConfigureAwait(false);
        }
    }

    /// <summary>Brings <paramref name="back"/> to the front again; never cancelled, so a stopped run still gives the tester their window back.</summary>
    public static async Task BringBackAsync(IFrontWindow? front, FrontWindow? back)
    {
        if (front is not null && back is not null)
        {
            await front.BringBackAsync(back, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
