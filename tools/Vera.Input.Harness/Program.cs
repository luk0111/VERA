using Spectre.Console;

using Vera.Input.Harness;

// No command in an interactive console: show the menu. Otherwise (a command, --help, or redirected output) run it as typed.
if (args.Length == 0 && AnsiConsole.Profile.Capabilities.Interactive)
{
    return await HarnessMenu.RunAsync();
}

return await HarnessApp.RunAsync(args);
