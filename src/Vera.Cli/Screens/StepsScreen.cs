using Spectre.Console;
using Vera.Application;
using Vera.Core;
#if WINDOWS
using Vera.Adapters.Desktop;
#endif

namespace Vera.Cli.Screens;

/// <summary>
/// Write a chain of steps for the selected app and run it. Each command decides its action; the model finds each element, as in Find.
/// VERA runs the steps in order without asking anything in between (except risky clicks, if Settings says so) and ends with a verdict.
/// Steps can be saved as text files and loaded again (<see cref="StepFileStore"/>), and on Windows recorded while the tester does them (#48).
/// </summary>
internal static class StepsScreen
{
    private const string BackCommand = "/back";
    private const string ClearCommand = "/clear";

    // The Back entry in the list of saved steps. "<" can't be in a saved name.
    private const string BackChoice = "<back>";

    private enum Start
    {
        Write,
        Record,
        Load,
        Back,
    }

    private enum Ready
    {
        Run,
        AddMore,
        Save,
        Back,
    }

    private enum Next
    {
        RunAgain,
        Save,
        Other,
        Back,
    }

    private enum SaveAs
    {
        Same,
        New,
        Back,
    }

    public static async Task ShowAsync(AppSettings settings, RunningApp app, StepRunner runner, StepFileStore store)
    {
        var chain = await ChooseAsync(settings, app, store);
        while (chain is not null)
        {
            await RunAsync(settings, app, runner, chain.Steps);
            chain = await AfterRunAsync(settings, app, store, chain);
        }
    }

    // Write new steps, record them or load saved ones, then save or add to them until the tester runs them; null to go back to the main menu.
    private static async Task<Chain?> ChooseAsync(AppSettings settings, RunningApp app, StepFileStore store)
    {
        while (true)
        {
            var saved = store.List();
            List<Start> choices = [Start.Write];
#if WINDOWS
            choices.Add(Start.Record);
#endif
            if (saved.Count > 0)
            {
                choices.Add(Start.Load);
            }

            choices.Add(Start.Back);

            // With only Write and Back there is nothing to choose.
            var hasMenu = choices.Count > 2;
            Chain? chain;
            if (!hasMenu)
            {
                chain = new Chain([]);
            }
            else
            {
                ShowTop(settings, app);
                var start = AnsiConsole.Prompt(
                    new SelectionPrompt<Start>()
                        .Title("What do you want to run?")
                        .HighlightStyle(Ui.Highlight)
                        .AddChoices(choices)
                        .UseConverter(s => s switch
                        {
                            Start.Write => "Write new steps",
                            Start.Record => "Record steps by doing them",
                            Start.Load => $"Load saved steps ({saved.Count})",
                            _ => "Back to the main menu",
                        }));
                if (start == Start.Back)
                {
                    return null;
                }

                chain = start switch
                {
                    Start.Load => Load(settings, app, store, saved),
#if WINDOWS
                    Start.Record => await RecordAsync(settings, app),
#endif
                    _ => new Chain([]),
                };
                if (chain is null)
                {
                    continue;
                }
            }

            if (chain.Steps.Count == 0 && !WriteSteps(settings, app, chain.Steps))
            {
                // Without a start menu there is nothing to go back to.
                if (!hasMenu)
                {
                    return null;
                }

                continue;
            }

            return Review(settings, app, store, chain) ? chain : null;
        }
    }

    // Asks what to do with the steps until the tester runs them (true) or goes back to the main menu (false).
    private static bool Review(AppSettings settings, RunningApp app, StepFileStore store, Chain chain)
    {
        while (true)
        {
            AnsiConsole.WriteLine();
            var named = chain.Name is null ? "" : $" ([teal]{Markup.Escape(chain.Name)}[/])";
            var ready = AnsiConsole.Prompt(
                new SelectionPrompt<Ready>()
                    .Title($"{chain.Steps.Count} steps ready{named}. What next?")
                    .HighlightStyle(Ui.Highlight)
                    .AddChoices(Enum.GetValues<Ready>())
                    .UseConverter(r => r switch
                    {
                        Ready.Run => $"Run them in {Markup.Escape(app.Name)}",
                        Ready.AddMore => "Add more steps",
                        Ready.Save => "Save these steps",
                        _ => "Back to the main menu",
                    }));

            switch (ready)
            {
                case Ready.Run:
                    return true;
                case Ready.AddMore:
                    // /back here only stops adding; the steps stay.
                    WriteSteps(settings, app, chain.Steps);
                    break;
                case Ready.Save:
                    Save(store, chain);
                    break;
                default:
                    return false;
            }

            if (chain.Steps.Count == 0)
            {
                // Cleared with /clear and nothing written since: start over.
                if (!WriteSteps(settings, app, chain.Steps))
                {
                    return false;
                }
            }
        }
    }

