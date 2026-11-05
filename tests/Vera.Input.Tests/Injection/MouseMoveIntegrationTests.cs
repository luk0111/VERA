using System.Globalization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Vera.Input.Coordinates;
using Vera.Input.Platform;

namespace Vera.Input.Tests.Injection;

/// <summary>
/// Moves the real mouse (no clicks) and checks it lands on the exact screen pixel. Needs an unlocked desktop. CI skips it.
/// Puts the mouse back roughly where it was afterwards. Don't touch the mouse while it runs: real movement in the
/// 20 ms between a move and the check shows up as misses.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MouseMoveIntegrationTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public async Task Moves_land_on_the_exact_screen_pixel(double scale)
    {
        using var services = Services(scale);
        var screen = services.GetRequiredService<DesktopScreen>();
        var cursor = services.GetRequiredService<ICursorPosition>();
        var frame = screen.CurrentGeometry();
        var start = cursor.Read();

        var misses = new List<string>();
        foreach (var point in Points(frame))
        {
            await screen.MoveAsync(point.X, point.Y, TestContext.Current.CancellationToken);
            var expected = CoordinateMapper.ImageToScreen(frame, point);
            var actual = cursor.Read();
            if (actual != expected)
            {
                misses.Add($"{point} → {actual}, expected {expected}");
            }
        }

        if (frame.CaptureBounds.Contains(start.X, start.Y))
        {
            var back = CoordinateMapper.ScreenToImage(frame, start);
            await screen.MoveAsync(back.X, back.Y, TestContext.Current.CancellationToken);
        }

        misses.ShouldBeEmpty();
    }

    /// <summary>The 4 corners, the centre and 50 random points (always the same ones).</summary>
    private static IEnumerable<ImagePoint> Points(FrameGeometry frame)
    {
        int right = frame.ImageWidth - 1, bottom = frame.ImageHeight - 1;
        yield return new ImagePoint(0, 0);
        yield return new ImagePoint(right, 0);
        yield return new ImagePoint(0, bottom);
        yield return new ImagePoint(right, bottom);
        yield return new ImagePoint(right / 2, bottom / 2);

        var random = new Random(42);
        for (var i = 0; i < 50; i++)
        {
            yield return new ImagePoint(random.Next(frame.ImageWidth), random.Next(frame.ImageHeight));
        }
    }

    private static ServiceProvider Services(double scale)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VeraInput:Capture:DefaultScale"] = scale.ToString(CultureInfo.InvariantCulture),
                ["VeraInput:Delays:AfterMove"] = "20",
            })
            .Build();
        return new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddVeraInput(config)
            .BuildServiceProvider();
    }
}
