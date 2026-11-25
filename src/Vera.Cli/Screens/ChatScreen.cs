using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using Vera.Application;
using Vera.Core;
using Vera.Files;
using Vera.Llm;

namespace Vera.Cli.Screens;

/// <summary>
/// Talk with VERA about the selected app: ask about it, find elements, and have it propose steps that the tester then runs, edits or saves.
/// Replies stream in; the session remembers the conversation.
/// </summary>
internal static class ChatScreen
{
    private const string Title = "Start testing an app";
    private const string ExitCommand = "/back";
    private const string ClearCommand = "/clear";
    private const string RefreshCommand = "/refresh";
    private const string ElementsCommand = "/elements";
    private const string StepsCommand = "/steps";
    private const string FindCommand = "/find";

    private enum Proposed
    {
        NotNow,
        Run,
        Edit,
        Save,
    }

    private static readonly Color Neon = new(57, 255, 20);

    // Redrawing on every token flickers
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan WaitingTick = TimeSpan.FromSeconds(1);

    // From here on the wait is longer than usual, so it says what may be going on and how to stop.
    private static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(10);

    public static async Task ShowAsync(AppSettings settings, RunningApp app, ChatSession session, ChatAppContextReader appReader, FindElementService find, StepRunner runner, StepFileStore store)
    {
        // The model was checked before the process list (Ui.EnsureModelReadyAsync), so reading the window is worth it.
        Ui.ShowHeader(settings);
        Ui.Title(Title);
        await ReadAppAsync(app, session, appReader);

        Ui.ShowHeader(settings);
        Ui.Title(Title);
        AnsiConsole.Write(SessionPanel(settings, app, session));
        AnsiConsole.MarkupLine(
            $"[grey]Say what to test, e.g. [teal]check that searching works[/], and VERA proposes the steps. [teal]find the search button[/] shows where it is, [teal]{StepsCommand}[/] writes, records or loads steps.[/]");
        AnsiConsole.MarkupLine(
            $"[grey]Type [teal]{RefreshCommand}[/] after the app's window changes, [teal]{ElementsCommand}[/] to see what VERA reads in it, [teal]{ClearCommand}[/] to start over or [teal]{ExitCommand}[/] to return to the main menu.[/]");
        AnsiConsole.MarkupLine($"[grey]Attach a text or PDF file: [teal]{Markup.Escape(FileCommand.Usage)}[/][/]");
        AnsiConsole.WriteLine();

        // The steps found or proposed so far, kept for /steps.
        var draft = new Draft();

        // VERA acted in the app, so what the model was told about the window may be old.
        var acted = false;
        while (true)
        {
            var message = Ask();
            if (message is null)
            {
                continue;
            }

            if (Is(message, ExitCommand))
            {
                if (StepsScreen.MayLose(store, draft.Steps))
                {
                    return;
                }

                continue;
            }

            if (Is(message, ClearCommand))
            {
                session.Reset();
                AnsiConsole.MarkupLine("[grey]Conversation cleared. The model has forgotten everything said so far.[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            if (Is(message, ElementsCommand))
            {
                ShowElements(session.App, session.ElementLimit);
                AnsiConsole.WriteLine();
                continue;
            }

            if (Is(message, RefreshCommand))
            {
                await ReadAppAsync(app, session, appReader);
                AnsiConsole.Write(SessionPanel(settings, app, session));
                AnsiConsole.WriteLine();
                continue;
            }

            if (Is(message, StepsCommand))
            {
                draft.Steps = await StepsScreen.ShowAsync(settings, app, find, runner, store, draft.Steps, (ran, outcome) => { session.AddRun(ran, outcome); acted = true; });

                // Finding there can click or type too.
                acted = true;
                ShowSessionAgain(settings, app, session);
                continue;
            }

            if (FindInSteps.AskedInChat(message) is { } what)
            {
                var (step, didAct) = await FindInSteps.RunAsync(settings, app, find, what);
                acted |= didAct;
                if (step is not null)
                {
                    // Steps VERA proposed and the tester left alone aren't theirs: a found step starts a new list.
                    if (!draft.Steps.Own)
                    {
                        draft.Steps = new StepChain();
                    }

                    draft.Steps.Steps.Add(step);
                    AnsiConsole.MarkupLine($"[grey]Step {draft.Steps.Steps.Count} >[/] {Markup.Escape(step.ToString())} [grey](type[/] [teal]{StepsCommand}[/] [grey]to see, run or save your steps)[/]");
                }

                AnsiConsole.WriteLine();
                continue;
            }

            if (FileCommand.IsFileCommand(message))
            {
                // With a question, the file is attached and the question is sent as the next message.
                var question = await AttachFileAsync(session, message);
                if (question is null)
                {
                    AnsiConsole.WriteLine();
                    continue;
                }

                message = question;
            }

            // A mistyped command such as "/refesh" would go to the model, which may pretend it ran.
            if (message.StartsWith('/') && !message.Contains(' ', StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]{Markup.Escape(message)} isn't a command.[/] [grey]Commands: [teal]{RefreshCommand}[/], [teal]{ElementsCommand}[/], [teal]{FindCommand}[/], [teal]{StepsCommand}[/], [teal]{ClearCommand}[/], [teal]{FileCommand.Name}[/], [teal]{ExitCommand}[/].[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            // E.g. the tester switched to another browser tab: read the window again so the model answers about the new page.
            if (session.App is { } last && appReader.TitleChanged(last))
            {
                await ReadAppAsync(app, session, appReader);
                AnsiConsole.MarkupLine(
                    $"[grey]The window changed to[/] [teal]{Markup.Escape(session.App?.WindowTitle ?? "")}[/][grey], VERA read it again:[/] {WindowSummary(session.App)}");
            }

            // The title is often the same after VERA clicked or typed.
            else if (acted && session.App is not null)
            {
                await ReadAppAsync(app, session, appReader);
            }

            acted = false;
            var reply = await StreamReplyAsync(settings, app, session, message);
            AnsiConsole.WriteLine();
            if (reply is null)
            {
                continue;
            }

            var proposal = StepProposal.Read(reply, settings.ApiKeyNames);
            if (proposal.Problem is { } problem)
            {
                // Once: without hearing why, the model writes the same steps again.
                AnsiConsole.MarkupLine($"[grey]VERA's steps can't be used: {Markup.Escape(problem)} Asking VERA to correct them…[/]");
                reply = await StreamReplyAsync(settings, app, session, ChatSession.StepsRefusedNote(problem));
                AnsiConsole.WriteLine();
                if (reply is null)
                {
                    continue;
                }

                proposal = StepProposal.Read(reply, settings.ApiKeyNames);
            }

            acted = await OfferStepsAsync(settings, app, session, find, runner, store, draft, proposal);
        }
    }

    // Shows the steps the model proposed; only the tester can run them. True when VERA acted in the app.
    // The proposal becomes the steps /steps opens, unless the tester has steps of their own there.
    private static async Task<bool> OfferStepsAsync(
        AppSettings settings, RunningApp app, ChatSession session, FindElementService find, StepRunner runner, StepFileStore store, Draft draft, StepProposal proposal)
    {
        if (proposal.Problem is { } problem)
        {
            AnsiConsole.MarkupLine($"[yellow]VERA's steps still can't be used: {Markup.Escape(problem)}[/] [grey]Say it another way, or write them with[/] [teal]{StepsCommand}[/][grey].[/]");
            AnsiConsole.WriteLine();
            return false;
        }

        if (proposal.Steps.Count == 0)
        {
            return false;
        }

        var steps = proposal.Steps;
        var chain = StepChain.Proposed(steps);
        if (!draft.Steps.Unsaved)
        {
            draft.Steps = chain;
        }

        StepsScreen.ShowSteps(steps);
        var look = steps.Select((step, i) => (step, number: i + 1)).Where(s => StepProposal.DeservesALook(s.step)).Select(s => s.number).ToList();
        if (look.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Look at step {string.Join(", ", look)} before you run: it may send, change or delete something, or type a saved secret. The AI wrote these steps.[/]");
        }

        if (!proposal.ChecksSomething)
        {
            AnsiConsole.MarkupLine("[yellow]No step checks a result, so \"Passed\" would only mean the steps could be done.[/] [grey]Choose Edit to add one, e.g. expect the results list.[/]");
        }

        var acted = false;
        while (true)
        {
            AnsiConsole.WriteLine();
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Proposed>()
                    .Title(acted ? "What next?" : "Do you want to test this?")
                    .HighlightStyle(Ui.Highlight)
                    .AddChoices(Enum.GetValues<Proposed>())
                    .UseConverter(p => p switch
                    {
                        Proposed.Run => acted ? "Run them again" : $"Run them in {Markup.Escape(app.Name)}",
                        Proposed.Edit => "Edit them",
                        Proposed.Save => "Save them",
                        _ => acted ? "Back to the chat" : "Not now",
                    }));
            switch (choice)
            {
                case Proposed.Run:
                    acted = true;
                    if (await StepsScreen.RunAsync(settings, app, runner, steps) is { } result)
                    {
                        session.AddRun(steps, result);
                    }

                    break;
                case Proposed.Edit:
                    // The edited steps take the place of the ones the chat holds.
                    if (draft.Steps != chain && !StepsScreen.MayLose(store, draft.Steps, "Replace them with VERA's steps?", "Replace them (yours are lost)", "Keep mine"))
                    {
                        break;
                    }

                    draft.Steps = await StepsScreen.ShowAsync(settings, app, find, runner, store, chain, session.AddRun);
                    ShowSessionAgain(settings, app, session);

                    // Finding there can click or type too, not only a run.
                    return true;
                case Proposed.Save:
                    StepsScreen.Save(store, chain);
                    break;
                default:
                    if (!acted && draft.Steps == chain)
                    {
                        AnsiConsole.MarkupLine($"[grey]Type[/] [teal]{StepsCommand}[/] [grey]to run, edit or save them later.[/]");
                    }

                    AnsiConsole.WriteLine();
                    return acted;
            }
        }
    }

    private static void ShowSessionAgain(AppSettings settings, RunningApp app, ChatSession session)
    {
        Ui.ShowHeader(settings);
        Ui.Title(Title);
        AnsiConsole.Write(SessionPanel(settings, app, session));
        AnsiConsole.MarkupLine("[grey]The conversation goes on: VERA remembers what was said.[/]");
        AnsiConsole.WriteLine();
    }

    // The steps /steps opens: the last ones VERA proposed, or the tester's own.
    private sealed class Draft
    {
        public StepChain Steps { get; set; } = new();
    }

    // Returns the question to send next, or null when there is nothing to send: the tester gave no question, or the file was not attached.
    private static async Task<string?> AttachFileAsync(ChatSession session, string message)
    {
        if (!FileCommand.TryParse(message, out var command, out var problem))
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(problem)}[/] [grey]Use: {Markup.Escape(FileCommand.Usage)}[/]");
            return null;
        }

        try
        {
            var file = await AnsiConsole.Status()
                .StartAsync("Reading file…", _ => session.AttachFileAsync(command.Path, command.Pages, CancellationToken.None));
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(AttachedFileSummary.Describe(file))}[/]");
            return command.Question;
        }
        catch (FileAccessException e)
        {
            ShowErrorPanel(e.Message);
            if (e.Error == FileAccessError.NotFound)
            {
                AnsiConsole.MarkupLine("[grey]A path with spaces needs quotes, for example: /file \"C:\\my folder\\spec.pdf\"[/]");
            }

            return null;
        }
    }

    // The whole reply when it finished; null when it was stopped or failed.
    private static async Task<string?> StreamReplyAsync(AppSettings settings, RunningApp app, ChatSession session, string message)
    {
        var reply = new StringBuilder();

        // Ctrl+C stops only this reply. The handler is removed when the reply ends, so at the prompt Ctrl+C works as before.
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            await AnsiConsole.Live(ReplyPanel(null))
                .AutoClear(true)
                .Overflow(VerticalOverflow.Crop)
                .StartAsync(async ctx =>
                {
                    // The live view draws nothing until it is refreshed, so "thinking…" would stay hidden until the first chunk.
                    ctx.Refresh();
                    var sinceRedraw = Stopwatch.StartNew();
                    var sinceSent = Stopwatch.StartNew();
                    await using var chunks = session.StreamReplyAsync(message, stop.Token).GetAsyncEnumerator(stop.Token);
                    while (true)
                    {
                        // Nothing arrives while the model thinks or its provider is busy: count the seconds, so the screen doesn't look frozen.
                        var next = chunks.MoveNextAsync().AsTask();
                        while (await Task.WhenAny(next, Task.Delay(WaitingTick)) != next)
                        {
                            if (StepProposal.Visible(reply.ToString()).Length == 0)
                            {
                                ctx.UpdateTarget(ReplyPanel(null, session.ProviderLabel, sinceSent.Elapsed));
                            }
                        }

                        if (!await next)
                        {
                            break;
                        }

                        reply.Append(chunks.Current);
                        if (sinceRedraw.Elapsed >= RedrawInterval)
                        {
                            // Only steps so far, which are not shown here: still thinking.
                            var soFar = StepProposal.Visible(reply.ToString());
                            ctx.UpdateTarget(soFar.Length == 0 ? ReplyPanel(null, session.ProviderLabel, sinceSent.Elapsed) : ReplyPanel(soFar));
                            sinceRedraw.Restart();
                        }
                    }
                });
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The session keeps the user message and adds no assistant message for a stopped reply.
            if (StepProposal.Visible(reply.ToString()) is { Length: > 0 } arrived)
            {
                AnsiConsole.Write(ReplyPanel(arrived));
            }

            AnsiConsole.MarkupLine("[grey](stopped)[/]");
            return null;
        }
