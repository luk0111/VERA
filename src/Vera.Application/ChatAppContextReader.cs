using Vera.Core;

namespace Vera.Application;

/// <summary>Reads the picked app's window once, so the chat model knows which app it is and what is in it.</summary>
public sealed class ChatAppContextReader(Func<IAppAdapter> newAdapter)
{
    /// <summary>
    /// Attaches to the process and reads its window. Never fails because of the app:
    /// when the window is gone or minimized, the context has no elements and says why in <see cref="ChatAppContext.Problem"/>.
    /// </summary>
    public async Task<ChatAppContext> ReadAsync(int processId, string name, string windowTitle, CancellationToken ct)
    {
        var target = new AppTarget(name, AppPlatform.Desktop, "", ProcessId: processId);
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
            return new ChatAppContext(processId, name, title, screen.Elements);
        }
        catch (InvalidOperationException e)
        {
            return new ChatAppContext(processId, name, windowTitle, [], e.Message);
        }
    }
}
