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
/// </summary>
/// <param name="pause">How long the app gets to react after each click, type or key press.</param>
public sealed class StepRunner(Func<IAppAdapter> newAdapter, ILlmClient llm, ISecretStore secrets, string outputDirectory, TimeSpan pause)
{
    /// <summary>The pause after each action in the app.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromSeconds(1);

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

    // One step: read, find with the model, act or check, save the screenshot.
    private sealed class Run(
        StepRunner runner, IAppAdapter adapter, AppTarget target, int number, StepCommand step, string folder,
        Func<UiElement, Task<bool>>? approveRisky, CancellationToken ct)
    {
        private ScreenState? _screen;
        private UiElement? _element;

        public async Task<StepResult> ExecuteAsync()
        {
            StepOutcome outcome;
            string message;
            try
            {
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
                    var text = await SecretPlaceholder.ResolveAsync(type.Text, runner.Secrets, ct).ConfigureAwait(false);
                    await adapter.TypeAsync(field.Id, text, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done, $"typed into {ElementFinder.Describe(field)} (element {field.Id})");

                case PressStep press:
                    await adapter.PressKeyAsync(press.Keys, ct).ConfigureAwait(false);
                    await runner.PauseAsync(ct).ConfigureAwait(false);
                    return (StepOutcome.Done, $"pressed {press.Keys}");

                case ExpectShownStep shown:
                    var (seen, notSeen) = await FindAsync(shown.Target).ConfigureAwait(false);
                    return seen is null
                        ? (StepOutcome.CheckFailed, notSeen)
                        : (StepOutcome.Done, $"{ElementFinder.Describe(seen)} is shown");

                case ExpectContainsStep contains:
                    var (holder, missing) = await FindAsync(contains.Target).ConfigureAwait(false);
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
                    var windowTitle = _screen!.WindowTitle;
                    return windowTitle.Contains(title.Text, StringComparison.OrdinalIgnoreCase)
                        ? (StepOutcome.Done, $"the title contains {StepCommand.Quote(title.Text)}")
                        : (StepOutcome.CheckFailed, $"The title is {StepCommand.Quote(windowTitle)}, which doesn't contain {StepCommand.Quote(title.Text)}.");

                default:
                    throw new NotSupportedException($"VERA can't run \"{step}\" yet.");
            }
        }

        // The element the model picks for the description, or why there is none.
        private async Task<(UiElement? Element, string NotFound)> FindAsync(string description)
        {
            var (element, reason) = await ElementFinder.FindAsync(runner.Llm, _screen!, target, description, ct).ConfigureAwait(false);
            _element = element;
            var notFound = $"I can't find {StepCommand.Quote(description)} on this screen.";
            return (element, reason.Length == 0 ? notFound : $"{notFound} {reason}");
        }

        // The element to click or type into: found, and enabled.
        private async Task<UiElement> UsableAsync(string description)
        {
            var (element, notFound) = await FindAsync(description).ConfigureAwait(false);
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
