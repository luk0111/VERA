namespace Vera.Adapters.Mac.Tests;

public sealed class MacPickedWindowsTests
{
    private const int Safari = 2;

    [Fact]
    public void The_picked_window_is_used_when_another_open_one_became_the_main_window()
    {
        Picked().Choose(Safari, main: 11, [10, 11]).ShouldBe(10);
    }

    [Fact]
    public void The_picked_window_is_used_when_the_app_names_no_main_window()
    {
        Picked().Choose(Safari, main: null, [10, 11]).ShouldBe(10);
    }

    [Fact]
    public void A_window_the_app_opened_later_is_used_while_it_is_the_main_one()
    {
        var picked = Picked();

        picked.Choose(Safari, main: 12, [10, 11, 12]).ShouldBe(12);
        picked.Choose(Safari, main: 11, [10, 11, 12]).ShouldBe(10);
    }

    [Fact]
    public void After_the_picked_window_was_closed_the_main_window_is_used()
    {
        Picked().Choose(Safari, main: 11, [11]).ShouldBe(11);
    }

    [Fact]
    public void An_app_that_was_not_picked_keeps_its_main_window()
    {
        Picked().Choose(processId: 3, main: 30, [30, 31]).ShouldBe(30);
        new MacPickedWindows().Choose(Safari, main: null, []).ShouldBeNull();
    }

    [Fact]
    public void Picking_again_replaces_the_window()
    {
        var picked = Picked();

        picked.Remember(Safari, 11, [10, 11]);

        picked.Choose(Safari, main: 10, [10, 11]).ShouldBe(11);
    }

    [Fact]
    public void The_window_remembered_is_the_one_whose_title_the_picker_showed()
    {
        var shown = MacRunningApps.Shown(
        [
            new MacWindowInfo(Safari, "Safari", "", 0, 9),
            new MacWindowInfo(Safari, "Safari", "Apple", 0, 10),
            new MacWindowInfo(Safari, "Safari", "Other", 0, 11),
            new MacWindowInfo(3, "Notes", "", 0, 30),
            new MacWindowInfo(4, "Dock", "Dock", 20, 40),
        ]);

        shown.ShouldBe(new Dictionary<int, long> { [Safari] = 10, [3] = 30 }, ignoreOrder: true);
    }

    [Fact]
    public void The_title_is_the_picked_windows_even_with_another_in_front()
    {
        MacWindowInfo[] windows = [new(Safari, "Safari", "Other", 0, 11), new(Safari, "Safari", "Start Page", 0, 10)];

        MacRunningApps.TitleOf(Safari, windows, Picked()).ShouldBe("Start Page");
        MacRunningApps.TitleOf(Safari, windows, picked: null).ShouldBe("Other");
    }

    [Fact]
    public void The_title_of_a_window_the_app_opened_later_is_noticed()
    {
        MacRunningApps.TitleOf(Safari, [new(Safari, "Safari", "Settings", 0, 12), new(Safari, "Safari", "Start Page", 0, 10)], Picked()).ShouldBe("Settings");
    }

    [Fact]
    public void After_the_picked_window_was_closed_the_front_title_counts()
    {
        MacRunningApps.TitleOf(Safari, [new(Safari, "Safari", "Other", 0, 11)], Picked()).ShouldBe("Other");
    }

    private static MacPickedWindows Picked()
    {
        var picked = new MacPickedWindows();
        picked.Remember(Safari, 10, [10, 11]);
        return picked;
    }
}