#pragma warning disable CA1031 // Any failure of one reply is shown in the chat; the chat itself must stay open.
        catch (Exception e)
#pragma warning restore CA1031
        {
            // Keep what already arrived on screen, then explain what went wrong.
            if (StepProposal.Visible(reply.ToString()) is { Length: > 0 } arrived)
            {
                AnsiConsole.Write(ReplyPanel(arrived));
            }

            ShowError(settings, app, session, e);
            return null;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        // The live view is cropped to the window height; print the full reply once it is complete.
        if (string.IsNullOrWhiteSpace(reply.ToString()))
        {
            AnsiConsole.MarkupLine("[grey](no reply, send it again)[/]");
        }
        else
        {
            // Some replies are only steps, without the sentence the model was asked for.
            var text = StepProposal.Visible(reply.ToString());
            AnsiConsole.Write(ReplyPanel(text.Length == 0 ? "Should I test this?" : text));
        }

        // Also after a blank reply: many tokens with no text means the model spent them on hidden reasoning.
        if (session.LastReplyStats is { } stats)
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(FormatStats(stats))}[/]");
        }

        if (session.DroppedForRoom > 0)
        {
            AnsiConsole.MarkupLine($"[grey]The chat got too long for the model, so it no longer sees the {session.DroppedForRoom} oldest messages. Attach a file again with /file if it still needs it, or type /clear to start fresh.[/]");
        }

        SetConnection(settings, app, session, ConnectionState.Connected, "");
        return reply.ToString();
    }

    // e.g. "212 tokens · 3.4 s · 62 tokens/s". Tokens include hidden reasoning, so they can exceed the visible text.
    private static string FormatStats(ReplyStats stats)
    {
        var culture = CultureInfo.InvariantCulture;
        var seconds = stats.Duration.TotalSeconds.ToString("0.0", culture) + " s";
        return stats.OutputTokens is { } tokens && stats.TokensPerSecond is { } perSecond
            ? $"{tokens.ToString(culture)} tokens · {seconds} · {perSecond.ToString("0", culture)} tokens/s"
            : seconds;
    }

    private static void ShowError(AppSettings settings, RunningApp app, ChatSession session, Exception error)
    {
        string text;
        switch (error)
        {
            case LlmUnauthorizedException:
                text = error.Message;
                SetConnection(settings, app, session, ConnectionState.Unauthorized, error.Message);
                break;

            case LlmDailyQuotaException:
                text = $"{error.Message} Switch to a local model in Settings to keep working.";
                break;

            // Ollama answers 404 when the model is not installed.
            case HttpRequestException { StatusCode: HttpStatusCode.NotFound } when settings.IsLocalModel:
                text = $"Model '{session.ModelName}' is not installed. Run: ollama pull {session.ModelName}";
                SetConnection(settings, app, session, ConnectionState.ModelMissing, text);
                break;

            // Refused or dropped connection, or no answer before the client's timeout (Ctrl+C is handled before this).
            case HttpRequestException or IOException or OperationCanceledException when settings.IsLocalModel:
                text = "Could not reach Ollama. Start it and send your message again.";
                SetConnection(settings, app, session, ConnectionState.Unreachable, "Ollama is not running. Start it, then try again.");
                break;

            case HttpRequestException or IOException or OperationCanceledException:
                text = $"Could not reach {session.ProviderLabel}. Check your internet connection and send your message again.";
                SetConnection(settings, app, session, ConnectionState.Unreachable, $"Could not reach {session.ProviderLabel}.");
                break;

            default:
                text = error.Message;
                break;
        }

        ShowErrorPanel(text);
    }

    private static void ShowErrorPanel(string text) =>
        AnsiConsole.Write(new Panel(new Text(text, new Style(Color.Red)))
            .Header("[red]Error[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red)
            .Padding(1, 0));

    // Stores the new state; when it changed, prints the session panel again so the status there is current.
    private static void SetConnection(AppSettings settings, RunningApp app, ChatSession session, ConnectionState state, string detail)
    {
        if (settings.ConnectionState == state && settings.ConnectionDetail == detail)
        {
            return;
        }

        settings.ConnectionState = state;
        settings.ConnectionDetail = detail;
        AnsiConsole.WriteLine();
        AnsiConsole.Write(SessionPanel(settings, app, session));
    }

    // Model text is shown as a Text widget, never as markup, so "[" and "]" in a reply can't break rendering.
    private static Panel ReplyPanel(string? text, string provider = "", TimeSpan waited = default)
    {
        IRenderable body = text is null
            ? new Markup(Thinking(provider, waited))
            : new Text(text, new Style(Neon));

        return new Panel(body)
            .Header($"[{Neon.ToMarkup()}]VERA[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Neon)
            .Padding(1, 0);
    }

    private static string Thinking(string provider, TimeSpan waited)
    {
        var seconds = ((int)waited.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return waited < WaitingTick ? "[grey]thinking…[/]"
            : waited < SlowAfter ? $"[grey]thinking… {seconds} s[/]"
            : $"[grey]thinking… {seconds} s[/]\n[yellow]{Markup.Escape(provider)} is slow or busy. VERA tries again by itself for a while. Press Ctrl+C to stop waiting.[/]";
    }

    private static Panel SessionPanel(AppSettings settings, RunningApp app, ChatSession session)
    {
        var connectedTo = settings.ConnectionState switch
        {
            ConnectionState.Connected => $"[teal]{Markup.Escape(session.ProviderLabel)}[/] [green]✓[/]",
            ConnectionState.ModelMissing or ConnectionState.Unauthorized => $"[yellow]{Markup.Escape(settings.ConnectionDetail)}[/]",
            _ => $"[red]{Markup.Escape(settings.ConnectionDetail)}[/]",
        };

        // The title from the last read, e.g. the browser's new tab after /refresh; the picker's title is from before the chat.
        var current = session.App is { WindowTitle.Length: > 0 } read ? app with { WindowTitle = read.WindowTitle } : app;

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn()
            .AddRow("[grey]Process[/]", $"[teal]{Markup.Escape(current.ToString())}[/]")
            .AddRow("[grey]Model[/]", $"[teal]{Markup.Escape(session.ModelName)}[/]")
            .AddRow("[grey]Connected to[/]", connectedTo)
            .AddRow("[grey]Window[/]", WindowSummary(session.App));

        // The model runs elsewhere, e.g. Gemini: say what is hidden from it and what isn't (#30, #31).
        if (session.HidesPersonalData)
        {
            grid.AddRow(
                "[grey]Privacy[/]",
                $"[teal]Emails, phone and card numbers, codes, login fields and your hide list are hidden from {Markup.Escape(session.ProviderLabel)}.[/] "
                + "[yellow]Screenshots are sent as they are.[/]");
        }

        return new Panel(grid)
            .Header("[bold]Session[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Teal)
            .Expand();
    }

    // Reads the app's window and tells the model about it. A window that can't be read is shown in the Session panel, not as an error.
    private static async Task ReadAppAsync(RunningApp app, ChatSession session, ChatAppContextReader appReader)
    {
        var context = await AnsiConsole.Status()
            .StartAsync("Reading the app's window…", _ => appReader.ReadAsync(app.Id, app.Name, app.WindowTitle, CancellationToken.None));
        session.SetApp(context);
    }

    // e.g. "23 elements seen", or why the window couldn't be read.
    private static string WindowSummary(ChatAppContext? app) => app switch
    {
        null => "[grey]not read[/]",
        { Problem: { } problem } => $"[yellow]{Markup.Escape(problem)}[/]",
        { ElementCount: 0 } => $"[yellow]No buttons or fields found. Type {RefreshCommand} to read it again.[/]",
        _ => $"[teal]{app.ElementCount.ToString(CultureInfo.InvariantCulture)} elements seen[/]",
    };

    // Every element VERA read, so the tester can check what the model can know about. The model gets at most `limit` of them.
    private static void ShowElements(ChatAppContext? app, int limit)
    {
        if (app is null || app.ElementCount == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]VERA has read no elements in this window.[/] [grey]Type {RefreshCommand} to read it again.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn("[grey]#[/]").AddColumn("[grey]Role[/]").AddColumn("[grey]Name[/]");
        foreach (var e in app.Elements.Where(e => e.Id > 0))
        {
            table.AddRow(
                e.Id.ToString(CultureInfo.InvariantCulture),
                Markup.Escape(e.Role),
                e.Name.Length == 0 ? "[grey](no name, not sent)[/]" : Markup.Escape(e.Name) + (e.IsEnabled ? "" : " [grey](disabled)[/]"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"[grey]{app.ElementCount.ToString(CultureInfo.InvariantCulture)} elements. The model gets the named ones, up to {limit.ToString(CultureInfo.InvariantCulture)}.[/]");
    }

    // Returns the trimmed message, or null when the tester just pressed Enter.
    private static string? Ask()
    {
        var message = AnsiConsole.Prompt(new TextPrompt<string>("[bold teal]You >[/]").AllowEmpty());
        return string.IsNullOrWhiteSpace(message) ? null : message.Trim();
    }

    private static bool Is(string message, string command) =>
        message.Equals(command, StringComparison.OrdinalIgnoreCase);
}