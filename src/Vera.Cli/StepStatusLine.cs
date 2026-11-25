using System.Diagnostics;
using System.Globalization;

using Spectre.Console;

using Vera.Application;

namespace Vera.Cli;

/// <summary>
/// Two lines under the finished steps that show the running step and what VERA is doing in it, with a spinner and a seconds counter (#59).
/// They're redrawn in place and cleared before anything else is written. Nothing is shown when the console can't move the cursor.
/// </summary>
internal sealed class StepStatusLine : IDisposable
{
    private const string Teal = "\e[36m";
    private const string Grey = "\e[90m";
    private const string Reset = "\e[0m";

    private static readonly string[] Spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private readonly int _count;
    private readonly bool _enabled = !Console.IsOutputRedirected && AnsiConsole.Profile.Capabilities.Ansi;
    private readonly Stopwatch _clock = new();
    private readonly Timer _timer;
    private StepActivity? _activity;
    private int _number;
    private int _frame;
    private bool _shown;

    /// <param name="count">How many steps the run has.</param>
    public StepStatusLine(int count)
    {
        _count = count;
        _timer = new Timer(_ => Redraw());
    }

    /// <summary>Shows what VERA is doing now. The counter starts again for each new step.</summary>
    public void Show(StepActivity activity)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_lock)
        {
            if (activity.Number != _number)
            {
                _number = activity.Number;
                _clock.Restart();
            }

            _activity = activity;
            Draw();
        }

        _timer.Change(Every, Every);
    }

    /// <summary>Removes the lines until the next <see cref="Show"/>, so a finished step, a question or the verdict can be written.</summary>
    public void Clear()
    {
        if (!_enabled)
        {
            return;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        lock (_lock)
        {
            _activity = null;
            Erase();
        }
    }

    public void Dispose()
    {
        Clear();
        _timer.Dispose();
    }

    /// <summary>How long a step took, as the finished line shows it: tenths under 10 s, whole seconds after.</summary>
    public static string Seconds(TimeSpan time) =>
        time.TotalSeconds < 10
            ? time.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : time.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s";

    /// <summary>What VERA is doing, in the tester's words.</summary>
    public static string Doing(StepActivity activity) => (activity.Phase, activity.Step) switch
    {
        (StepPhase.Reading, _) => "Reading the window…",
        (StepPhase.Finding, DragStep drag) => $"Asking the model to find {StepCommand.Quote(drag.Source)} and {StepCommand.Quote(drag.Destination)}…",
        (StepPhase.Finding, ExpectGoneStep gone) => $"Asking the model whether {StepCommand.Quote(gone.Target)} is still there…",
        (StepPhase.Finding, var step) => $"Asking the model to find {StepCommand.Quote(Target(step))}…",
        (StepPhase.Waiting, ExpectTitleStep) => "Waiting for the title…",
        (StepPhase.Waiting, ExpectContainsStep) => "Waiting for the text…",
        (StepPhase.Waiting, ExpectShownStep) => "Waiting for it to show…",
        (StepPhase.Waiting, ExpectGoneStep) => "Waiting for it to go…",
        (StepPhase.Waiting, _) => "Waiting for the app…",
        (_, ClickStep) => "Clicking…",
        (_, TypeStep) => "Typing…",
        (_, PressStep press) => $"Pressing {press.Keys}…",
        (_, ScrollStep or ScrollUntilStep) => "Scrolling…",
        (_, DragStep) => "Dragging…",
        (_, HoverStep) => "Hovering…",
        _ => "Checking…",
    };

    private static string Target(StepCommand step) => step switch
    {
        ClickStep s => s.Target,
        TypeStep s => s.Target,
        ScrollStep s => s.Target,
        ScrollUntilStep s => s.Target,
        HoverStep s => s.Target,
        ExpectShownStep s => s.Target,
        ExpectContainsStep s => s.Target,
        _ => "the element",
    };

    private void Redraw()
    {
        lock (_lock)
        {
            _frame++;
            Draw();
        }
    }

    // Called with the lock held.
    private void Draw()
    {
        if (_activity is not { } activity)
        {
            return;
        }

        var width = Width();
        var spinner = Spinner[_frame % Spinner.Length];
        var title = Cut($"Step {activity.Number} of {_count}: {activity.Step}", width - 2);
        var seconds = $" {(int)_clock.Elapsed.TotalSeconds} s";
        var doing = Cut(Doing(activity), width - 3 - seconds.Length);

        Erase();
        Console.Out.Write($"{Teal}{spinner}{Reset} {title}\n   {Grey}{doing}{seconds}{Reset}");
        Console.Out.Flush();
        _shown = true;
    }

    // Called with the lock held. Clears both lines and leaves the cursor at the start of the first.
    private void Erase()
    {
        if (!_shown)
        {
            return;
        }

        Console.Out.Write("\r\e[2K\e[1A\e[2K");
        Console.Out.Flush();
        _shown = false;
    }

    // One column spare, so a line never wraps (wrapping would break moving back up).
    private static int Width()
    {
        try
        {
            return Math.Max(20, Console.WindowWidth - 1);
        }
        catch (IOException)
        {
            return 79;
        }
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, Math.Max(0, max - 1)), "…");
}
