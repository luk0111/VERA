using System.Diagnostics;
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

    private static readonly Color Neon = new(57, 255, 20);

    // Redrawing on every token flickers
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(50);

    public static async Task ShowAsync(AppSettings settings, RunningApp app, ChatSession session)
    {
        if (!settings.IsLocalModel)
        {
            ShowNotConnected(settings, app);
            return;
        }

        Ui.ShowHeader(settings);
        Ui.Title("Agent chat");
        var status = await AnsiConsole.Status()
            .StartAsync("Checking Ollama…", _ => session.CheckConnectionAsync(CancellationToken.None));
        Store(settings, status);

        Ui.ShowHeader(settings);
        Ui.Title("Agent chat");
        AnsiConsole.Write(SessionPanel(settings, app, session));
        if (settings.ConnectionState != ConnectionState.Connected)
        {
            AnsiConsole.MarkupLine("[grey]Start Ollama, then send a message to retry.[/]");
        }

        AnsiConsole.MarkupLine($"[grey]Describe what to test and press Enter. Type [teal]{ClearCommand}[/] to start over or [teal]{ExitCommand}[/] to return to the main menu.[/]");
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

            await StreamReplyAsync(settings, app, session, message);
            AnsiConsole.WriteLine();
        }
    }

    // Any model other than the local one has no backend yet: say so and make no call.
    private static void ShowNotConnected(AppSettings settings, RunningApp app)
    {
        Ui.ShowHeader(settings);
        Ui.Title("Agent chat");
        AnsiConsole.Write(new Panel(
                new Grid()
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn()
                    .AddRow("[grey]Process[/]", $"[teal]{Markup.Escape(app.ToString())}[/]")
                    .AddRow("[grey]Model[/]", $"[teal]{Markup.Escape(settings.Model)}[/]"))
            .Header("[bold]Session[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Teal)
            .Expand());

        var notConnected = new Panel(new Markup($"[yellow]This model is not connected yet.[/] Choose [teal]{Markup.Escape(AppSettings.LocalModelEntry)}[/] in Settings."))
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
        AnsiConsole.Write(ReplyPanel(reply.ToString()));
        SetConnection(settings, app, session, ConnectionState.Connected, "");
    }

    private static void ShowError(AppSettings settings, RunningApp app, ChatSession session, Exception error)
    {
        string text;
        switch (error)
        {
            // Ollama answers 404 when the model is not installed.
            case HttpRequestException { StatusCode: HttpStatusCode.NotFound }:
                text = $"Model '{session.ModelName}' is not installed. Run: ollama pull {session.ModelName}";
                SetConnection(settings, app, session, ConnectionState.ModelMissing, text);
                break;

            // Refused or dropped connection, or no answer before the client's timeout (Ctrl+C is handled before this).
            case HttpRequestException or IOException or OperationCanceledException:
                text = "Could not reach Ollama. Start it and send your message again.";
                SetConnection(settings, app, session, ConnectionState.Unreachable, "Ollama is not running. Start it, then try again.");
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
            ConnectionState.ModelMissing => $"[yellow]{Markup.Escape(settings.ConnectionDetail)}[/]",
            _ => $"[red]{Markup.Escape(settings.ConnectionDetail)}[/]",
        };

        return new Panel(
                new Grid()
                    .AddColumn(new GridColumn().NoWrap())
                    .AddColumn()
                    .AddRow("[grey]Process[/]", $"[teal]{Markup.Escape(app.ToString())}[/]")
                    .AddRow("[grey]Model[/]", $"[teal]{Markup.Escape(session.ModelName)}[/]")
                    .AddRow("[grey]Connected to[/]", connectedTo))
            .Header("[bold]Session[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Teal)
            .Expand();
    }

    private static void Store(AppSettings settings, ConnectionStatus status)
    {
        settings.ConnectionState = status.Kind switch
        {
            ConnectionStatusKind.Connected => ConnectionState.Connected,
            ConnectionStatusKind.ModelMissing => ConnectionState.ModelMissing,
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