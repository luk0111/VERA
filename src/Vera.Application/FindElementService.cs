using System.Globalization;

using Vera.Core;
using Vera.Reporting;

namespace Vera.Application;

/// <summary>
/// Finds one element in a running app: reads its window, asks the model which element it is, and saves a screenshot with it boxed.
/// It is the agent loop's look-and-decide step, run once and shown to the tester. <see cref="ClickAsync"/> and <see cref="TypeAsync"/> then act on what was found, if the tester asks.
/// </summary>
public sealed class FindElementService(Func<IAppAdapter> newAdapter, ILlmClient llm, string outputDirectory)
{
    /// <param name="what">What the tester typed, e.g. <c>Log in button</c>.</param>
    /// <exception cref="InvalidOperationException">The app is closed or has no window.</exception>
    public async Task<FindResult> FindAsync(int processId, string windowTitle, string what, CancellationToken ct)
    {
        var target = Target(processId, windowTitle);
        ScreenState screen;
        var adapter = newAdapter();
        await using (adapter.ConfigureAwait(false))
        {
            await adapter.StartAsync(target, ct).ConfigureAwait(false);
            screen = await adapter.GetScreenStateAsync(ct).ConfigureAwait(false);
        }

        var (element, reason) = await ElementFinder.FindAsync(llm, screen, target, what, ct).ConfigureAwait(false);
        var image = element is null ? screen.ScreenshotPng : ElementHighlight.DrawBox(screen.ScreenshotPng, screen.Elements[0].Bounds, element.Bounds);
        return new FindResult(element is not null, element, reason, await SaveAsync(image, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Clicks an element that <see cref="FindAsync"/> returned. Ids and positions are only valid for one reading, so it reads the window again
    /// and clicks the same element there: same role, name and locator, the nearest to where it was if several match. No model is asked.
    /// </summary>
    /// <returns>The element as it was clicked, with its id and position in the new reading.</returns>
    /// <exception cref="InvalidOperationException">The app is closed, the element is no longer on the screen, or it can't be clicked (the message says why).</exception>
    public Task<UiElement> ClickAsync(int processId, string windowTitle, UiElement found, CancellationToken ct) =>
        ActOnAsync(processId, windowTitle, found, (adapter, id) => adapter.ClickAsync(id, ct), ct);

    /// <summary>
    /// Replaces the text of a field that <see cref="FindAsync"/> returned, finding it again as <see cref="ClickAsync"/> does.
    /// The adapter clicks the field, selects all, then types; empty text clears it.
    /// </summary>
    /// <inheritdoc cref="ClickAsync" path="/returns"/>
    /// <inheritdoc cref="ClickAsync" path="/exception"/>
    public Task<UiElement> TypeAsync(int processId, string windowTitle, UiElement found, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ActOnAsync(processId, windowTitle, found, (adapter, id) => adapter.TypeAsync(id, text, ct), ct);
    }

    /// <summary>Whether the tester can type into the element: a text field. Others can only be clicked.</summary>
    public static bool AcceptsText(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Role == "Edit";
    }

    private async Task<UiElement> ActOnAsync(int processId, string windowTitle, UiElement found, Func<IAppAdapter, int, Task> act, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(found);
        var adapter = newAdapter();
        await using (adapter.ConfigureAwait(false))
        {
            await adapter.StartAsync(Target(processId, windowTitle), ct).ConfigureAwait(false);
            var screen = await adapter.GetScreenStateAsync(ct).ConfigureAwait(false);
            var same = SameElement(screen.Elements, found)
                ?? throw new InvalidOperationException($"\"{found.Name}\" is no longer on the screen.");
            await act(adapter, same.Id).ConfigureAwait(false);
            return same;
        }
    }

    private static AppTarget Target(int processId, string windowTitle) =>
        new(windowTitle, AppPlatform.Desktop, "", ProcessId: processId);

    private static UiElement? SameElement(IReadOnlyList<UiElement> elements, UiElement found) =>
        elements
            .Where(e => e.Id >= 1 && e.Role == found.Role && e.Name == found.Name && e.Locator == found.Locator)
            .MinBy(e => DistanceSquared(e.Bounds, found.Bounds));

    // Between the centres of two rectangles; long, so huge screens can't overflow.
    private static long DistanceSquared(ScreenRect a, ScreenRect b)
    {
        long dx = (a.X + (a.Width / 2)) - (b.X + (b.Width / 2));
        long dy = (a.Y + (a.Height / 2)) - (b.Y + (b.Height / 2));
        return (dx * dx) + (dy * dy);
    }

    private async Task<string> SaveAsync(ReadOnlyMemory<byte> png, CancellationToken ct)
    {
        var folder = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"find-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.png");
        await File.WriteAllBytesAsync(path, png, ct).ConfigureAwait(false);
        return path;
    }
}
