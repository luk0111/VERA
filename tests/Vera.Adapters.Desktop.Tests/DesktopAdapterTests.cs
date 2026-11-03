using System.Buffers.Binary;
using System.Diagnostics;

using Vera.Core;

namespace Vera.Adapters.Desktop.Tests;

public sealed class DesktopAdapterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starting_without_a_process_id_is_not_supported()
    {
        await using var adapter = new DesktopAdapter();

        await Should.ThrowAsync<NotSupportedException>(() => adapter.StartAsync(new AppTarget("x", AppPlatform.Desktop, "x.exe"), Ct));
    }

    [Fact]
    public async Task Attaching_to_a_process_that_is_gone_throws()
    {
        await using var adapter = new DesktopAdapter();

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

    private static async Task<ScreenState> ReadCharacterMapAsync()
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

            await using var adapter = new DesktopAdapter();
            await adapter.StartAsync(new AppTarget("Character Map", AppPlatform.Desktop, "", ProcessId: charmap.Id), Ct);
            return await adapter.GetScreenStateAsync(Ct);
        }
        finally
        {
            charmap.Kill();
        }
    }
}
