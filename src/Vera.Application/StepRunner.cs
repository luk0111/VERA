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
/// <param name="front">Brings the window that was in front when the run started, VERA's own, back to ask about a risky step. Null leaves the app in front.</param>
/// <param name="apiKeyNames">The secrets that hold VERA's own model API keys. A <c>type</c> step that names one is not done: they never go into the app.</param>
/// <param name="trail">Where the secrets typed so far are kept out of every reading, shared with Find and the chat; this runner's own when null.</param>
public sealed class StepRunner(
    Func<IAppAdapter> newAdapter, ILlmClient llm, ISecretStore secrets, string outputDirectory, TimeSpan pause, TimeSpan? wait = null,
    IFrontWindow? front = null, IReadOnlyCollection<string>? apiKeyNames = null, SecretTrail? trail = null)
{
    /// <summary>The pause after each action in the app.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromSeconds(1);

    /// <summary>How long a step waits for the app before it gives up.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);

    /// <summary>How often <see cref="ScrollUntilStep"/> scrolls before it gives up. Each time can cost one question to the model.</summary>
    internal const int MaxScrolls = 10;

    // Before VERA knows how far one notch moves this app: little enough not to jump past anything.
    internal const int FirstNotches = 3;

    private readonly TimeSpan _wait = wait ?? DefaultWait;

    /// <param name="approveRisky">Asked before a click or drop on an element with a risky name, or a key that deletes (<see cref="RiskyWords"/>); gets what VERA is about to do, e.g. <c>click button "Delete"</c>. False stops the run. Null acts without asking.</param>
    /// <param name="progress">Told about each step as soon as it's finished.</param>
    /// <param name="activity">Told what VERA is doing in the running step, each time that changes.</param>
    /// <param name="ct">The tester's stop: cancelling it ends the run at once with <see cref="StepRunResult.StoppedByTester"/>, not an exception.</param>
    /// <exception cref="InvalidOperationException">The app is closed or has no window.</exception>
    /// <remarks>
    /// An error that isn't about one step, e.g. the model can't be reached, ends the run: the result keeps the steps finished so far,
    /// the verdict is <see cref="Verdict.Inconclusive"/>, and <see cref="StepRunResult.StoppedBy"/> holds the error.
    /// A stop by the tester works the same way, without an error.
    /// </remarks>
    public async Task<StepRunResult> RunAsync(
        int processId,
        string windowTitle,
        IReadOnlyList<StepCommand> steps,
        Func<string, Task<bool>>? approveRisky,
        IProgress<StepResult>? progress,
        IProgress<StepActivity>? activity,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var folder = Path.GetFullPath(Path.Combine(outputDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
        var target = new AppTarget(windowTitle, AppPlatform.Desktop, "", ProcessId: processId);
        var results = new List<StepResult>();
        Exception? stoppedBy = null;
        var stoppedByTester = false;

        // The tester just started the run, so VERA's window is in front.
        var asksIn = front?.Current();
        Run? opener = null;

        var adapter = newAdapter();
        await using (adapter.ConfigureAwait(false))
        {
            try
            {
                await adapter.StartAsync(target, ct).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Whatever the stop interrupted, the tester asked for it; nothing ran yet.
            catch (Exception) when (ct.IsCancellationRequested)
#pragma warning restore CA1031
            {
                stoppedByTester = true;
            }

            for (var i = 0; i < steps.Count && !stoppedByTester; i++)
            {
                StepResult result;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var run = new Run(this, adapter, target, i + 1, steps[i], folder, approveRisky, asksIn, opener, activity, ct);
                    result = await run.ExecuteAsync().ConfigureAwait(false);

                    // A step may turn what the stop interrupted into its own failure; it's still the tester's stop.
                    ct.ThrowIfCancellationRequested();

                    // What may have opened a menu: the last click or hover. Looking changes nothing; anything else does.
                    opener = steps[i] switch
                    {
                        ClickStep or HoverStep => run,
                        ExpectShownStep or ExpectGoneStep or ExpectContainsStep or ExpectTitleStep => opener,
                        _ => null,
                    };
                }
#pragma warning disable CA1031 // The tester stopped the run: the step that was running is dropped, the steps done so far are kept.
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    stoppedByTester = true;
                    break;
                }

                // Any error outside one step's own failures ends the run; the caller gets the steps done so far and the error.
                catch (Exception e)
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

        var verdict = stoppedBy is not null || stoppedByTester ? Verdict.Inconclusive : results.LastOrDefault()?.Outcome switch
        {
            StepOutcome.CheckFailed => Verdict.Failed,
            StepOutcome.NotDone => Verdict.Inconclusive,
            _ => Verdict.Passed,
        };
        return new StepRunResult(verdict, results, folder, stoppedBy, stoppedByTester);
    }

    private Task PauseAsync(CancellationToken ct) => pause > TimeSpan.Zero ? Task.Delay(pause, ct) : Task.CompletedTask;

    // Ten readings over the wait: every 0.5 s for 5 s.
    private TimeSpan ReadEvery => _wait / 10;

    // One step: read, find with the model, act or check, save the screenshot.
    private sealed class Run(
        StepRunner runner, IAppAdapter adapter, AppTarget target, int number, StepCommand step, string folder,
        Func<string, Task<bool>>? approveRisky, FrontWindow? asksIn, Run? opener, IProgress<StepActivity>? activity, CancellationToken ct)
    {
        private StepCommand Step => step;

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
                Tell(StepPhase.Reading);
                _screen = await ReadAsync().ConfigureAwait(false);
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
            catch (InvalidKeyComboException e)
            {
                var hint = step is PressStep { Keys: var keys } && !StepParser.IsCombo(keys)
                    ? $" To press something on the screen, write click {keys}."
                    : "";
                (outcome, message) = (StepOutcome.NotDone, e.Message + hint);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException or UnreadableAnswerException
                or InputRejectedException or ScreenUnavailableException)
            {
                (outcome, message) = (StepOutcome.NotDone, e.Message);
            }

            var screenshot = await SaveAsync().ConfigureAwait(false);
            return new StepResult(number, step, outcome, message, screenshot, _clock.Elapsed);
        }

        private void Tell(StepPhase phase) => activity?.Report(new StepActivity(number, step, phase));

        private async Task<(StepOutcome, string)> DoStepAsync()
        {
            switch (step)
            {
                case ClickStep click:
                    var button = await UsableAsync(click.Target).ConfigureAwait(false);
                    if (approveRisky is not null && RiskyWords.IsRisky(button.Name))
                    {
                        await AskAsync($"click {ElementFinder.Describe(button)}", $"clicking {ElementFinder.Describe(button)}").ConfigureAwait(false);
                        button = await AfterAskingAsync(button).ConfigureAwait(false);
                    }

                    Tell(StepPhase.Acting);
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

                    var names = SecretPlaceholder.FindNames(type.Text);
                    if (names.FirstOrDefault(n => runner.ApiKeyNames.Contains(n, StringComparer.OrdinalIgnoreCase)) is { } apiKey)
                    {
                        throw new StepNotDoneException($"\"{apiKey}\" is VERA's model API key, not a test secret. Save the app's own secret in Settings under another name.");
                    }

                    var used = new UsedSecrets(runner.Secrets);
                    var text = await SecretPlaceholder.ResolveAsync(type.Text, used, ct).ConfigureAwait(false);
                    if (names.Count > 0)
                    {
                        runner.Trail.Typed(ProcessId, field, used.Values);
                    }
                    else
                    {
                        runner.Trail.Forget(ProcessId, field);
                    }

                    Tell(StepPhase.Acting);
                    await adapter.TypeAsync(field.Id, text, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done, $"typed into {ElementFinder.Describe(field)} (element {field.Id})");

                case PressStep press:
                    if (approveRisky is not null && RiskyWords.IsRiskyKey(press.Keys))
                    {
                        await AskAsync($"press {press.Keys}", $"pressing {press.Keys}").ConfigureAwait(false);
                    }

                    Tell(StepPhase.Acting);
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
                    Tell(StepPhase.Acting);
                    await adapter.ScrollAsync(area.Id, down, right, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done,
                        $"scrolled {scroll.Direction.ToString().ToLowerInvariant()} {scroll.Notches} in {(wholeWindow ? "the window" : $"{ElementFinder.Describe(area)} (element {area.Id})")}");

                case ScrollUntilStep until:
                    return await ScrollUntilAsync(until).ConfigureAwait(false);

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
                    if (approveRisky is not null && RiskyWords.IsRisky(drop.Name))
                    {
                        await AskAsync($"drop onto {ElementFinder.Describe(drop)}", $"dropping onto {ElementFinder.Describe(drop)}").ConfigureAwait(false);
                        dragged = StillThere(dragged);
                        drop = StillThere(drop);
                    }

                    Tell(StepPhase.Acting);
                    await adapter.DragAsync(dragged.Id, drop.Id, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done,
                        $"dragged {ElementFinder.Describe(dragged)} (element {dragged.Id}) to {ElementFinder.Describe(drop)} (element {drop.Id})");

                case HoverStep hover:
                    var hovered = await FoundAsync(hover.Target).ConfigureAwait(false);
                    Tell(StepPhase.Acting);
                    await adapter.HoverAsync(hovered.Id, ct).ConfigureAwait(false);
                    return (StepOutcome.Done, $"hovered over {ElementFinder.Describe(hovered)} (element {hovered.Id})");

                case ExpectShownStep shown:
                    if (ExpectGoneStep.Problem(shown) is { } problem)
                    {
                        throw new StepNotDoneException(problem);
                    }

                    var (seen, notSeen) = await WaitForAsync(shown.Target, _ => true, care: ElementFinder.Care.MustMatch).ConfigureAwait(false);
                    return seen is null
                        ? (StepOutcome.CheckFailed, notSeen)
                        : (StepOutcome.Done, $"{ElementFinder.Describe(seen)} is shown");

                case ExpectGoneStep gone:
                    return await IsGoneAsync(gone.Target).ConfigureAwait(false);

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

        // Asks in VERA's own window, so the tester only has to type the answer, then reads the app again: what was there before may be gone.
        private async Task AskAsync(string what, string doing)
        {
            if (runner.Front is { } front && asksIn is not null)
            {
                await front.BringBackAsync(asksIn, ct).ConfigureAwait(false);
            }

            if (!await approveRisky!(what).ConfigureAwait(false))
            {
                throw new StepNotDoneException($"Stopped: {doing} wasn't approved.");
            }

            Tell(StepPhase.Reading);
            _screen = await ReadAsync().ConfigureAwait(false);
            await runner.PauseAsync(ct).ConfigureAwait(false);
            _screen = await ReadAsync().ConfigureAwait(false);
        }

        // A menu closes when its app is no longer in front. Then what opened it is done once more, but only for a menu:
        // doing a step twice could otherwise do something twice, e.g. put two of a thing in a cart.
        private async Task<UiElement> AfterAskingAsync(UiElement element)
        {
            if (element.Role == "MenuItem" && ElementFinder.Same(_screen!.Elements, element) is null
                && opener?._element is { } opened && ElementFinder.Same(_screen.Elements, opened) is { IsEnabled: true } again && !RiskyWords.IsRisky(again.Name))
            {
                if (opener.Step is HoverStep)
                {
                    await adapter.HoverAsync(again.Id, ct).ConfigureAwait(false);
                }
                else
                {
                    await adapter.ClickAsync(again.Id, ct).ConfigureAwait(false);
                }

                await runner.PauseAsync(ct).ConfigureAwait(false);
                _clock.Restart();
                do
                {
                    _screen = await ReadAsync().ConfigureAwait(false);
                }
                while (ElementFinder.Same(_screen.Elements, element) is null && await ReadAgainAsync().ConfigureAwait(false));
            }

            return StillThere(element);
        }

        private UiElement StillThere(UiElement element)
        {
            _element = ElementFinder.Same(_screen!.Elements, element);
            return _element is { IsEnabled: true }
                ? _element
                : throw new StepNotDoneException($"{ElementFinder.Describe(element)} isn't there any more after the question, so VERA did nothing.");
        }

        // The opposite of waiting for an element: waits while it's still there. The model is asked only when the elements changed.
        private async Task<(StepOutcome, string)> IsGoneAsync(string description)
        {
            // On a page that is still opening, nothing is there yet: that isn't gone.
            await SettleAsync().ConfigureAwait(false);
            string? asked = null;
            do
            {
                if (_screen!.Elements.Count <= 1)
                {
                    throw new StepNotDoneException(ElementFinder.NoElementsReason);
                }

                var seen = ElementFinder.Seen(_screen);
                if (seen != asked)
                {
                    asked = seen;
                    Tell(StepPhase.Finding);
                    (_element, _) = await ElementFinder.FindAsync(runner.Llm, _screen, target, description, ct, ElementFinder.Care.MustBeGone).ConfigureAwait(false);
                    if (_element is null)
                    {
                        return (StepOutcome.Done, $"VERA sees no {description} on this screen");
                    }
                }
            }
            while (await ReadAgainAsync().ConfigureAwait(false));

            return (StepOutcome.CheckFailed, $"{ElementFinder.Describe(_element!)} is still shown.");
        }

        // Looks, scrolls the window a little and looks again, until the model sees the element, the page ends, or MaxScrolls.
        // The model is asked only when the elements changed.
        private async Task<(StepOutcome, string)> ScrollUntilAsync(ScrollUntilStep until)
        {
            var direction = until.Direction.ToString().ToLowerInvariant();
            var vertical = until.Direction is ScrollDirection.Up or ScrollDirection.Down;
            var sign = until.Direction is ScrollDirection.Down or ScrollDirection.Right ? 1 : -1;
            var notches = FirstNotches;
            string? asked = null;
            var reason = "";
            await SettleAsync().ConfigureAwait(false);
            for (var scrolls = 0; ; scrolls++)
            {
                if (_screen!.Elements.Count == 0)
                {
                    throw new StepNotDoneException(ElementFinder.NoElementsReason);
                }

                var before = _screen;
                var seen = ElementFinder.Seen(before);
                if (seen != asked)
                {
                    asked = seen;
                    Tell(StepPhase.Finding);
                    (_element, reason) = await ElementFinder.FindAsync(runner.Llm, before, target, until.Target, ct, care: ElementFinder.Care.MustMatch).ConfigureAwait(false);
                    if (_element is not null)
                    {
                        var times = scrolls == 0 ? "without scrolling" : $"after scrolling {direction} {Times(scrolls)}";
                        return (StepOutcome.Done, $"found {ElementFinder.Describe(_element)} {times} (element {_element.Id})");
                    }
                }

                if (scrolls == MaxScrolls)
                {
                    throw new StepNotDoneException($"I scrolled {direction} {Times(scrolls)} and can't find {StepCommand.Quote(until.Target)}.{Because(reason)}");
                }

                await ScrollWindowAsync(sign * notches, vertical).ConfigureAwait(false);

                // Same elements in the same places, and the same picture: there is nothing more that way.
                if (ElementFinder.Seen(_screen) == seen && _screen.ScreenshotPng.Span.SequenceEqual(before.ScreenshotPng.Span))
                {
                    throw new StepNotDoneException(scrolls == 0
                        ? $"I can't find {StepCommand.Quote(until.Target)}, and the window doesn't scroll {direction}.{Because(reason)}"
                        : $"I scrolled {direction} to the end and can't find {StepCommand.Quote(until.Target)}.{Because(reason)}");
                }

                // Jumped past a part nobody has seen: go half of the way back, then in smaller steps.
                if (notches > 1 && ScrollDistance.Jumped(before, _screen, vertical))
                {
                    var back = notches - (notches / 2);
                    await ScrollWindowAsync(-sign * back, vertical).ConfigureAwait(false);
                    notches -= back;
                    continue;
                }

                notches = ScrollDistance.Next(before, _screen, vertical, sign > 0, notches);
            }
        }

        private async Task ScrollWindowAsync(int notches, bool vertical)
        {
            Tell(StepPhase.Acting);
            await adapter.ScrollAsync(0, vertical ? notches : 0, vertical ? 0 : notches, ct).ConfigureAwait(false);
            await runner.PauseAsync(ct).ConfigureAwait(false);
            _screen = await ReadAsync().ConfigureAwait(false);
        }

        // A page that is still opening would be scrolled before its top is there. Waits until two readings show the same; no model call.
        private async Task SettleAsync()
        {
            string before;
            do
            {
                before = ElementFinder.Seen(_screen!);
            }
            while (await ReadAgainAsync().ConfigureAwait(false) && ElementFinder.Seen(_screen!) != before);
        }

        private static string Because(string reason) => reason.Length == 0 ? "" : $" The model's last answer: {reason}";

        private static string Times(int count) => count == 1 ? "once" : string.Create(CultureInfo.InvariantCulture, $"{count} times");

        // The element the model picks for the description, waiting for the app until it's there and ready; or why there is none.
        // The model is asked only when the window's elements changed since it was last asked; the element it found is followed without it.
        private async Task<(UiElement? Element, string NotFound)> WaitForAsync(string description, Func<UiElement, bool> ready, ElementFinder.Care care = ElementFinder.Care.Act)
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
                    Tell(StepPhase.Finding);
                    (element, reason) = await ElementFinder.FindAsync(runner.Llm, _screen, target, description, ct, care).ConfigureAwait(false);
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

            Tell(StepPhase.Waiting);
            await Task.Delay(runner.ReadEvery, ct).ConfigureAwait(false);
            _screen = await ReadAsync().ConfigureAwait(false);
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

        // Every reading goes through here: a secret typed earlier in the run is taken out before the model, a message or a screenshot sees it.
        private async Task<ScreenState> ReadAsync() => runner.Trail.Hide(ProcessId, await adapter.GetScreenStateAsync(ct).ConfigureAwait(false));

        private int ProcessId => target.ProcessId ?? 0;

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

    private IFrontWindow? Front => front;

    private IReadOnlyCollection<string> ApiKeyNames => apiKeyNames ?? [];

    private SecretTrail Trail { get; } = trail ?? new SecretTrail();

    // Reads each secret once and keeps what it read, so the trail learns the values without asking the store again.
    private sealed class UsedSecrets(ISecretStore store) : ISecretStore
    {
        public List<string> Values { get; } = [];

        public async Task<string?> GetAsync(string name, CancellationToken ct)
        {
            var value = await store.GetAsync(name, ct).ConfigureAwait(false);
            if (value is not null)
            {
                Values.Add(value);
            }

            return value;
        }

        public Task SetAsync(string name, string value, CancellationToken ct) => throw new NotSupportedException();
    }

    // A step VERA can't do; the message is shown to the tester.
    private sealed class StepNotDoneException(string message) : Exception(message);
}
