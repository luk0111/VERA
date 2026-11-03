using System.Globalization;

using Vera.Core;
using Vera.Reporting;

namespace Vera.Application;

/// <summary>
/// Finds one element in a running app: reads its window, asks the model which element it is, and saves a screenshot with it boxed.
/// It is the agent loop's look-and-decide step, run once and shown to the tester instead of acted on.
/// </summary>
public sealed class FindElementService(Func<IAppAdapter> newAdapter, ILlmClient llm, string outputDirectory)
{
    internal const string NoElementsReason = "VERA can't see any buttons or fields in this window. Is it minimized?";

    /// <param name="what">What the tester typed, e.g. <c>Log in button</c>.</param>
    /// <exception cref="InvalidOperationException">The app is closed or has no window.</exception>
    public async Task<FindResult> FindAsync(int processId, string windowTitle, string what, CancellationToken ct)
    {
        var target = new AppTarget(windowTitle, AppPlatform.Desktop, "", ProcessId: processId);
        ScreenState screen;
        var adapter = newAdapter();
        await using (adapter.ConfigureAwait(false))
        {
            await adapter.StartAsync(target, ct).ConfigureAwait(false);
            screen = await adapter.GetScreenStateAsync(ct).ConfigureAwait(false);
        }

        if (screen.Elements.Count <= 1)
        {
            return new FindResult(false, null, NoElementsReason, await SaveAsync(screen.ScreenshotPng, ct).ConfigureAwait(false));
        }

        var task = new TestTask($"Click the {what}", $"The {what} is found", target);
        var action = await llm.DecideNextActionAsync(task, screen, [], ct).ConfigureAwait(false);

        var element = action.Type is AgentActionType.Click or AgentActionType.Type
            ? screen.Elements.FirstOrDefault(e => e.Id > 0 && e.Id == action.TargetId)
            : null;
        var image = element is null ? screen.ScreenshotPng : ElementHighlight.DrawBox(screen.ScreenshotPng, screen.Elements[0].Bounds, element.Bounds);
        return new FindResult(element is not null, element, action.Reason, await SaveAsync(image, ct).ConfigureAwait(false));
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
