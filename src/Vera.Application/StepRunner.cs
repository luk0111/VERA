using System.Diagnostics;
using System.Globalization;

using Vera.Core;
using Vera.Input;
using Vera.Llm;
using Vera.Reporting;

namespace Vera.Application;

/// <summary>
/// Runs a chain of steps the tester wrote in a running app. The command decides the action; the model only finds each element, as Find does
/// (<see cref="ElementFinder"/>), and code checks text for <c>expect … contains</c>. No agent decides what to do.
/// Before each step it reads the window. It saves one screenshot per step and stops at the first step that isn't done.
/// A slow app gets time: a step whose element isn't there yet (or is disabled, or an expect that doesn't hold yet) reads the window again,
/// ten times over <paramref name="wait"/>, before it gives up. The model is asked again only when the window's elements changed.
/// </summary>
/// <param name="pause">How long the app gets to react after each click, type or key press.</param>
/// <param name="wait">How long a step waits for the app; <see cref="DefaultWait"/> when null. Zero reads once.</param>
public sealed class StepRunner(Func<IAppAdapter> newAdapter, ILlmClient llm, ISecretStore secrets, string outputDirectory, TimeSpan pause, TimeSpan? wait = null)
{
    /// <summary>The pause after each action in the app.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromSeconds(1);

    /// <summary>How long a step waits for the app before it gives up.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _wait = wait ?? DefaultWait;

