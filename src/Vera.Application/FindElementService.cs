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
        var sameNameElsewhere = element is not null && screen.Elements.Count(e => e.Id >= 1 && e.Role == element.Role && e.Name == element.Name) > 1;
        return new FindResult(element is not null, element, reason, await SaveAsync(image, ct).ConfigureAwait(false), sameNameElsewhere);
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

    /// <summary>
    /// Whether the element can be typed into: a text field, a text area (e.g. Windows 11 Notepad's page) or a combo box, which can often be typed into.
    /// Others (buttons, tabs, links, menus…) can only be clicked.
    /// </summary>
    public static bool AcceptsText(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Role is "Edit" or "Document" or "ComboBox";
    }

    /// <summary>Whether clicking the element may change or send something, from words in its name; a steps run asks before such a click too.</summary>
    /// <param name="rules">The words that count; <see cref="RiskRules.Default"/> when null.</param>
    public static bool IsRisky(UiElement element, RiskRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (rules ?? RiskRules.Default).IsRisky(element.Name);
    }

    private async Task<UiElement> ActOnAsync(int processId, string windowTitle, UiElement found, Func<IAppAdapter, int, Task> act, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(found);
        var adapter = newAdapter();
        await using (adapter.ConfigureAwait(false))
        {
            await adapter.StartAsync(Target(processId, windowTitle), ct).ConfigureAwait(false);
            var screen = await adapter.GetScreenStateAsync(ct).ConfigureAwait(false);
            var same = ElementFinder.Same(screen.Elements, found)
                ?? throw new InvalidOperationException($"\"{found.Name}\" is no longer on the screen.");
            await act(adapter, same.Id).ConfigureAwait(false);
            return same;
        }
    }

    private static AppTarget Target(int processId, string windowTitle) =>
        new(windowTitle, AppPlatform.Desktop, "", ProcessId: processId);

    private async Task<string> SaveAsync(ReadOnlyMemory<byte> png, CancellationToken ct)
    {
        var folder = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"find-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.png");
        await File.WriteAllBytesAsync(path, png, ct).ConfigureAwait(false);
        return path;
    }
}
