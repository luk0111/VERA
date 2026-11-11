using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using Vera.Application;
using Vera.Llm;

namespace Vera.Cli.Screens;

/// <summary>Chat with the model about the selected app. Replies stream in; the session remembers the conversation.</summary>
internal static class ChatScreen
{
    private const string ExitCommand = "/back";
    private const string ClearCommand = "/clear";
    private const string RefreshCommand = "/refresh";
    private const string ElementsCommand = "/elements";

    private static readonly Color Neon = new(57, 255, 20);

    // Redrawing on every token flickers
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(50);

    public static async Task ShowAsync(AppSettings settings, RunningApp app, ChatSession session, ChatAppContextReader appReader)
    {
        if (settings.SelectedModel is null || (settings.SelectedModel.NeedsApiKey && !settings.IsApiKeyConnected))
        {
            ShowNotConnected(settings, app);
            return;
        }

        Ui.ShowHeader(settings);
        Ui.Title("Agent chat");
        var status = await AnsiConsole.Status()
            .StartAsync($"Checking {Markup.Escape(session.ProviderLabel)}…", _ => session.CheckConnectionAsync(CancellationToken.None));
        Store(settings, status);
        await ReadAppAsync(app, session, appReader);

        Ui.ShowHeader(settings);
        Ui.Title("Agent chat");
        AnsiConsole.Write(SessionPanel(settings, app, session));
        if (settings.ConnectionState != ConnectionState.Connected)
        {
            var hint = settings.ConnectionState switch
            {
                ConnectionState.Unauthorized => "Connect a valid API key in Settings, then come back.",
                _ when settings.IsLocalModel => "Start Ollama, then send a message to retry.",
                _ => "Check your internet connection, then send a message to retry.",
            };
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(hint)}[/]");
        }

        AnsiConsole.MarkupLine(
            $"[grey]Describe what to test and press Enter. Type [teal]{RefreshCommand}[/] after the app's window changes, [teal]{ElementsCommand}[/] to see what VERA reads in it, [teal]{ClearCommand}[/] to start over or [teal]{ExitCommand}[/] to return to the main menu.[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var message = Ask();
            if (message is null)
            {
                continue;
            }

            if (Is(message, ExitCommand))
            {
                return;
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
                ShowElements(session.App);
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

            // E.g. the tester switched to another browser tab: read the window again so the model answers about the new page.
            if (session.App is { } last && appReader.TitleChanged(last))
            {
                await ReadAppAsync(app, session, appReader);
                AnsiConsole.MarkupLine(
                    $"[grey]The window changed to[/] [teal]{Markup.Escape(session.App?.WindowTitle ?? "")}[/][grey], VERA read it again:[/] {WindowSummary(session.App)}");
            }

            await StreamReplyAsync(settings, app, session, message);
            AnsiConsole.WriteLine();
        }
    }

    // No model, or a hosted model without a key: say so and make no call.
    private static void ShowNotConnected(AppSettings settings, RunningApp app)
    {
        Ui.ShowHeader(settings);
        Ui.Title("Agent chat");
        AnsiConsole.Write(new Panel(
                new Grid()
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn()
                    .AddRow("[grey]Process[/]", $"[teal]{Markup.Escape(app.ToString())}[/]")
                    .AddRow("[grey]Model[/]", $"[teal]{Markup.Escape(settings.ModelLabel)}[/]"))
            .Header("[bold]Session[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Teal)
            .Expand());

        var notConnected = new Panel(new Markup(
                "[yellow]This model has no API key yet.[/] Add one in Settings → [teal]Connect your API key[/], or choose a local model."))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Padding(1, 0);
        AnsiConsole.Write(notConnected);
        AnsiConsole.MarkupLine($"[grey]Type [teal]{ExitCommand}[/] to return to the main menu.[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var message = Ask();
            if (message is not null && Is(message, ExitCommand))
            {
                return;
            }

            if (message is not null)
            {
                AnsiConsole.Write(notConnected);
                AnsiConsole.WriteLine();
            }
        }
    }

    private static async Task StreamReplyAsync(AppSettings settings, RunningApp app, ChatSession session, string message)
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
                    await foreach (var chunk in session.StreamReplyAsync(message, stop.Token))
                    {
                        reply.Append(chunk);
                        if (sinceRedraw.Elapsed >= RedrawInterval)
                        {
                            ctx.UpdateTarget(ReplyPanel(reply.ToString()));
                            sinceRedraw.Restart();
                        }
                    }
                });
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The session keeps the user message and adds no assistant message for a stopped reply.
            if (reply.Length > 0)
            {
                AnsiConsole.Write(ReplyPanel(reply.ToString()));
            }

            AnsiConsole.MarkupLine("[grey](stopped)[/]");
            return;
        }
#pragma warning disable CA1031 // Any failure of one reply is shown in the chat; the chat itself must stay open.
        catch (Exception e)
#pragma warning restore CA1031
        {
            // Keep what already arrived on screen, then explain what went wrong.
            if (reply.Length > 0)
            {
                AnsiConsole.Write(ReplyPanel(reply.ToString()));
            }

            ShowError(settings, app, session, e);
            return;
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
            AnsiConsole.Write(ReplyPanel(reply.ToString()));
        }

        // Also after a blank reply: many tokens with no text means the model spent them on hidden reasoning.
        if (session.LastReplyStats is { } stats)
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(FormatStats(stats))}[/]");
        }

        SetConnection(settings, app, session, ConnectionState.Connected, "");
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

        AnsiConsole.Write(new Panel(new Text(text, new Style(Color.Red)))
            .Header("[red]Error[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red)
            .Padding(1, 0));
    }

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
    private static Panel ReplyPanel(string? text)
    {
        IRenderable body = text is null
            ? new Markup("[grey]thinking…[/]")
            : new Text(text, new Style(Neon));

        return new Panel(body)
            .Header($"[{Neon.ToMarkup()}]VERA[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Neon)
            .Padding(1, 0);
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

    // Every element VERA read, so the tester can check what the model can know about. The model gets at most 40 of them.
    private static void ShowElements(ChatAppContext? app)
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
            $"[grey]{app.ElementCount.ToString(CultureInfo.InvariantCulture)} elements. The model gets the named ones, up to 40.[/]");
    }

    private static void Store(AppSettings settings, ConnectionStatus status)
    {
        settings.ConnectionState = status.Kind switch
        {
            ConnectionStatusKind.Connected => ConnectionState.Connected,
            ConnectionStatusKind.ModelMissing => ConnectionState.ModelMissing,
            ConnectionStatusKind.Unauthorized => ConnectionState.Unauthorized,
            _ => ConnectionState.Unreachable,
        };
        settings.ConnectionDetail = status.Message;
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