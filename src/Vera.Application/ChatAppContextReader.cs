using Vera.Core;

namespace Vera.Application;

/// <summary>
/// Reads the picked app's window, so the chat model knows which app it is and what is in it,
/// and notices when the window's title changes, e.g. when the tester switches to another browser tab.
/// </summary>
/// <param name="front">Gives VERA's window the front back after each read (#58); null leaves the app in front.</param>
public sealed class ChatAppContextReader(Func<IAppAdapter> newAdapter, IRunningAppSource runningApps, IFrontWindow? front = null)
{
    /// <summary>
    /// Attaches to the process and reads its window. Never fails because of the app:
    /// when the window is gone or minimized, the context has no elements and says why in <see cref="ChatAppContext.Problem"/>.
    /// </summary>
    public Task<ChatAppContext> ReadAsync(int processId, string name, string windowTitle, CancellationToken ct) =>
        FocusReturn.AroundAsync(front, () => ReadWindowAsync(processId, name, windowTitle, ct));

    private async Task<ChatAppContext> ReadWindowAsync(int processId, string name, string windowTitle, CancellationToken ct)
    {
        // Taken before the read, so a change during the read is noticed on the next check.
        var listedTitle = ListedTitle(processId);

        // The title tells the adapter which window it is when the app has several, like Find and the steps runner do (#44).
        var target = new AppTarget(windowTitle, AppPlatform.Desktop, "", ProcessId: processId);
        try
        {
            ScreenState screen;
            var adapter = newAdapter();
            await using (adapter.ConfigureAwait(false))
            {
                await adapter.StartAsync(target, ct).ConfigureAwait(false);
                screen = await adapter.GetScreenStateAsync(ct).ConfigureAwait(false);
            }

            // The title read now is fresher than the one from the picker, e.g. after a file was opened.
            var title = string.IsNullOrWhiteSpace(screen.WindowTitle) ? windowTitle : screen.WindowTitle;
            return new ChatAppContext(processId, name, title, screen.Elements, ListedTitle: listedTitle);
        }
        catch (InvalidOperationException e)
        {
            return new ChatAppContext(processId, name, windowTitle, [], e.Message, listedTitle);
        }
    }

    /// <summary>
    /// True when the system's app list now shows another title for the app than when <paramref name="last"/> was read.
    /// It only asks the app list, so it is quick and doesn't bring the app to the front.
    /// False when the title can't be known now: the app is gone, has no window, or macOS hides titles without the screen recording permission.
    /// </summary>
    public bool TitleChanged(ChatAppContext last)
    {
        ArgumentNullException.ThrowIfNull(last);
        var now = ListedTitle(last.ProcessId);
        return now is not null && !string.Equals(now, last.ListedTitle, StringComparison.Ordinal);
    }

    private string? ListedTitle(int processId) =>
        runningApps.List().FirstOrDefault(a => a.ProcessId == processId) is { WindowTitle.Length: > 0 } app ? app.WindowTitle : null;
}
