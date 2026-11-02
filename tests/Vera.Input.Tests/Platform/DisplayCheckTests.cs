using Microsoft.Extensions.Logging.Abstractions;

using Vera.Input.Options;
using Vera.Input.Platform;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace Vera.Input.Tests.Platform;

public sealed class DisplayCheckTests
{
    private static readonly MonitorInfo Primary = new(0, @"\\.\DISPLAY1", new ScreenRect(0, 0, 1920, 1080), true, 96, 100, 60);
    private static readonly MonitorInfo Left = new(1, @"\\.\DISPLAY2", new ScreenRect(-2560, 0, 2560, 1440), false, 120, 125, 144);

    [Fact]
    public void The_primary_monitor_is_captured_by_default()
    {
        var report = DisplayCheck.Evaluate(Snapshot(Left with { Index = 0 }, Primary with { Index = 1 }), new DisplayOptions());

        report.CaptureMonitor.Index.ShouldBe(1);
    }

    [Fact]
    public void MonitorIndex_picks_that_monitor()
    {
        var report = DisplayCheck.Evaluate(Snapshot(Primary, Left), new DisplayOptions { MonitorIndex = 1 });

        report.CaptureMonitor.ShouldBe(Left);
    }

    [Fact]
    public void A_MonitorIndex_that_does_not_exist_throws()
    {
        var e = Should.Throw<DisplayMismatchException>(() => DisplayCheck.Evaluate(Snapshot(Primary), new DisplayOptions { MonitorIndex = 3 }));

        e.Message.ShouldContain("MonitorIndex is 3");
    }

    [Fact]
    public void Run_makes_the_process_dpi_aware_before_reading_the_display()
    {
        var dpi = Substitute.For<IDpiAwarenessCheck>();
        var displayInfo = Substitute.For<IDisplayInfo>();
        displayInfo.Read().Returns(Snapshot(Primary));

        new DisplayCheck(dpi, displayInfo, MsOptions.Create(new VeraInputOptions()), NullLogger<DisplayCheck>.Instance).Run();

        Received.InOrder(() =>
        {
            dpi.EnsurePerMonitorV2();
            displayInfo.Read();
        });
    }

    private static DisplaySnapshot Snapshot(params MonitorInfo[] monitors) =>
        new(new ScreenRect(-2560, 0, 4480, 1440), monitors, IsRemoteSession: false);
}
