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

    private static readonly ScreenRect Window = new(100, 200, 400, 300);

    [Fact]
    public async Task Starting_without_a_process_id_is_not_supported()
    {
        await using var adapter = new DesktopAdapter(Substitute.For<IScreen>());

        await Should.ThrowAsync<NotSupportedException>(() => adapter.StartAsync(new AppTarget("x", AppPlatform.Desktop, "x.exe"), Ct));
    }

    [Fact]
    public async Task Attaching_to_a_process_that_is_gone_throws()
    {
        await using var adapter = new DesktopAdapter(Substitute.For<IScreen>());

        await Should.ThrowAsync<InvalidOperationException>(() => adapter.StartAsync(new AppTarget("x", AppPlatform.Desktop, "", ProcessId: int.MaxValue), Ct));
    }

    [Fact]
    public async Task Acting_before_reading_the_screen_throws_and_does_nothing()
    {
        var screen = Substitute.For<IScreen>();
        await using var adapter = new DesktopAdapter(screen);

        (await Should.ThrowAsync<InvalidOperationException>(() => adapter.ClickAsync(1, Ct))).Message.ShouldBe("Read the screen first.");
        (await Should.ThrowAsync<InvalidOperationException>(() => adapter.TypeAsync(1, "hi", Ct))).Message.ShouldBe("Read the screen first.");
        (await Should.ThrowAsync<InvalidOperationException>(() => adapter.PressKeyAsync("enter", Ct))).Message.ShouldBe("Read the screen first.");
        screen.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void An_element_is_clicked_in_its_centre_in_screenshot_pixels()
    {
        // Element at 120..200 × 220..250 on screen; the screenshot starts at the window's corner (100, 200).
        DesktopAdapter.ClickPoint(new ScreenRect(120, 220, 80, 30), Window).ShouldBe((60, 35));
    }

    [Fact]
    public void Only_the_part_inside_the_window_counts()
    {
        // Sticks out 100 px past the right edge (500): the visible part is 450..500.
        DesktopAdapter.ClickPoint(new ScreenRect(450, 300, 150, 20), Window).ShouldBe((375, 110));
    }

    [Fact]
    public void An_element_outside_the_window_has_no_click_point()
    {
        DesktopAdapter.ClickPoint(new ScreenRect(500, 300, 50, 20), Window).ShouldBeNull();
        DesktopAdapter.ClickPoint(new ScreenRect(0, 0, 100, 200), Window).ShouldBeNull();
    }

    [Fact]
    public void A_click_point_never_leaves_the_screenshot()
    {
        // A 1-pixel element in the window's bottom-right corner.
        DesktopAdapter.ClickPoint(new ScreenRect(499, 499, 1, 1), Window).ShouldBe((399, 299));
    }
}

/// <summary>Reads Character Map, a classic Win32 window on every Windows install, so it needs an unlocked desktop. CI skips it.</summary>
[Trait("Category", "Integration")]
[Collection(CharacterMapTests.Name)]
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
        var screen = Substitute.For<IScreen>();
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
        var screen = Substitute.For<IScreen>();
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
            await using var adapter = new DesktopAdapter(Substitute.For<IScreen>());
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

    internal static async Task<Process> StartCharacterMapAsync()
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

    internal static IScreen RealScreen() =>
        new ServiceCollection().AddLogging().AddVeraInput(new ConfigurationBuilder().Build()).BuildServiceProvider().GetRequiredService<IScreen>();

    private static async Task<ScreenState> ReadCharacterMapAsync(IScreen? screen = null)
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

/// <summary>Character Map tests share one window and the real mouse, so they run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CharacterMapTests
{
    public const string Name = "Character Map";
}

/// <summary>
/// Clicks, types and presses keys in Character Map with the real mouse and keyboard, then reads the result back.
/// Processes started by Claude Code can't send input, so a person runs these from their own terminal, without touching the mouse. CI skips them.
/// </summary>
[Trait("Category", "Integration")]
[Collection(CharacterMapTests.Name)]
public sealed class DesktopAdapterActingTests
{
    private const string Field = "Characters to copy :";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public Task Typing_replaces_the_text_in_a_field() => WithCharacterMapAsync(async (adapter, _) =>
    {
        await adapter.TypeAsync(await IdAsync(adapter, "Edit", Field), "hello", Ct);
        (await FieldTextAsync(adapter)).ShouldBe("hello");

        await adapter.TypeAsync(await IdAsync(adapter, "Edit", Field), "bye", Ct);
        (await FieldTextAsync(adapter)).ShouldBe("bye");
    });

    [Fact]
    public Task Clicking_Select_adds_the_selected_character() => WithCharacterMapAsync(async (adapter, _) =>
    {
        var before = await FieldTextAsync(adapter) ?? "";

        await adapter.ClickAsync(await IdAsync(adapter, "Button", "Select"), Ct);

        (await FieldTextAsync(adapter) ?? "").Length.ShouldBe(before.Length + 1);
    });

    [Fact]
    public Task A_key_is_pressed_in_the_app() => WithCharacterMapAsync(async (adapter, _) =>
    {
        await adapter.TypeAsync(await IdAsync(adapter, "Edit", Field), "abc", Ct);

        await adapter.PressKeyAsync("Backspace", Ct);

        (await FieldTextAsync(adapter)).ShouldBe("ab");
    });

    [Fact]
    public Task An_element_not_on_the_latest_screen_is_refused() => WithCharacterMapAsync(async (adapter, _) =>
    {
        await adapter.GetScreenStateAsync(Ct);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => adapter.ClickAsync(9999, Ct));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => adapter.ClickAsync(0, Ct)); // 0 is the window itself
    });

    [Fact]
    public Task A_window_moved_after_reading_is_not_clicked() => WithCharacterMapAsync(async (adapter, charmap) =>
    {
        var select = await IdAsync(adapter, "Button", "Select");
        using var automation = new UIA3Automation();
        var window = automation.FromHandle(charmap.MainWindowHandle);
        var r = window.BoundingRectangle;
        window.Patterns.Transform.Pattern.Move(r.X + 40, r.Y + 40);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => adapter.ClickAsync(select, Ct));

        error.Message.ShouldBe("The window moved since VERA last looked at it. Read it again.");
    });

    // Reads the screen and returns the element's id in that reading; ids change between readings.
    private static async Task<int> IdAsync(DesktopAdapter adapter, string role, string name)
    {
        var state = await adapter.GetScreenStateAsync(Ct);
        return state.Elements.Single(e => e.Role == role && e.Name == name).Id;
    }

    // The field is a RichEdit: while it has the focus, its text ends with an end-of-text "\r" that nobody typed.
    private static async Task<string?> FieldTextAsync(DesktopAdapter adapter)
    {
        var state = await adapter.GetScreenStateAsync(Ct);
        return state.Elements.Single(e => e.Role == "Edit" && e.Name == Field).Value?.TrimEnd('\r');
    }

    private static async Task WithCharacterMapAsync(Func<DesktopAdapter, Process, Task> test)
    {
        using var charmap = await DesktopAdapterIntegrationTests.StartCharacterMapAsync();
        try
        {
            await using var adapter = new DesktopAdapter(DesktopAdapterIntegrationTests.RealScreen());
            await adapter.StartAsync(new AppTarget("Character Map", AppPlatform.Desktop, "", ProcessId: charmap.Id), Ct);
            await test(adapter, charmap);
        }
        finally
        {
            charmap.Kill();
        }
    }
}