    /// <param name="approveRisky">Asked before clicking an element with a risky name (<see cref="RiskyWords"/>); false stops the run. Null clicks without asking.</param>
    /// <param name="progress">Told about each step as soon as it's finished.</param>
    /// <exception cref="InvalidOperationException">The app is closed or has no window.</exception>
    /// <remarks>
    /// An error that isn't about one step, e.g. the model can't be reached, ends the run: the result keeps the steps finished so far,
    /// the verdict is <see cref="Verdict.Inconclusive"/>, and <see cref="StepRunResult.StoppedBy"/> holds the error.
    /// </remarks>
    public async Task<StepRunResult> RunAsync(
        int processId,
        string windowTitle,
        IReadOnlyList<StepCommand> steps,
        Func<UiElement, Task<bool>>? approveRisky,
        IProgress<StepResult>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var folder = Path.GetFullPath(Path.Combine(outputDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
        var target = new AppTarget(windowTitle, AppPlatform.Desktop, "", ProcessId: processId);
        var results = new List<StepResult>();
        Exception? stoppedBy = null;

        var adapter = newAdapter();
        await using (adapter.ConfigureAwait(false))
        {
            await adapter.StartAsync(target, ct).ConfigureAwait(false);
            for (var i = 0; i < steps.Count; i++)
            {
                StepResult result;
                try
                {
                    var run = new Run(this, adapter, target, i + 1, steps[i], folder, approveRisky, ct);
                    result = await run.ExecuteAsync().ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Any error outside one step's own failures ends the run; the caller gets the steps done so far and the error.
                catch (Exception e) when (!ct.IsCancellationRequested)
#pragma warning restore CA1031
                {
                    stoppedBy = e;
                    break;
                }

                results.Add(result);
                progress?.Report(result);
                if (result.Outcome != StepOutcome.Done)
                {
                    break;
                }
            }
        }

        var verdict = stoppedBy is not null ? Verdict.Inconclusive : results.LastOrDefault()?.Outcome switch
        {
            StepOutcome.CheckFailed => Verdict.Failed,
            StepOutcome.NotDone => Verdict.Inconclusive,
            _ => Verdict.Passed,
        };
        return new StepRunResult(verdict, results, folder, stoppedBy);
    }

    private Task PauseAsync(CancellationToken ct) => pause > TimeSpan.Zero ? Task.Delay(pause, ct) : Task.CompletedTask;

    // Ten readings over the wait: every 0.5 s for 5 s.
    private TimeSpan ReadEvery => _wait / 10;

    // One step: read, find with the model, act or check, save the screenshot.
    private sealed class Run(
        StepRunner runner, IAppAdapter adapter, AppTarget target, int number, StepCommand step, string folder,
        Func<UiElement, Task<bool>>? approveRisky, CancellationToken ct)
    {
        private readonly Stopwatch _clock = new();
        private ScreenState? _screen;
        private UiElement? _element;

        public async Task<StepResult> ExecuteAsync()
        {
            StepOutcome outcome;
            string message;
            try
            {
                _clock.Start();
                _screen = await adapter.GetScreenStateAsync(ct).ConfigureAwait(false);
                (outcome, message) = await DoStepAsync().ConfigureAwait(false);
            }
            catch (StepNotDoneException e)
            {
                (outcome, message) = (StepOutcome.NotDone, e.Message);
            }
            catch (SecretNotFoundException e)
            {
                (outcome, message) = (StepOutcome.NotDone, $"Secret \"{e.SecretName}\" isn't saved. Save it in Settings.");
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException or UnreadableAnswerException
                or InvalidKeyComboException or InputRejectedException or ScreenUnavailableException)
            {
                (outcome, message) = (StepOutcome.NotDone, e.Message);
            }

            var screenshot = await SaveAsync().ConfigureAwait(false);
            return new StepResult(number, step, outcome, message, screenshot);
        }

        private async Task<(StepOutcome, string)> DoStepAsync()
        {
            switch (step)
            {
                case ClickStep click:
                    var button = await UsableAsync(click.Target).ConfigureAwait(false);
                    if (approveRisky is not null && RiskyWords.IsRisky(button.Name) && !await approveRisky(button).ConfigureAwait(false))
                    {
                        throw new StepNotDoneException($"Stopped: clicking {ElementFinder.Describe(button)} wasn't approved.");
                    }

                    await adapter.ClickAsync(button.Id, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done, $"clicked {ElementFinder.Describe(button)} (element {button.Id})");

                case TypeStep type:
                    var field = await UsableAsync(type.Target).ConfigureAwait(false);
                    if (!FindElementService.AcceptsText(field))
                    {
                        // The model picked something else, e.g. Notepad's tab for "notepad": typing there would do nothing useful.
                        throw new StepNotDoneException($"{ElementFinder.Describe(field)} can't take text. Describe the field or text area to type into.");
                    }

                    var text = await SecretPlaceholder.ResolveAsync(type.Text, runner.Secrets, ct).ConfigureAwait(false);
                    await adapter.TypeAsync(field.Id, text, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done, $"typed into {ElementFinder.Describe(field)} (element {field.Id})");

                case PressStep press:
                    await adapter.PressKeyAsync(press.Keys, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done, $"pressed {press.Keys}");

                case ScrollStep scroll:
                    var wholeWindow = ElementFinder.MeansWindow(scroll.Target) && _screen!.Elements.Count > 0;
                    var area = wholeWindow ? _screen!.Elements[0] : await FoundAsync(scroll.Target).ConfigureAwait(false);
                    var (down, right) = scroll.Direction switch
                    {
                        ScrollDirection.Up => (-scroll.Notches, 0),
                        ScrollDirection.Down => (scroll.Notches, 0),
                        ScrollDirection.Left => (0, -scroll.Notches),
                        _ => (0, scroll.Notches),
                    };
                    await adapter.ScrollAsync(area.Id, down, right, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done,
                        $"scrolled {scroll.Direction.ToString().ToLowerInvariant()} {scroll.Notches} in {(wholeWindow ? "the window" : $"{ElementFinder.Describe(area)} (element {area.Id})")}");

                case DragStep drag:
                    var dragged = await UsableAsync(drag.Source).ConfigureAwait(false);
                    var drop = await FoundAsync(drag.Destination).ConfigureAwait(false);

                    // Waiting for the drop target may have read the window again, so follow the dragged element into that reading.
                    dragged = ElementFinder.Same(_screen!.Elements, dragged)
                        ?? throw new StepNotDoneException($"{ElementFinder.Describe(dragged)} went away while VERA looked for \"{drag.Destination}\".");
                    if (drop.Id == dragged.Id)
                    {
                        throw new StepNotDoneException($"\"{drag.Source}\" and \"{drag.Destination}\" are both {ElementFinder.Describe(drop)}. Describe two different elements.");
                    }

                    // The drop target decides what happens, e.g. a "Trash" or "Delete" area.
                    if (approveRisky is not null && RiskyWords.IsRisky(drop.Name) && !await approveRisky(drop).ConfigureAwait(false))
                    {
                        throw new StepNotDoneException($"Stopped: dropping onto {ElementFinder.Describe(drop)} wasn't approved.");
                    }

                    await adapter.DragAsync(dragged.Id, drop.Id, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done,
                        $"dragged {ElementFinder.Describe(dragged)} (element {dragged.Id}) to {ElementFinder.Describe(drop)} (element {drop.Id})");

                case HoverStep hover:
                    var hovered = await FoundAsync(hover.Target).ConfigureAwait(false);
                    await adapter.HoverAsync(hovered.Id, ct).ConfigureAwait(false);
                    return (StepOutcome.Done, $"hovered over {ElementFinder.Describe(hovered)} (element {hovered.Id})");

                case ExpectShownStep shown:
                    var (seen, notSeen) = await WaitForAsync(shown.Target, _ => true).ConfigureAwait(false);
                    return seen is null
                        ? (StepOutcome.CheckFailed, notSeen)
                        : (StepOutcome.Done, $"{ElementFinder.Describe(seen)} is shown");

                case ExpectContainsStep contains:
                    // Text that can't be read won't become readable by waiting.
                    var (holder, missing) = await WaitForAsync(
                        contains.Target,
                        e => e.Value is null || e.Value.Contains(contains.Text, StringComparison.OrdinalIgnoreCase)).ConfigureAwait(false);
                    if (holder is null)
                    {
                        return (StepOutcome.CheckFailed, missing);
                    }

                    return holder.Value switch
                    {
                        null => (StepOutcome.NotDone, $"VERA can't read the text of {ElementFinder.Describe(holder)}."),
                        var value when value.Contains(contains.Text, StringComparison.OrdinalIgnoreCase) =>
                            (StepOutcome.Done, $"{ElementFinder.Describe(holder)} contains {StepCommand.Quote(contains.Text)}"),
                        var value => (StepOutcome.CheckFailed,
                            $"{ElementFinder.Describe(holder)} has {StepCommand.Quote(value.TrimEnd('\r', '\n'))}, which doesn't contain {StepCommand.Quote(contains.Text)}."),
                    };

                case ExpectTitleStep title:
                    while (!_screen!.WindowTitle.Contains(title.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!await ReadAgainAsync().ConfigureAwait(false))
                        {
                            break;
                        }
                    }

                    var windowTitle = _screen.WindowTitle;
                    return windowTitle.Contains(title.Text, StringComparison.OrdinalIgnoreCase)
                        ? (StepOutcome.Done, $"the title contains {StepCommand.Quote(title.Text)}")
                        : (StepOutcome.CheckFailed, $"The title is {StepCommand.Quote(windowTitle)}, which doesn't contain {StepCommand.Quote(title.Text)}.");

                default:
                    throw new NotSupportedException($"VERA can't run \"{step}\" yet.");
            }
        }

        // The element the model picks for the description, waiting for the app until it's there and ready; or why there is none.
        // The model is asked only when the window's elements changed since it was last asked; the element it found is followed without it.
        private async Task<(UiElement? Element, string NotFound)> WaitForAsync(string description, Func<UiElement, bool> ready)
        {
            UiElement? element = null;
            string? asked = null;
            var reason = "";
            do
            {
                if (element is not null)
                {
                    element = ElementFinder.Same(_screen!.Elements, element);
                }

                var seen = ElementFinder.Seen(_screen!);
                if (element is null && _screen!.Elements.Count > 1 && seen != asked)
                {
                    asked = seen;
                    (element, reason) = await ElementFinder.FindAsync(runner.Llm, _screen, target, description, ct).ConfigureAwait(false);
                }

                if (element is not null && ready(element))
                {
                    break;
                }
            }
            while (await ReadAgainAsync().ConfigureAwait(false));

            _element = element;

            // Nothing to look at is not a failed check: the window may be minimized or its elements can't be read.
            if (_screen!.Elements.Count <= 1)
            {
                throw new StepNotDoneException(ElementFinder.NoElementsReason);
            }

            var notFound = $"I can't find {StepCommand.Quote(description)} on this screen.";
            return (element, reason.Length == 0 ? notFound : $"{notFound} {reason}");
        }

        // Waits a little and reads the window again; false once the step has waited long enough.
        private async Task<bool> ReadAgainAsync()
        {
            if (_clock.Elapsed >= runner._wait)
            {
                return false;
            }

            await Task.Delay(runner.ReadEvery, ct).ConfigureAwait(false);
            _screen = await adapter.GetScreenStateAsync(ct).ConfigureAwait(false);
            return true;
        }

        // An element to scroll in, drop on or hover over: found. Being disabled doesn't matter for these.
        private async Task<UiElement> FoundAsync(string description)
        {
            var (element, notFound) = await WaitForAsync(description, _ => true).ConfigureAwait(false);
            return element ?? throw new StepNotDoneException(notFound);
        }

        // The element to click or type into: found, and enabled.
        private async Task<UiElement> UsableAsync(string description)
        {
            var (element, notFound) = await WaitForAsync(description, e => e.IsEnabled).ConfigureAwait(false);
            if (element is null)
            {
                throw new StepNotDoneException(notFound);
            }

            return element.IsEnabled ? element : throw new StepNotDoneException($"{ElementFinder.Describe(element)} is disabled.");
        }

        // The screen the step saw, with its element boxed when there is one.
        private async Task<string?> SaveAsync()
        {
            if (_screen is null || _screen.ScreenshotPng.IsEmpty)
            {
                return null;
            }

            var png = _element is not null && _screen.Elements.Count > 0
                ? ElementHighlight.DrawBox(_screen.ScreenshotPng, _screen.Elements[0].Bounds, _element.Bounds)
                : _screen.ScreenshotPng.ToArray();
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{number:00}.png");
            await File.WriteAllBytesAsync(path, png, ct).ConfigureAwait(false);
            return path;
        }
    }

    private ILlmClient Llm => llm;

    private ISecretStore Secrets => secrets;

    // A step VERA can't do; the message is shown to the tester.
    private sealed class StepNotDoneException(string message) : Exception(message);
}
