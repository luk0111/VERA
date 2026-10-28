using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input;
using Vera.Input.Harness;
using Vera.Input.Harness.Commands;
using Vera.Input.Platform;

try
{
    // No Args: the command line belongs to Spectre.Console.Cli, not to the configuration.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
    builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);

    // "Hosting failed to start" would repeat the error below with a stack trace; the exception still reaches the catch.
    builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None);
    builder.Services.AddVeraInput(builder.Configuration);

    var app = new CommandApp(new HostTypeRegistrar(builder));
    app.Configure(config =>
    {
        config.SetApplicationName("Vera.Input.Harness");
        config.PropagateExceptions();
        config.AddCommand<InfoCommand>("info").WithDescription("Prints the DPI awareness and the effective VeraInput configuration.");
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