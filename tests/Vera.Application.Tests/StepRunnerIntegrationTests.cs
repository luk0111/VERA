#if WINDOWS
using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Vera.Adapters.Desktop;
using Vera.Core;
using Vera.Input;

namespace Vera.Application.Tests;

/// <summary>
/// Runs real chains of steps in Character Map with the real desktop adapter, mouse and keyboard. A stand-in model finds the elements by name,
/// so the result doesn't depend on a real model. Needs an unlocked desktop and nobody touching the mouse; CI skips it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StepRunnerIntegrationTests : IDisposable
{
    private const string Field = "the characters to copy box";

    private readonly string _output = Path.Combine(Path.GetTempPath(), "vera-steps-integration", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public Task A_chain_that_works_passes() => WithCharacterMapAsync(async charmap =>
    {
        var result = await RunAsync(charmap,
            $"type \"abc\" into {Field}",
            "click the select button",
            $"expect {Field} contains \"abc\"");

        result.Verdict.ShouldBe(Verdict.Passed);
        result.Steps.Count.ShouldBe(3);
        result.Steps.ShouldAllBe(s => File.Exists(s.ScreenshotPath));
    });

    [Fact]
    public Task A_check_that_does_not_hold_fails() => WithCharacterMapAsync(async charmap =>
    {
        var result = await RunAsync(charmap,
            $"type \"abc\" into {Field}",
            $"expect {Field} contains \"xyz\"");

        result.Verdict.ShouldBe(Verdict.Failed);
        result.Steps[^1].Message.ShouldBe("field \"Characters to copy :\" has \"abc\", which doesn't contain \"xyz\".");
    });

    [Fact]
    public Task A_missing_element_is_inconclusive_and_nothing_after_it_runs() => WithCharacterMapAsync(async charmap =>
    {
        var result = await RunAsync(charmap,
            "click the log in button",
            $"type \"abc\" into {Field}");

        result.Verdict.ShouldBe(Verdict.Inconclusive);
        result.Steps.Count.ShouldBe(1);
        (await FieldTextAsync(charmap)).ShouldBeEmpty();
    });

    public void Dispose()
    {
        if (Directory.Exists(_output))
        {
            Directory.Delete(_output, recursive: true);
        }
    }

    private static Func<IAppAdapter> RealAdapters() =>
        new ServiceCollection()
            .AddLogging()
            .AddVeraInput(new ConfigurationBuilder().Build())
            .AddVeraDesktopAdapter()
            .BuildServiceProvider()
            .GetRequiredService<Func<IAppAdapter>>();

    private Task<StepRunResult> RunAsync(Process charmap, params string[] lines) =>
        new StepRunner(RealAdapters(), new NameModel(), Substitute.For<ISecretStore>(), _output, TimeSpan.FromMilliseconds(300))
            .RunAsync(charmap.Id, "Character Map", [.. lines.Select(l => StepParser.Parse(l)!)], null, null, Ct);

    // The field's text, read with a fresh adapter. The RichEdit field ends with an end-of-text "\r" while it has the focus.
    private static async Task<string> FieldTextAsync(Process charmap)
    {
        await using var adapter = RealAdapters()();
        await adapter.StartAsync(new AppTarget("Character Map", AppPlatform.Desktop, "", ProcessId: charmap.Id), Ct);
        var screen = await adapter.GetScreenStateAsync(Ct);
        return (screen.Elements.Single(e => e.Role == "Edit" && e.Name.StartsWith("Characters to copy", StringComparison.Ordinal)).Value ?? "").TrimEnd('\r');
    }

    private static async Task WithCharacterMapAsync(Func<Process, Task> test)
    {
        using var charmap = Process.Start("charmap.exe");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (charmap.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200, Ct);
                charmap.Refresh();
            }

            await test(charmap);
        }
        finally
        {
            charmap.Kill();
        }
    }

    /// <summary>Stands in for the model: points at Character Map's field or Select button when the step describes them, else finds nothing.</summary>
    private sealed class NameModel : ILlmClient
    {
        private static readonly Dictionary<string, (string Role, string Name)> Known = new(StringComparer.OrdinalIgnoreCase)
        {
            ["the characters to copy box"] = ("Edit", "Characters to copy"),
            ["the select button"] = ("Button", "Select"),
        };

        public Task<AgentAction> DecideNextActionAsync(TestTask task, ScreenState screen, IReadOnlyList<AgentStep> history, CancellationToken ct)
        {
            var what = task.Description["Click ".Length..];
            var element = Known.TryGetValue(what, out var known)
                ? screen.Elements.FirstOrDefault(e => e.Id > 0 && e.Role == known.Role && e.Name.StartsWith(known.Name, StringComparison.Ordinal))
                : null;
            return Task.FromResult(element is null
                ? new AgentAction(AgentActionType.Fail, null, null, "It isn't on this screen.")
                : new AgentAction(AgentActionType.Click, element.Id, null, $"It's \"{element.Name}\"."));
        }
    }
}
#endif
