using Vera.Input.Injection;

namespace Vera.Input.Tests.Injection;

public sealed class KeyComboTests
{
    [Fact]
    public void A_single_key_is_one_key()
    {
        var combo = KeyCombo.Parse("enter");

        combo.Text.ShouldBe("enter");
        combo.Keys.ShouldBe([new ScanKey(0x1C)]);
    }

    [Theory]
    [InlineData("Ctrl+S")]
    [InlineData(" ctrl + s ")]
    [InlineData("CTRL+s")]
    public void Case_and_spaces_do_not_matter(string text)
    {
        var combo = KeyCombo.Parse(text);

        combo.Text.ShouldBe("ctrl+s");
        combo.Keys.ShouldBe([new ScanKey(0x1D), new ScanKey(0x1F)]);
    }

    [Fact]
    public void Modifiers_are_put_in_one_order_with_the_main_key_last()
    {
        var combo = KeyCombo.Parse("s+shift+ctrl");

        combo.Text.ShouldBe("ctrl+shift+s");
        combo.Keys.ShouldBe([new ScanKey(0x1D), new ScanKey(0x2A), new ScanKey(0x1F)]);
    }

    [Fact]
    public void A_modifier_alone_is_allowed()
    {
        KeyCombo.Parse("win").Keys.ShouldBe([new ScanKey(0x5B, Extended: true)]);
    }

    [Theory]
    [InlineData("left", 0x4B)]
    [InlineData("delete", 0x53)]
    [InlineData("home", 0x47)]
    [InlineData("pagedown", 0x51)]
    public void Grey_keys_are_extended(string name, int scanCode)
    {
        KeyCombo.Parse(name).Keys.ShouldBe([new ScanKey((ushort)scanCode, Extended: true)]);
    }

    [Theory]
    [InlineData("a", 0x1E)]
    [InlineData("z", 0x2C)]
    [InlineData("p", 0x19)]
    [InlineData("1", 0x02)]
    [InlineData("0", 0x0B)]
    [InlineData("f1", 0x3B)]
    [InlineData("f10", 0x44)]
    [InlineData("f12", 0x58)]
    [InlineData("esc", 0x01)]
    public void Scancodes_match_a_us_keyboard(string name, int scanCode)
    {
        KeyCombo.Parse(name).Keys.ShouldBe([new ScanKey((ushort)scanCode)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ctrl+")]
    [InlineData("ctrl++s")]
    [InlineData("ctrl+ctrl+s")]
    [InlineData("ctrl+a+b")]
    [InlineData("hyper+s")]
    [InlineData("f13")]
    public void Bad_combos_throw(string text)
    {
        Should.Throw<InvalidKeyComboException>(() => KeyCombo.Parse(text));
    }

    [Fact]
    public void The_message_lists_the_valid_names()
    {
        var e = Should.Throw<InvalidKeyComboException>(() => KeyCombo.Parse("escape"));

        e.Message.ShouldContain("Unknown key 'escape'");
        e.Message.ShouldContain("esc");
        e.Message.ShouldContain("pagedown");
    }

    [Theory]
    [InlineData("ctrl+alt+delete")]
    [InlineData("alt+ctrl+delete")]
    [InlineData("ctrl+alt+shift+delete")]
    [InlineData("win+l")]
    [InlineData("Win+L")]
    [InlineData("win+shift+l")]
    public void Refused_combos_throw(string text)
    {
        Should.Throw<InvalidKeyComboException>(() => KeyCombo.Parse(text));
    }

    [Theory]
    [InlineData("ctrl+delete")]
    [InlineData("ctrl+l")]
    [InlineData("win+r")]
    public void Combos_close_to_refused_ones_are_allowed(string text)
    {
        Should.NotThrow(() => KeyCombo.Parse(text));
    }
}
