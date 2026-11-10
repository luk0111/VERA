using Vera.Core;

namespace Vera.Application;

/// <summary>What the chat model is told about the app the tester picked.</summary>
/// <param name="Name">The process name, e.g. <c>Notepad</c>.</param>
/// <param name="WindowTitle">Empty when the system doesn't share it.</param>
/// <param name="Elements">The window's elements when it was read; empty when it couldn't be read.</param>
/// <param name="Problem">Why the window couldn't be read, e.g. it is minimized; null when it was read.</param>
public sealed record ChatAppContext(
    int ProcessId,
    string Name,
    string WindowTitle,
    IReadOnlyList<UiElement> Elements,
    string? Problem = null)
{
    /// <summary>The elements the model can see, without the window itself.</summary>
    public int ElementCount => Elements.Count(e => e.Id > 0);
}
