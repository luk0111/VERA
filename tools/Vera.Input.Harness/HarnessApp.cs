using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Harness.Commands;
using Vera.Input.Platform;

namespace Vera.Input.Harness;

/// <summary>Builds the host and runs one harness command. Each run gets a fresh host, so the menu can change settings between runs.</summary>
internal static class HarnessApp
{
    /// <summary>Runs one command and turns the expected failures into a red message and an exit code.</summary>
    /// <param name="overrides">Settings on top of appsettings.json, e.g. <c>VeraInput:Rdp:Enabled = false</c>.</param>
    public static async Task<int> RunAsync(string[] args, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        try
        {
            var app = new CommandApp(new HostTypeRegistrar(CreateBuilder(overrides)));
            app.Configure(config =>
            {
                config.SetApplicationName("Vera.Input.Harness");
                config.PropagateExceptions();
                config.UseStrictParsing();
                config.AddCommand<InfoCommand>("info").WithDescription("Prints the DPI awareness and the effective VeraInput configuration.");
                config.AddCommand<ScreenshotCommand>("screenshot").WithDescription("Saves a screenshot of the capture monitor as a PNG.");
                config.AddCommand<MapCommand>("map").WithDescription("Shows where a point in a screenshot lands on the screen. Nothing moves.");
                config.AddCommand<MouseCommand>("mouse").WithDescription("Shows live where the mouse is, on the screen and in a screenshot.");
                config.AddCommand<ClickCommand>("click").WithDescription("Takes a screenshot, clicks a point in it and checks where the cursor landed.");
                config.AddCommand<TypeCommand>("type").WithDescription("Types text into the window in front, after a countdown.");
            });

            return await app.RunAsync(args);
        }
        catch (Exception e) when (e is HarnessConfigurationException or InvalidDataException)
        {
            var message = e is InvalidDataException ? $"appsettings.json could not be read: {e.InnerException?.Message ?? e.Message}" : e.Message;
            AnsiConsole.MarkupLine($"[red]Configuration error:[/] {Markup.Escape(message)}");
            return 1;
        }
        catch (DpiAwarenessException e)
        {
            AnsiConsole.MarkupLine($"[red]DPI error:[/] {Markup.Escape(e.Message)}");
            return 1;
        }
        catch (DisplayMismatchException e)
        {
            AnsiConsole.MarkupLine($"[red]Display error:[/] {Markup.Escape(e.Message)}");
            return 1;
        }
        catch (ScreenUnavailableException e)
        {
            AnsiConsole.MarkupLine($"[red]Screen unavailable ({e.Reason}):[/] {Markup.Escape(e.Message)}");
            return 2;
        }
        catch (InputRejectedException e)
        {
            AnsiConsole.MarkupLine($"[red]Input rejected:[/] {Markup.Escape(e.Message)}");
            return 2;
        }
        catch (CommandAppException e)
        {
            // A wrong command or option, e.g. "--delay -1". PropagateExceptions would otherwise show it as a crash.
            if (e.Pretty is not null)
            {
                AnsiConsole.Write(e.Pretty);
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(e.Message)}");
            }

            return 1;
        }
    }

    public static HostApplicationBuilder CreateBuilder(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        // No Args: the command line belongs to Spectre.Console.Cli, not to the configuration.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);

        // Sinks and levels come from the "Serilog" section of appsettings.json (warnings on the console, everything in logs/).
        builder.Services.AddSerilog((services, logger) => logger.ReadFrom.Configuration(builder.Configuration).ReadFrom.Services(services));

        // "Hosting failed to start" would repeat the error below with a stack trace; the exception still reaches the catch.
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None);
        builder.Services.AddVeraInput(builder.Configuration);
        return builder;
    }
}
