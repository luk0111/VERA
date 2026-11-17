using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Vera.Input.Harness.Commands;

/// <summary>Presses a key or key combo in the window in front, after a countdown so you can click into it.</summary>
internal sealed class KeysCommand(IScreen screen, TimeProvider timeProvider) : AsyncCommand<KeysCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<COMBO>")]
        [Description("The key or combo, e.g. enter, ctrl+s, shift+tab, alt+f4")]
        public string Combo { get; init; } = "";

        [CommandOption("--repeat <N>")]
        [Description("Press it this many times. Default: 1")]
        public int Repeat { get; init; } = 1;

        [CommandOption("--delay <SECONDS>")]
        [Description("Wait this long first, to click into the target window. Default: 3")]
        public int Delay { get; init; } = 3;

        public override ValidationResult Validate() =>
            Delay < 0 ? ValidationResult.Error("--delay can't be negative.")
            : Repeat < 1 ? ValidationResult.Error("--repeat must be at least 1.")
            : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var times = settings.Repeat == 1 ? "" : $" ×{settings.Repeat}";
        AnsiConsole.MarkupLine($"Will press: [bold]{Markup.Escape(settings.Combo)}[/]{times}");

        for (var left = settings.Delay; left > 0; left--)
        {
            AnsiConsole.MarkupLine($"[grey]Pressing in {left}… click into the target window.[/]");
            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
        }

        try
        {
            await screen.KeyAsync(settings.Combo, settings.Repeat, cancellationToken);
        }
        catch (InvalidKeyComboException e)
        {
            AnsiConsole.MarkupLine($"[red]Can't press that:[/] {Markup.Escape(e.Message)}");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Pressed.[/] Check what the window did.");
        return 0;
    }
}
