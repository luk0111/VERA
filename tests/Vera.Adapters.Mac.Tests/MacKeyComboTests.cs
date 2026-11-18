using Vera.Input;

namespace Vera.Adapters.Mac.Tests;

public sealed class MacKeyComboTests
{
    private const ushort Command = 0x37;
    private const ushort Shift = 0x38;
    private const ushort Option = 0x3A;
    private const ushort S = 0x01;

    [Theory]
    [InlineData("ctrl+s")]
    [InlineData("cmd+s")]
    [InlineData("Ctrl+S")]
    [InlineData(" CMD + s ")]
    public void Ctrl_and_cmd_both_press_command(string text)
    {
        var combo = MacKeyCombo.Parse(text);

        combo.Text.ShouldBe("cmd+s");
        combo.Keys.ShouldBe([new MacKey(Command, MacKeyCombo.CommandFlag), new MacKey(S)]);
    }

    [Fact]
    public void Modifiers_are_put_in_one_order_with_the_main_key_last()
    {
        var combo = MacKeyCombo.Parse("s+alt+shift+ctrl");

        combo.Text.ShouldBe("cmd+shift+alt+s");
        combo.Keys.Select(k => k.KeyCode).ShouldBe([Command, Shift, Option, S]);
    }

    [Theory]
    [InlineData("enter", 0x24)]
    [InlineData("backspace", 0x33)]
    [InlineData("delete", 0x75)]
    [InlineData("a", 0x00)]
    [InlineData("z", 0x06)]
    [InlineData("0", 0x1D)]
    [InlineData("f1", 0x7A)]
    [InlineData("f12", 0x6F)]
    [InlineData("left", 0x7B)]
    [InlineData("esc", 0x35)]
    public void Keys_have_their_mac_key_codes(string name, int keyCode)
    {
        MacKeyCombo.Parse(name).Keys.ShouldBe([new MacKey((ushort)keyCode)]);
    }

    [Fact]
    public void The_same_key_names_as_windows_except_insert()
    {
        MacKeyCombo.MainKeys.Count.ShouldBe(26 + 10 + 12 + 14);
        MacKeyCombo.MainKeys.Values.Select(k => k.KeyCode).Distinct().Count().ShouldBe(MacKeyCombo.MainKeys.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cmd+")]
    [InlineData("ctrl+cmd+s")]
    [InlineData("cmd+a+b")]
    [InlineData("hyper+s")]
    [InlineData("win+l")]
    [InlineData("insert")]
    public void Bad_combos_are_refused(string text)
    {
        Should.Throw<InvalidKeyComboException>(() => MacKeyCombo.Parse(text));
    }
}
