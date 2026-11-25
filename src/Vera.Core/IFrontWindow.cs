namespace Vera.Core;

/// <summary>A window that was in front, as its system names it: a window handle on Windows, the app's process id on macOS.</summary>
public sealed record FrontWindow(long Id);

/// <summary>
/// Remembers which window is in front and brings it back, e.g. the terminal VERA runs in when it has to ask something while it works in an app.
/// One implementation per platform.
/// </summary>
public interface IFrontWindow
{
    /// <summary>The window in front now; null when it can't be known.</summary>
    FrontWindow? Current();

    /// <returns>False when it couldn't be brought to the front, e.g. because it was closed.</returns>
    Task<bool> BringBackAsync(FrontWindow window, CancellationToken ct);
}
