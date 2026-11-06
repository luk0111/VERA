using Vera.Adapters.Mac;
using Vera.Core;

namespace Vera.Adapters.Mac.Tests;

public sealed class MacRunningAppsTests
{
    private const int OwnProcess = 1;

    [Fact]
    public void One_row_is_kept_per_app_with_its_first_title()
    {
        var apps = MacRunningApps.Pick(
            [new MacWindowInfo(2, "Safari", "", 0), new MacWindowInfo(2, "Safari", "Apple", 0), new MacWindowInfo(2, "Safari", "Other", 0)],
            OwnProcess);

        apps.ShouldBe([new RunningAppInfo(2, "Safari", "Apple")]);
    }

    [Fact]
    public void An_app_stays_listed_without_a_title() =>
        MacRunningApps.Pick([new MacWindowInfo(2, "Safari", "", 0)], OwnProcess).ShouldBe([new RunningAppInfo(2, "Safari", "")]);

    [Fact]
    public void System_layers_and_VERA_itself_are_left_out()
    {
        var apps = MacRunningApps.Pick(
            [new MacWindowInfo(3, "Dock", "", 20), new MacWindowInfo(OwnProcess, "Vera.Cli", "", 0), new MacWindowInfo(4, "", "", 0)],
            OwnProcess);

        apps.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void The_real_window_list_can_be_read_on_a_Mac()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Needs macOS.");

        Should.NotThrow(() => new MacRunningApps().List());
    }
}
