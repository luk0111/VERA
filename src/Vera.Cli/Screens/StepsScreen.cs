using Spectre.Console;
using Vera.Application;
using Vera.Core;

namespace Vera.Cli.Screens;

/// <summary>
/// Write a chain of steps for the selected app and run it. Each command decides its action; the model finds each element, as in Find.
/// VERA runs the steps in order without asking anything in between (except risky clicks, if Settings says so) and ends with a verdict.
/// </summary>
internal static class StepsScreen
{
    private const string BackCommand = "/back";
    private const string ClearCommand = "/clear";

    private enum Next
    {
        RunAgain,
        NewSteps,
        Back,
    }

    public static async Task ShowAsync(AppSettings settings, RunningApp app, StepRunner runner)
    {
        var steps = WriteSteps(settings, app);
        while (steps is not null)
        {
            await RunAsync(settings, app, runner, steps);

            var next = AnsiConsole.Prompt(
                new SelectionPrompt<Next>()
                    .Title("What next?")
                    .HighlightStyle(Ui.Highlight)
                    .AddChoices(Enum.GetValues<Next>())
                    .UseConverter(n => n switch
                    {
                        Next.RunAgain => "Run the same steps again",
                        Next.NewSteps => "Write new steps",
                        _ => "Back to the main menu",
                    }));

            steps = next switch
            {
                Next.RunAgain => steps,
                Next.NewSteps => WriteSteps(settings, app),
                _ => null,
            };
        }
    }

    // The steps the tester writes, checked line by line; null to go back.
    private static List<StepCommand>? WriteSteps(AppSettings settings, RunningApp app)
    {
        while (true)
        {
            ShowTop(settings, app);
            ShowHelp();

            var steps = new List<StepCommand>();
            while (true)
            {
                var line = AnsiConsole.Prompt(new TextPrompt<string>($"[bold teal]Step {steps.Count + 1} >[/]").AllowEmpty()).Trim();
                if (line.Equals(BackCommand, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (line.Equals(ClearCommand, StringComparison.OrdinalIgnoreCase))
                {
                    steps.Clear();
                    AnsiConsole.MarkupLine("[grey]Cleared. Start again from step 1.[/]");
                    continue;
                }

                if (line.Length == 0)
                {
                    if (steps.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                try
                {
                    if (StepParser.Parse(line) is { } step)
                    {
                        steps.Add(step);
                    }
                }
                catch (FormatException e)
                {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Message)}[/]");
                }
            }

            if (AnsiConsole.Confirm($"Run these {steps.Count} steps in [teal]{Markup.Escape(app.Name)}[/]?", defaultValue: true))
            {
                return steps;
            }
        }
    }

    private static async Task RunAsync(AppSettings settings, RunningApp app, StepRunner runner, List<StepCommand> steps)
    {
        ShowTop(settings, app);
        AnsiConsole.MarkupLine($"[grey]Running {steps.Count} steps. VERA brings the app to the front: don't use the mouse or keyboard until it's done.[/]");
        if (settings.IsLocalModel)
        {
            AnsiConsole.MarkupLine("[grey]With a local model, finding each element can take a few minutes.[/]");
        }

        AnsiConsole.WriteLine();

        Func<UiElement, Task<bool>>? approveRisky = settings.AskBeforeRiskyActions ? element => Task.FromResult(ApproveRisky(element)) : null;
        StepRunResult result;
        try
        {
            result = await runner.RunAsync(app.Id, app.WindowTitle, steps, approveRisky, new LineProgress(ShowStep), CancellationToken.None);
        }
#pragma warning disable CA1031 // A run that can't start (e.g. the app closed) is shown on screen; the screen itself must stay open.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Ui.ShowError(settings, e);
            return;
        }

        if (result.StoppedBy is { } error)
        {
            AnsiConsole.MarkupLine($"[yellow]![/] {result.Steps.Count + 1}. {Markup.Escape(steps[result.Steps.Count].ToString())}");
            AnsiConsole.MarkupLine($"    [yellow]Stopped here: VERA couldn't finish this step.[/]");
            Ui.ShowError(settings, error);
        }

        AnsiConsole.WriteLine();
        var done = result.Steps.Count(s => s.Outcome == StepOutcome.Done);
        var verdict = result.Verdict switch
        {
            Verdict.Passed => "[green bold]Passed[/]",
            Verdict.Failed => "[red bold]Failed[/]",
            _ => "[yellow bold]Inconclusive[/]",
        };
        AnsiConsole.MarkupLine($"{verdict}: {done} of {steps.Count} steps done.");
        AnsiConsole.MarkupLine($"[grey]Screenshots: {Markup.Escape(result.Folder)}[/]");
        AnsiConsole.MarkupLine("[grey]Click back into this window to continue.[/]");
        AnsiConsole.WriteLine();
    }

    private static void ShowStep(StepResult step)
    {
        var (mark, color) = step.Outcome switch
        {
            StepOutcome.Done => ("✓", "green"),
            StepOutcome.CheckFailed => ("✗", "red"),
            _ => ("!", "yellow"),
        };
        AnsiConsole.MarkupLine($"[{color}]{mark}[/] {step.Number}. {Markup.Escape(step.Step.ToString())}");
        AnsiConsole.MarkupLine($"    [{(step.Outcome == StepOutcome.Done ? "grey" : color)}]{Markup.Escape(step.Message)}[/]");
    }

    // Asked before clicking an element with a risky name. The app is in front, so the tester has to come back to this window first.
    private static bool ApproveRisky(UiElement element)
    {
        AnsiConsole.MarkupLine($"[yellow]The next click is on \"{Markup.Escape(element.Name)}\", which may change or send something.[/]");
        return AnsiConsole.Confirm("[yellow]Click it?[/] [grey](click back into this window to answer)[/]", defaultValue: false);
    }

    private static void ShowTop(AppSettings settings, RunningApp app)
    {
        Ui.ShowHeader(settings);
        Ui.Title("Run steps in an app");
        AnsiConsole.MarkupLine($"[grey]Process:[/] [teal]{Markup.Escape(app.ToString())}[/]");
        AnsiConsole.MarkupLine($"[grey]Model:[/] [teal]{Markup.Escape(settings.ModelLabel)}[/] [grey](finds each element)[/]");
        AnsiConsole.WriteLine();
    }

    private static void ShowHelp()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumns("Step", "Example");
        table.AddRow("click <what>", Markup.Escape("click the search button"));
        table.AddRow("type \"<text>\" into <what>", Markup.Escape("type \"cats\" into the search box"));
        table.AddRow("press <keys>", Markup.Escape("press enter, press ctrl+s"));
        table.AddRow("expect <what>", Markup.Escape("expect the results list"));
        table.AddRow("expect <what> contains \"<text>\"", Markup.Escape("expect the search box contains \"cats\""));
        table.AddRow("expect title contains \"<text>\"", Markup.Escape("expect title contains \"cats\""));
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(Markup.Escape("For a password, save it in Settings and type \"{{secret:name}}\"."));
        AnsiConsole.MarkupLine($"[grey]One step per line. An empty line runs them. [teal]{ClearCommand}[/] starts over, [teal]{BackCommand}[/] goes back.[/]");
        AnsiConsole.WriteLine();
    }

    // Shows each step the moment it's finished, in order.
    private sealed class LineProgress(Action<StepResult> show) : IProgress<StepResult>
    {
        public void Report(StepResult value) => show(value);
    }
}
