using Vera.Core;

namespace Vera.Application;

/// <summary>What the chat model is told about the app the tester picked.</summary>
/// <param name="Name">The process name, e.g. <c>Notepad</c>.</param>
/// <param name="WindowTitle">Empty when the system doesn't share it.</param>
/// <param name="Elements">The window's elements when it was read; empty when it couldn't be read.</param>
/// <param name="Problem">Why the window couldn't be read, e.g. it is minimized; null when it was read.</param>
/// <param name="ListedTitle">
/// The title the system's app list showed when the window was read. It is compared with the list later to notice a change,
/// e.g. another browser tab. It can differ from <see cref="WindowTitle"/>, which the adapter reads another way.
/// </param>
public sealed record ChatAppContext(
    int ProcessId,
    string Name,
    string WindowTitle,
    IReadOnlyList<UiElement> Elements,
    string? Problem = null,
    string? ListedTitle = null)
{
    /// <summary>The elements the model can see, without the window itself.</summary>
    public int ElementCount => Elements.Count(e => e.Id > 0);
}
