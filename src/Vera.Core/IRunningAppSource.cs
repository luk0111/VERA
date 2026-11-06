namespace Vera.Core;

/// <summary>An app with a window, as the tester sees it in the app picker.</summary>
/// <param name="WindowTitle">Empty when the system doesn't share it.</param>
public sealed record RunningAppInfo(int ProcessId, string Name, string WindowTitle);

/// <summary>Lists the running apps with a window, excluding VERA itself. One implementation per platform.</summary>
public interface IRunningAppSource
{
    IReadOnlyList<RunningAppInfo> List();
}
