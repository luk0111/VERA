using Microsoft.Extensions.Logging.Abstractions;

using Vera.Input.Capture;
using Vera.Input.Platform;

namespace Vera.Input.Tests.Capture;

/// <summary>Copies the real screen, so it needs an unlocked desktop. CI skips it.</summary>
[Trait("Category", "Integration")]
public sealed class GdiScreenGrabberTests
{
    [Fact]
    public void The_primary_monitor_is_copied_at_its_full_size()
    {
        new DpiAwarenessCheck(NullLogger<DpiAwarenessCheck>.Instance).EnsurePerMonitorV2();
        var primary = new Win32DisplayInfo().Read().Monitors.Single(m => m.IsPrimary);

        var frame = new GdiScreenGrabber(TimeProvider.System).Grab(primary.Bounds);

        frame.Bounds.ShouldBe(primary.Bounds);
        frame.Bgra.Length.ShouldBe(primary.Bounds.Width * primary.Bounds.Height * 4);
        frame.Bgra.ShouldContain(b => b != 0);
    }

    [Fact]
    public void A_part_of_the_screen_can_be_copied()
    {
        var frame = new GdiScreenGrabber(TimeProvider.System).Grab(new ScreenRect(10, 20, 30, 40));

        frame.Width.ShouldBe(30);
        frame.Height.ShouldBe(40);
        frame.Bgra.Length.ShouldBe(30 * 40 * 4);
    }
}