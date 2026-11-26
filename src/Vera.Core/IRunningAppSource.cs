namespace Vera.Core;

/// <summary>An app with a window, as the tester sees it in the app picker.</summary>
/// <param name="WindowTitle">Empty when the system doesn't share it.</param>
public sealed record RunningAppInfo(int ProcessId, string Name, string WindowTitle);

/// <summary>Lists the running apps with a window, excluding VERA itself. One implementation per platform.</summary>
public interface IRunningAppSource
{
    IReadOnlyList<RunningAppInfo> List();
}

/// <summary>An <see cref="IRunningAppSource"/> on a system where the windows of one app can be told apart, so VERA stays with the one the tester picked (#44).</summary>
public interface IPickedWindows
{
    /// <summary>The tester picked this app from the last <see cref="IRunningAppSource.List"/>.</summary>
    void Picked(int processId);

    /// <summary>The title of the window VERA would read now; null when the app has none. Another window that was already open doesn't count, even when it is in front.</summary>
    string? TitleOf(int processId);
}
