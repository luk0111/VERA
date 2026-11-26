using System.Threading.Channels;

namespace Vera.Adapters.Desktop.Tests;

public sealed class StepRecorderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0x0Du, "enter")]
    [InlineData(0x1Bu, "esc")]
    [InlineData(0x74u, "f5")]
    [InlineData(0x53u, "s")]
    [InlineData(0x37u, "7")]
    [InlineData(0x25u, "left")]
    public void Windows_keys_get_veras_names(uint vk, string name) => StepRecorder.KeyName(vk).ShouldBe(name);

    [Theory]
    [InlineData(0x11u)]
    [InlineData(0xA0u)]
    [InlineData(0x5Bu)]
    public void Modifiers_and_unknown_windows_keys_have_no_name(uint vk) => StepRecorder.KeyName(vk).ShouldBeNull();

    [Theory]
    [InlineData("enter")]
    [InlineData("tab")]
    [InlineData("esc")]
    [InlineData("f5")]
    [InlineData("f12")]
    public void Action_keys_are_steps(string key) => StepRecording.KeyName(key, ctrl: false, shift: false, alt: false, Focus.Other).ShouldBe(key);

    [Fact]
    public void Combos_are_written_ctrl_shift_alt_then_the_key()
    {
        StepRecording.KeyName("s", ctrl: true, shift: false, alt: false, Focus.Other).ShouldBe("ctrl+s");
        StepRecording.KeyName("s", ctrl: true, shift: true, alt: false, Focus.Field).ShouldBe("ctrl+shift+s");
        StepRecording.KeyName("tab", ctrl: false, shift: true, alt: false, Focus.Field).ShouldBe("shift+tab");
        StepRecording.KeyName("f", ctrl: false, shift: false, alt: true, Focus.Other).ShouldBe("alt+f");
    }

    [Theory]
    [InlineData("a", false)]
    [InlineData("a", true)]
    [InlineData("space", false)]
    [InlineData("left", false)]
    [InlineData("f", false)]
    public void Plain_typing_is_left_to_the_fields_text(string key, bool shift)
    {
        StepRecording.KeyName(key, ctrl: false, shift, alt: false, Focus.Field).ShouldBeNull();
        StepRecording.KeyName(key, ctrl: false, shift, alt: false, Focus.Other).ShouldBeNull();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("v")]
    [InlineData("left")]
    public void Editing_combos_in_a_field_are_not_steps(string key)
    {
        StepRecording.KeyName(key, ctrl: true, shift: false, alt: false, Focus.Field).ShouldBeNull();
        StepRecording.KeyName(key, ctrl: true, shift: false, alt: false, Focus.TextArea).ShouldBeNull();
    }

    [Fact]
    public void Editing_combos_outside_a_field_are_steps() =>
        StepRecording.KeyName("v", ctrl: true, shift: false, alt: false, Focus.Other).ShouldBe("ctrl+v");

    [Fact]
    public void Enter_and_tab_in_a_text_area_are_part_of_the_text()
    {
        StepRecording.KeyName("enter", ctrl: false, shift: false, alt: false, Focus.TextArea).ShouldBeNull();
        StepRecording.KeyName("tab", ctrl: false, shift: false, alt: false, Focus.TextArea).ShouldBeNull();
        StepRecording.KeyName("enter", ctrl: false, shift: false, alt: false, Focus.Field).ShouldBe("enter");
    }

    [Fact]
    public async Task Typing_in_a_clicked_field_is_written_when_the_tester_moves_on()
    {
        var search = new FakeElement("Edit", "Search", "", "cats");
        var go = new FakeElement("Button", "Go");
        var app = new FakeApp { [(10, 10)] = search, [(50, 10)] = go };

        var actions = await RecordAsync(app, new ClickInput(10, 10), new ClickInput(50, 10));

        actions.ShouldBe([
            new RecordedClick("Edit", "Search"),
            new RecordedType("Edit", "Search", "cats"),
            new RecordedClick("Button", "Go"),
        ]);
    }

    [Fact]
    public async Task An_action_key_writes_the_typing_first_then_the_key()
    {
        var search = new FakeElement("Edit", "Search", "", "cats");
        var app = new FakeApp { [(10, 10)] = search };

        var actions = await RecordAsync(app, new ClickInput(10, 10), Key("enter"));

        actions.ShouldBe([new RecordedClick("Edit", "Search"), new RecordedType("Edit", "Search", "cats"), new RecordedPress("enter")]);
    }

    [Fact]
    public async Task Typing_without_a_click_uses_the_focused_field_and_is_written_at_the_end()
    {
        var app = new FakeApp { Focused = new FakeElement("Edit", "Search", "cats") };

        var actions = await RecordAsync(app, Key("c"));

        actions.ShouldBe([new RecordedType("Edit", "Search", "cats")]);
    }

    [Fact]
    public async Task A_field_left_unchanged_writes_no_typing()
    {
        var app = new FakeApp { [(10, 10)] = new FakeElement("Edit", "Search", "old") };

        var actions = await RecordAsync(app, new ClickInput(10, 10));

        actions.ShouldBe([new RecordedClick("Edit", "Search")]);
    }

    [Fact]
    public async Task Password_fields_are_never_read()
    {
        var password = new FakeElement("Edit", "Password", "secret") { IsPassword = true };
        var app = new FakeApp { [(10, 10)] = password };

        var actions = await RecordAsync(app, new ClickInput(10, 10), Key("p"));

        actions.Count.ShouldBe(2);
        actions[0].ShouldBe(new RecordedClick("Edit", "Password"));
        actions[1].ShouldBeOfType<RecordedSkip>().Reason.ShouldContain("{{secret:name}}");
        password.TextReads.ShouldBe(0);
    }

    [Fact]
    public async Task Nameless_elements_and_clicks_outside_the_app_are_not_steps()
    {
        var app = new FakeApp { [(10, 10)] = new FakeElement("Button", " ") };

        var actions = await RecordAsync(app, new ClickInput(10, 10), new ClickInput(99, 99));

        actions.ShouldHaveSingleItem().ShouldBeOfType<RecordedSkip>().Reason.ShouldContain("no name");
    }

    [Fact]
    public async Task Text_with_several_lines_is_skipped()
    {
        var app = new FakeApp { [(10, 10)] = new FakeElement("Document", "Text editor", "", "a\r\nb") { IsTextArea = true } };

        var actions = await RecordAsync(app, new ClickInput(10, 10), Key("esc"));

        actions.Select(a => a.GetType()).ShouldBe([typeof(RecordedClick), typeof(RecordedSkip), typeof(RecordedPress)]);
        ((RecordedSkip)actions[1]).Reason.ShouldContain("several lines");
    }

    [Fact]
    public async Task A_name_on_several_lines_is_written_on_one()
    {
        var app = new FakeApp
        {
            [(10, 10)] = new FakeElement("Button", "Save\r\n  file"),
            [(20, 20)] = new FakeElement("Edit", "Notes\nfor today", "", "cats"),
        };

        var actions = await RecordAsync(app, new ClickInput(10, 10), new ClickInput(20, 20), Key("esc"));

        actions.ShouldBe([
            new RecordedClick("Button", "Save file"),
            new RecordedClick("Edit", "Notes for today"),
            new RecordedType("Edit", "Notes for today", "cats"),
            new RecordedPress("esc"),
        ]);
    }

    private static KeyInput Key(string key) => new(key, Ctrl: false, Shift: false, Alt: false);

    private static async Task<List<RecordedAction>> RecordAsync(FakeApp app, params HookInput[] inputs)
    {
        var channel = Channel.CreateUnbounded<HookInput>();
        foreach (var input in inputs)
        {
            await channel.Writer.WriteAsync(input, Ct);
        }

        channel.Writer.Complete();
        var actions = new List<RecordedAction>();
        await foreach (var action in StepRecording.RecordAsync(channel.Reader, app, Ct))
        {
            actions.Add(action);
        }

        return actions;
    }

    /// <summary>An element whose text reads as each of <paramref name="texts"/> in turn, then stays at the last: what the tester typed between two reads.</summary>
    private sealed class FakeElement(string role, string name, params string[] texts)
    {
        public string Role { get; } = role;

        public string Name { get; } = name;

        public bool IsPassword { get; init; }

        public bool IsTextArea { get; init; }

        public int TextReads { get; private set; }

        public string? ReadText() => texts.Length == 0 ? null : texts[Math.Min(TextReads++, texts.Length - 1)];
    }

    private sealed class FakeApp : Dictionary<(int X, int Y), FakeElement>, IRecordedElements<FakeElement>
    {
        public FakeElement? Focused { get; init; }

        public FakeElement? At(int x, int y) => TryGetValue((x, y), out var e) ? e : null;

        public FakeElement? FocusedTextField() => Focused;

        public string Role(FakeElement element) => element.Role;

        public string Name(FakeElement element) => element.Name;

        public bool IsPassword(FakeElement element) => element.IsPassword;

        public bool IsTextArea(FakeElement element) => element.IsTextArea;

        public string? Text(FakeElement element) => element.ReadText();
    }
}
