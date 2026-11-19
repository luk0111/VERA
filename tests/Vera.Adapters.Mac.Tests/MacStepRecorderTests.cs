namespace Vera.Adapters.Mac.Tests;

public sealed class MacStepRecorderTests
{
    [Theory]
    [InlineData((ushort)0x01, "s")]
    [InlineData((ushort)0x24, "enter")]
    [InlineData((ushort)0x35, "esc")]
    [InlineData((ushort)0x60, "f5")]
    [InlineData((ushort)0x33, "backspace")]
    [InlineData((ushort)0x7B, "left")]
    public void Mac_keys_get_the_same_names_as_on_windows(ushort keyCode, string name) => MacStepRecorder.KeyName(keyCode).ShouldBe(name);

    [Theory]
    [InlineData((ushort)0x37)]
    [InlineData((ushort)0x38)]
    [InlineData((ushort)0x3B)]
    public void Modifiers_have_no_name(ushort keyCode) => MacStepRecorder.KeyName(keyCode).ShouldBeNull();

    [Fact]
    public void Every_recorded_key_name_can_be_pressed_back() =>
        MacKeyCombo.MainKeys.Keys.ShouldAllBe(name => MacKeyCombo.Parse("ctrl+" + name).Text == "cmd+" + name);
}