    // After a run: run again, save, pick other steps or go back. Returns the steps to run next, or null for the main menu.
    private static async Task<Chain?> AfterRunAsync(AppSettings settings, RunningApp app, StepFileStore store, Chain chain)
    {
        while (true)
        {
            var next = AnsiConsole.Prompt(
                new SelectionPrompt<Next>()
                    .Title("What next?")
                    .HighlightStyle(Ui.Highlight)
                    .AddChoices(Enum.GetValues<Next>())
                    .UseConverter(n => n switch
                    {
                        Next.RunAgain => "Run the same steps again",
                        Next.Save => "Save these steps",
                        Next.Other => "Other steps",
                        _ => "Back to the main menu",
                    }));

            switch (next)
            {
                case Next.RunAgain:
                    return chain;
                case Next.Save:
                    Save(store, chain);
                    AnsiConsole.WriteLine();
                    break;
                case Next.Other:
                    return await ChooseAsync(settings, app, store);
                default:
                    return null;
            }
        }
    }

    // Picks a saved file and reads it; null when the tester goes back or the file can't be read.
    private static Chain? Load(AppSettings settings, RunningApp app, StepFileStore store, IReadOnlyList<string> saved)
    {
        ShowTop(settings, app);
        AnsiConsole.MarkupLine($"[grey]Saved in {Markup.Escape(store.Folder)}[/]");
        var name = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which steps?")
                .HighlightStyle(Ui.Highlight)
                .PageSize(10)
                .MoreChoicesText("[grey](Move up and down to see more)[/]")
                .AddChoices(saved)
                .AddChoices(BackChoice)
                .UseConverter(n => n == BackChoice ? "[grey]Back[/]" : Markup.Escape(n)));
        if (name == BackChoice)
        {
            return null;
        }

