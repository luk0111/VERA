using System.Buffers.Binary;
using System.Diagnostics;

using FlaUI.Core.Definitions;
using FlaUI.UIA3;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Vera.Core;
using Vera.Input;

using Windows.Win32;

namespace Vera.Adapters.Desktop.Tests;

public sealed class DesktopAdapterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starting_without_a_process_id_is_not_supported()
    {
        await using var adapter = new DesktopAdapter(Substitute.For<IScreenshotSource>());

        await Should.ThrowAsync<NotSupportedException>(() => adapter.StartAsync(new AppTarget("x", AppPlatform.Desktop, "x.exe"), Ct));
    }

    [Fact]
    public async Task Attaching_to_a_process_that_is_gone_throws()
    {
        await using var adapter = new DesktopAdapter(Substitute.For<IScreenshotSource>());

        await Should.ThrowAsync<InvalidOperationException>(() => adapter.StartAsync(new AppTarget("x", AppPlatform.Desktop, "", ProcessId: int.MaxValue), Ct));
    }
}

/// <summary>Reads Character Map, a classic Win32 window on every Windows install, so it needs an unlocked desktop. CI skips it.</summary>
[Trait("Category", "Integration")]
public sealed class DesktopAdapterIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Character_Map_is_read_with_its_Copy_button()
    {
        var state = await ReadCharacterMapAsync();

        state.Elements[0].Role.ShouldBe("Window");
        state.Elements.ShouldContain(e => e.Role == "Button" && e.Name == "Copy");
    }

    [Fact]
    public async Task The_screenshot_is_the_size_of_the_window()
    {
        var state = await ReadCharacterMapAsync();

        // A PNG stores width and height as big-endian ints at bytes 16 and 20.
        var png = state.ScreenshotPng.Span;
        BinaryPrimitives.ReadInt32BigEndian(png[16..]).ShouldBe(state.Elements[0].Bounds.Width);
        BinaryPrimitives.ReadInt32BigEndian(png[20..]).ShouldBe(state.Elements[0].Bounds.Height);
    }

    [Fact]
    public async Task The_screenshot_is_taken_of_the_window_area()
    {
        byte[] png = [1, 2, 3];
        var screen = Substitute.For<IScreenshotSource>();
        screen.ScreenshotAreaAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ScreenFrame(1, 1, 1, 1, 1, png, DateTimeOffset.UtcNow, 1));

        var state = await ReadCharacterMapAsync(screen);

        var window = state.Elements[0].Bounds;
        await screen.Received(1).ScreenshotAreaAsync(window.X, window.Y, window.Width, window.Height, Arg.Any<CancellationToken>());
        state.ScreenshotPng.ToArray().ShouldBe(png);
    }

    [Fact]
    public async Task A_window_behind_another_one_comes_to_the_front_for_its_screenshot()
    {
        using var behind = await StartCharacterMapAsync();
        using var inFront = await StartCharacterMapAsync();
        try
        {
            await using var adapter = new DesktopAdapter(RealScreen());
            await adapter.StartAsync(new AppTarget("Character Map", AppPlatform.Desktop, "", ProcessId: behind.Id), Ct);

            await adapter.GetScreenStateAsync(Ct);

            ((nint)PInvoke.GetForegroundWindow()).ShouldBe(behind.MainWindowHandle);
        }
        finally
        {
            behind.Kill();
            inFront.Kill();
        }
    }

    [Fact]
    public async Task A_screen_that_cannot_be_seen_gives_its_reason()
    {
        var screen = Substitute.For<IScreenshotSource>();
        screen.ScreenshotAreaAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ScreenFrame>(_ => throw new ScreenUnavailableException(ScreenUnavailableReason.WorkstationLocked, "The VERA machine is locked."));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => ReadCharacterMapAsync(screen));

        error.Message.ShouldBe("The VERA machine is locked.");
    }

    [Fact]
    public async Task A_minimized_window_gives_the_minimized_message()
    {
        using var charmap = await StartCharacterMapAsync();
        try
        {
            using var automation = new UIA3Automation();
            automation.FromHandle(charmap.MainWindowHandle).Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);
            await using var adapter = new DesktopAdapter(Substitute.For<IScreenshotSource>());
            await adapter.StartAsync(new AppTarget("Character Map", AppPlatform.Desktop, "", ProcessId: charmap.Id), Ct);

            var error = await Should.ThrowAsync<InvalidOperationException>(() => adapter.GetScreenStateAsync(Ct));

            error.Message.ShouldBe("The app's window is minimized. Restore it and try again.");
        }
        finally
        {
            charmap.Kill();
        }
    }

    [Fact]
    public async Task A_window_closed_after_attaching_gives_the_closed_message()
    {
        using var charmap = await StartCharacterMapAsync();
        await using var adapter = new DesktopAdapter(RealScreen());
        await adapter.StartAsync(new AppTarget("Character Map", AppPlatform.Desktop, "", ProcessId: charmap.Id), Ct);
        charmap.Kill();
        await charmap.WaitForExitAsync(Ct);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => adapter.GetScreenStateAsync(Ct));

        error.Message.ShouldBe("The app is closed or has no window.");
    }

    private static async Task<Process> StartCharacterMapAsync()
    {
        var charmap = Process.Start("charmap.exe");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (charmap.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, Ct);
            charmap.Refresh();
        }

        return charmap;
    }

    private static IScreenshotSource RealScreen() =>
        new ServiceCollection().AddLogging().AddVeraInput(new ConfigurationBuilder().Build()).BuildServiceProvider().GetRequiredService<IScreenshotSource>();

    private static async Task<ScreenState> ReadCharacterMapAsync(IScreenshotSource? screen = null)
    {
        using var charmap = await StartCharacterMapAsync();
        try
        {
            await using var adapter = new DesktopAdapter(screen ?? RealScreen());
            await adapter.StartAsync(new AppTarget("Character Map", AppPlatform.Desktop, "", ProcessId: charmap.Id), Ct);
            return await adapter.GetScreenStateAsync(Ct);
        }
        finally
        {
            charmap.Kill();
        }
    }
}
