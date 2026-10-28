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
    public void The_primary_monitor_is_captured_by_default_and_matches()
    {
        var report = DisplayCheck.Evaluate(Snapshot(Primary, Left), new DisplayOptions());

        report.CaptureMonitor.ShouldBe(Primary);
        report.Mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void MonitorIndex_picks_that_monitor_and_compares_it()
    {
        var report = DisplayCheck.Evaluate(
            Snapshot(Primary, Left),
            new DisplayOptions { CaptureTarget = CaptureTarget.MonitorIndex, MonitorIndex = 1 });

        report.CaptureMonitor.ShouldBe(Left);
        report.Mismatches.ShouldBe(
        [
            "Monitor 1 is 2560×1440, expected 1920×1080.",
            "Monitor 1 is scaled 125%, expected 100%.",
        ]);
    }

    [Fact]
    public void Expected_values_come_from_the_options()
    {
        var report = DisplayCheck.Evaluate(
            Snapshot(Primary, Left),
            new DisplayOptions { CaptureTarget = CaptureTarget.MonitorIndex, MonitorIndex = 1, ExpectedWidth = 2560, ExpectedHeight = 1440, ExpectedScalePercent = 125 });

        report.Mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_capture_monitor_is_compared()
    {
        var report = DisplayCheck.Evaluate(Snapshot(Left with { IsPrimary = false }, Primary with { Index = 1 }), new DisplayOptions());

        report.CaptureMonitor.Index.ShouldBe(1);
        report.Mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void A_MonitorIndex_that_does_not_exist_throws()
    {
        var e = Should.Throw<DisplayMismatchException>(() => DisplayCheck.Evaluate(
            Snapshot(Primary),
            new DisplayOptions { CaptureTarget = CaptureTarget.MonitorIndex, MonitorIndex = 3 }));

        e.Message.ShouldContain("MonitorIndex is 3");
    }

    [Fact]
    public void Run_only_warns_about_a_mismatch_when_not_strict()
    {
        var report = Check(new DisplayOptions { ExpectedScalePercent = 150 }, Snapshot(Primary)).Run();

        report.Mismatches.ShouldHaveSingleItem();
    }

    [Fact]
    public void Run_throws_on_a_mismatch_when_strict()
    {
        var check = Check(new DisplayOptions { ExpectedScalePercent = 150, StrictMismatch = true }, Snapshot(Primary));

        var e = Should.Throw<DisplayMismatchException>(check.Run);

        e.Message.ShouldContain("scaled 100%, expected 150%");
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

    private static DisplayCheck Check(DisplayOptions display, DisplaySnapshot snapshot)
    {
        var displayInfo = Substitute.For<IDisplayInfo>();
        displayInfo.Read().Returns(snapshot);
        return new DisplayCheck(
            Substitute.For<IDpiAwarenessCheck>(),
            displayInfo,
            MsOptions.Create(new VeraInputOptions { Display = display }),
            NullLogger<DisplayCheck>.Instance);
    }

    private static DisplaySnapshot Snapshot(params MonitorInfo[] monitors) =>
        new(new ScreenRect(-2560, 0, 4480, 1440), monitors, IsRemoteSession: false);
}