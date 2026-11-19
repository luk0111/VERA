using static Vera.Adapters.Desktop.StepRecorder;

namespace Vera.Adapters.Desktop.Tests;

public sealed class StepRecorderTests
{
    private const uint Enter = 0x0D;
    private const uint Tab = 0x09;
    private const uint Esc = 0x1B;
    private const uint F5 = 0x74;
    private const uint Left = 0x25;
    private const uint A = 0x41;
    private const uint S = 0x53;
    private const uint V = 0x56;

    [Theory]
    [InlineData(Enter, "enter")]
    [InlineData(Tab, "tab")]
    [InlineData(Esc, "esc")]
    [InlineData(F5, "f5")]
    public void Action_keys_are_recorded(uint vk, string name) =>
        KeyName(vk, ctrl: false, shift: false, alt: false, Focus.Other).ShouldBe(name);

    [Fact]
    public void Combos_are_written_ctrl_shift_alt_then_the_key()
    {
        KeyName(S, ctrl: true, shift: false, alt: false, Focus.Other).ShouldBe("ctrl+s");
        KeyName(S, ctrl: true, shift: true, alt: false, Focus.Field).ShouldBe("ctrl+shift+s");
        KeyName(Tab, ctrl: false, shift: true, alt: false, Focus.Field).ShouldBe("shift+tab");
        KeyName(0x46, ctrl: false, shift: false, alt: true, Focus.Other).ShouldBe("alt+f");
    }

    [Theory]
    [InlineData(A, false)]
    [InlineData(A, true)]
    [InlineData(0x20u, false)]
    [InlineData(Left, false)]
    public void Plain_typing_is_left_to_the_fields_text(uint vk, bool shift)
    {
        KeyName(vk, ctrl: false, shift, alt: false, Focus.Field).ShouldBeNull();
        KeyName(vk, ctrl: false, shift, alt: false, Focus.Other).ShouldBeNull();
    }

    [Theory]
    [InlineData(A)]
    [InlineData(V)]
    [InlineData(Left)]
    public void Editing_combos_in_a_field_are_not_steps(uint vk)
    {
        KeyName(vk, ctrl: true, shift: false, alt: false, Focus.Field).ShouldBeNull();
        KeyName(vk, ctrl: true, shift: false, alt: false, Focus.TextArea).ShouldBeNull();
    }

    [Fact]
    public void Editing_combos_outside_a_field_are_steps() =>
        KeyName(V, ctrl: true, shift: false, alt: false, Focus.Other).ShouldBe("ctrl+v");

    [Fact]
    public void Enter_and_tab_in_a_text_area_are_part_of_the_text()
    {
        KeyName(Enter, ctrl: false, shift: false, alt: false, Focus.TextArea).ShouldBeNull();
        KeyName(Tab, ctrl: false, shift: false, alt: false, Focus.TextArea).ShouldBeNull();
        KeyName(Enter, ctrl: false, shift: false, alt: false, Focus.Field).ShouldBe("enter");
    }

    [Theory]
    [InlineData(0x11u)]
    [InlineData(0xA0u)]
    [InlineData(0x5Bu)]
    public void Modifiers_and_unknown_keys_alone_are_not_steps(uint vk) =>
        KeyName(vk, ctrl: false, shift: false, alt: false, Focus.Other).ShouldBeNull();
}