        try
        {
            var chain = new Chain([.. store.Load(name)]) { Name = name };
            ShowTop(settings, app);
            AnsiConsole.MarkupLine($"[grey]Loaded[/] [teal]{Markup.Escape(name)}[/][grey]:[/]");
            ShowSteps(chain.Steps);
            return chain;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            Ui.ShowErrorPanel($"Could not load {name}: {e.Message}");
            Ui.Pause();
            return null;
        }
    }

    // Saves under the name the steps were loaded or saved as, or a new one. Every question here can go back without saving.
    private static void Save(StepFileStore store, Chain chain)
    {
        var name = chain.Name;
        if (name is not null)
        {
            var saveAs = AnsiConsole.Prompt(
                new SelectionPrompt<SaveAs>()
                    .Title("Save as?")
                    .HighlightStyle(Ui.Highlight)
                    .AddChoices(Enum.GetValues<SaveAs>())
                    .UseConverter(s => s switch
                    {
                        SaveAs.Same => $"Save over {Markup.Escape(name)}",
                        SaveAs.New => "Save with a new name",
                        _ => "Back",
                    }));
            if (saveAs == SaveAs.Back)
            {
                return;
            }

            if (saveAs == SaveAs.New)
            {
                name = null;
            }
        }

        if (name is null)
        {
            name = AnsiConsole.Prompt(
                new TextPrompt<string>("Name for these steps [grey](letters, digits, spaces, _ -; empty to go back)[/]:")
                    .AllowEmpty()
                    .Validate(n => n.Trim().Length == 0 || StepFileStore.IsValidName(n.Trim())
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Use only letters, digits, spaces, _ and -, e.g. log in.[/]"))).Trim();
            if (name.Length == 0)
            {
                return;
            }

            if (store.Exists(name) && !AnsiConsole.Confirm($"[teal]{Markup.Escape(name)}[/] already exists. Replace it?", defaultValue: false))
            {
                return;
            }
        }

        try
        {
            var path = store.Save(name, chain.Steps);
            chain.Name = name;
            AnsiConsole.MarkupLine($"[green]Saved {chain.Steps.Count} steps as {Markup.Escape(name)}.[/] [grey]{Markup.Escape(path)}[/]");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Ui.ShowErrorPanel($"Could not save {name}: {e.Message}");
        }
    }

    // Adds the steps the tester writes, checked line by line, after those already in the list.
    // True when an empty line ends a list that has steps; false for /back.
    private static bool WriteSteps(AppSettings settings, RunningApp app, List<StepCommand> steps)
    {
        ShowTop(settings, app);
        ShowHelp();
        ShowSteps(steps);

        while (true)
        {
            var line = AnsiConsole.Prompt(new TextPrompt<string>($"[bold teal]Step {steps.Count + 1} >[/]").AllowEmpty()).Trim();
            if (line.Equals(BackCommand, StringComparison.OrdinalIgnoreCase))
            {
                return false;
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
                    return true;
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
    }

#if WINDOWS
    // Watches the tester do the steps in the app until they press Enter here, showing each step as it's written. Null when nothing was recorded.
    private static async Task<Chain?> RecordAsync(AppSettings settings, RunningApp app)
    {
        ShowTop(settings, app);
        AnsiConsole.MarkupLine($"Do the steps in [teal]{Markup.Escape(app.Name)}[/]: click, type and press keys as usual.");
        AnsiConsole.MarkupLine("[grey]Then come back to this window and press Enter to stop.[/]");
        AnsiConsole.WriteLine();

        var steps = new List<StepCommand>();
        using var stop = new CancellationTokenSource();
        var enter = Task.Run(() =>
        {
            Console.ReadLine();
            stop.Cancel();
        });

        try
        {
            await foreach (var action in StepRecorder.RecordAsync(app.Id, stop.Token))
            {
                if (action is RecordedSkip skip)
                {
                    AnsiConsole.MarkupLine($"[yellow]![/] [grey]{Markup.Escape(skip.Reason)}[/]");
                    continue;
                }

                var step = ToStep(action);
                steps.Add(step);
                AnsiConsole.MarkupLine($"[grey]Step {steps.Count} >[/] {Markup.Escape(step.ToString())}");
            }
        }
        catch (InvalidOperationException e)
        {
            Ui.ShowErrorPanel(e.Message);
            AnsiConsole.MarkupLine("[grey]Press Enter to go back.[/]");
        }

        await enter;
        if (steps.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Nothing was recorded.[/]");
            Ui.Pause();
            return null;
        }

        ShowTop(settings, app);
        AnsiConsole.MarkupLine("[grey]Recorded:[/]");
        ShowSteps(steps);
        return new Chain(steps);
    }

    // Elements are written the way Find describes them, e.g. button "Save", so the model finds them again.
    private static StepCommand ToStep(RecordedAction action) => action switch
    {
        RecordedClick c => new ClickStep(Describe(c.Role, c.Name)),
        RecordedType t => new TypeStep(t.Text, Describe(t.Role, t.Name)),
        RecordedPress p => new PressStep(p.Keys),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not a step."),
    };

    private static string Describe(string role, string name) =>
        ElementFinder.Describe(new UiElement(0, role, name, new ScreenRect(0, 0, 0, 0), IsEnabled: true, Locator: ""));
#endif

    private static void ShowSteps(List<StepCommand> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            AnsiConsole.MarkupLine($"[grey]Step {i + 1} >[/] {Markup.Escape(steps[i].ToString())}");
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

    // Asked before clicking, or dropping onto, an element with a risky name. The app is in front, so the tester has to come back to this window first.
    private static bool ApproveRisky(UiElement element)
    {
        AnsiConsole.MarkupLine($"[yellow]The next click or drop is on \"{Markup.Escape(element.Name)}\", which may change or send something.[/]");
        return AnsiConsole.Confirm("[yellow]Go ahead?[/] [grey](click back into this window to answer)[/]", defaultValue: false);
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
        table.AddRow("scroll <up|down|left|right> <notches> in <what>", Markup.Escape("scroll down 3 in the results list"));
        table.AddRow("drag <what> to <where>", Markup.Escape("drag the report file to the Documents folder"));
        table.AddRow("hover over <what>", Markup.Escape("hover over the help icon"));
        table.AddRow("expect <what>", Markup.Escape("expect the results list"));
        table.AddRow("expect <what> contains \"<text>\"", Markup.Escape("expect the search box contains \"cats\""));
        table.AddRow("expect title contains \"<text>\"", Markup.Escape("expect title contains \"cats\""));
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(Markup.Escape("For a password, save it in Settings and type \"{{secret:name}}\"."));
        AnsiConsole.MarkupLine(
            $"[grey]One step per line. An empty line ends the list, then you can run or save it. [teal]{ClearCommand}[/] starts over, [teal]{BackCommand}[/] goes back.[/]");
        AnsiConsole.WriteLine();
    }

    // The steps being written or run, and the name they were loaded or saved as.
    private sealed class Chain(List<StepCommand> steps)
    {
        public List<StepCommand> Steps { get; } = steps;

        public string? Name { get; set; }
    }

    // Shows each step the moment it's finished, in order.
    private sealed class LineProgress(Action<StepResult> show) : IProgress<StepResult>
    {
        public void Report(StepResult value) => show(value);
    }
}
