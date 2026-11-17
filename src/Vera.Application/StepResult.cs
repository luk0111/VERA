using Vera.Core;

namespace Vera.Application;

/// <summary>How one step went.</summary>
public enum StepOutcome
{
    /// <summary>The action was done, or the check held.</summary>
    Done,

    /// <summary>An expect check didn't hold: the app didn't do what the tester expected.</summary>
    CheckFailed,

    /// <summary>VERA couldn't do the step, e.g. no element with that name, the app closed, or the tester refused a risky click.</summary>
    NotDone,
}

/// <summary>One step of a run, as the tester sees it.</summary>
/// <param name="Number">The step's number, from 1.</param>
/// <param name="Step">The step as written; secret placeholders stay placeholders.</param>
/// <param name="Message">What happened, e.g. <c>typed into field "Search" (element 4)</c>.</param>
/// <param name="ScreenshotPath">The screen the step acted on, with its element boxed; null when the window couldn't be read.</param>
public sealed record StepResult(int Number, StepCommand Step, StepOutcome Outcome, string Message, string? ScreenshotPath);

/// <summary>A whole run: the steps up to the first one that wasn't done, and the verdict.</summary>
/// <param name="Verdict"><see cref="Verdict.Passed"/> when every step was done, <see cref="Verdict.Failed"/> when a check didn't hold,
/// <see cref="Verdict.Inconclusive"/> when VERA couldn't do a step.</param>
/// <param name="Folder">Where the run's screenshots are saved.</param>
/// <param name="StoppedBy">An error that ended the run partway, e.g. the model couldn't be reached; the verdict is then
/// <see cref="Verdict.Inconclusive"/> and <see cref="Steps"/> holds the steps finished before it. Null when the run wasn't stopped.</param>
public sealed record StepRunResult(Verdict Verdict, IReadOnlyList<StepResult> Steps, string Folder, Exception? StoppedBy = null);
