using System.Buffers.Binary;

using Microsoft.Extensions.Time.Testing;

using Vera.Input.Testing;

namespace Vera.Input.Tests.Testing;

public sealed class FakeScreenTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Frames_come_back_in_order_and_start_over_after_the_last()
    {
        var screen = new FakeScreen([Png(100, 50), Png(200, 80)]);

        var sizes = new List<(int, int)>();
        for (var i = 0; i < 3; i++)
        {
            var frame = await screen.ScreenshotAsync(ct: Ct);
            sizes.Add((frame.Width, frame.Height));
        }

        sizes.ShouldBe([(100, 50), (200, 80), (100, 50)]);
    }

    [Fact]
    public async Task A_frame_carries_its_png_sequence_time_and_scale_1()
    {
        var png = Png(100, 50);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var screen = new FakeScreen([png], time);

        var first = await screen.ScreenshotAsync(0.5, Ct);
        time.Advance(TimeSpan.FromSeconds(2));
        var second = await screen.ScreenshotAsync(ct: Ct);

        first.ShouldBe(new ScreenFrame(100, 50, 1, 100, 50, png, new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero), 1));
        second.Sequence.ShouldBe(2);
        second.CapturedAt.ShouldBe(new DateTimeOffset(2026, 10, 28, 12, 0, 2, TimeSpan.Zero));
    }

    [Fact]
    public async Task Every_call_is_recorded_with_its_arguments()
    {
        var screen = new FakeScreen([Png(100, 50)]);
        var options = new TypeTextOptions(Sensitive: true);

        await screen.ScreenshotAsync(0.5, Ct);
        await screen.MoveAsync(1, 2, Ct);
        await screen.ClickAsync(3, 4, MouseButton.Right, 2, Ct);
        await screen.ScrollAsync(5, 6, -3, 1, Ct);
        await screen.DragAsync(7, 8, 99, 49, MouseButton.Middle, Ct);
        await screen.TypeTextAsync("ăîș", options, Ct);
        await screen.KeyAsync("ctrl+s", 3, Ct);

        screen.Actions.ShouldBe(
        [
            new Screenshot(0.5),
            new Moved(1, 2),
            new Clicked(3, 4, MouseButton.Right, 2),
            new Scrolled(5, 6, -3, 1),
            new Dragged(7, 8, 99, 49, MouseButton.Middle),
            new TypedText("ăîș", options),
            new KeyPressed("ctrl+s", 3),
        ]);
    }

    [Fact]
    public async Task Defaults_are_recorded_too()
    {
        var screen = new FakeScreen([Png(100, 50)]);

        await screen.ScreenshotAsync(ct: Ct);
        await screen.ClickAsync(3, 4, ct: Ct);
        await screen.TypeTextAsync("hi", ct: Ct);
        await screen.KeyAsync("enter", ct: Ct);

        screen.Actions.ShouldBe(
        [
            new Screenshot(null),
            new Clicked(3, 4, MouseButton.Left, 1),
            new TypedText("hi", null),
            new KeyPressed("enter", 1),
        ]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(100, 0)]
    [InlineData(0, 50)]
    public async Task Coordinates_outside_the_frame_throw_and_are_not_recorded(int x, int y)
    {
        var screen = new FakeScreen([Png(100, 50)]);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => screen.ClickAsync(x, y, ct: Ct));

        screen.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_frame_corners_are_inside()
    {
        var screen = new FakeScreen([Png(100, 50)]);

        await screen.ClickAsync(0, 0, ct: Ct);
        await screen.ClickAsync(99, 49, ct: Ct);

        screen.Actions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Coordinates_are_checked_against_the_latest_frame()
    {
        var screen = new FakeScreen([Png(100, 50), Png(200, 80)]);

        await screen.ScreenshotAsync(ct: Ct);
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => screen.MoveAsync(150, 10, Ct));

        await screen.ScreenshotAsync(ct: Ct);
        await screen.MoveAsync(150, 10, Ct);
    }

    [Fact]
    public async Task A_drag_whose_end_is_outside_the_frame_throws()
    {
        var screen = new FakeScreen([Png(100, 50)]);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => screen.DragAsync(0, 0, 100, 0, ct: Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task An_empty_combo_throws_InvalidKeyComboException(string combo)
    {
        var screen = new FakeScreen([Png(100, 50)]);

        await Should.ThrowAsync<InvalidKeyComboException>(() => screen.KeyAsync(combo, ct: Ct));
    }

    [Fact]
    public async Task A_cancelled_call_throws_and_is_not_recorded()
    {
        var screen = new FakeScreen([Png(100, 50)]);

        await Should.ThrowAsync<OperationCanceledException>(() => screen.ClickAsync(1, 1, ct: new CancellationToken(canceled: true)));

        screen.Actions.ShouldBeEmpty();
    }

    [Fact]
    public void Bytes_that_are_not_a_png_are_rejected()
    {
        Should.Throw<InvalidDataException>(() => new FakeScreen([[1, 2, 3]]));
    }

    [Fact]
    public async Task FromFolder_plays_the_pngs_sorted_by_name()
    {
        var folder = Directory.CreateTempSubdirectory("vera-fakescreen-");
        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "02.png"), Png(20, 2));
            File.WriteAllBytes(Path.Combine(folder.FullName, "01.png"), Png(10, 1));
            File.WriteAllText(Path.Combine(folder.FullName, "notes.txt"), "ignored");

            var screen = FakeScreen.FromFolder(folder.FullName);

            (await screen.ScreenshotAsync(ct: Ct)).Width.ShouldBe(10);
            (await screen.ScreenshotAsync(ct: Ct)).Width.ShouldBe(20);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void FromFolder_without_pngs_throws()
    {
        var folder = Directory.CreateTempSubdirectory("vera-fakescreen-");
        try
        {
            Should.Throw<ArgumentException>(() => FakeScreen.FromFolder(folder.FullName));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task An_area_screenshot_returns_the_next_frame_and_is_recorded()
    {
        var screen = new FakeScreen([Png(100, 50), Png(200, 80)]);

        await screen.ScreenshotAsync(ct: Ct);
        var frame = await screen.ScreenshotAreaAsync(10, 20, 30, 40, Ct);

        (frame.Width, frame.Height, frame.Sequence).ShouldBe((200, 80, 2));
        screen.Actions[^1].ShouldBe(new AreaScreenshot(10, 20, 30, 40));
    }

    // Only the PNG signature and the IHDR size fields matter to FakeScreen; the rest is not decoded.
    private static byte[] Png(int width, int height)
    {
        var png = new byte[33];
        byte[] header = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R'];
        header.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        return png;
    }
}