namespace Vera.Adapters.Mac.Tests;

public sealed class MacStopKeyTests
{
    private const ushort Esc = 0x35;

    [Fact]
    public void Esc_from_the_tester_stops_the_run() => MacStopKey.Stops(Esc, sourceProcessId: 0).ShouldBeTrue();

    [Fact]
    public void Esc_sent_by_VERA_does_not_stop_the_run() => MacStopKey.Stops(Esc, Environment.ProcessId).ShouldBeFalse();

    [Theory]
    [InlineData((ushort)0x24)]
    [InlineData((ushort)0x01)]
    public void Other_keys_do_not_stop_the_run(ushort keyCode) => MacStopKey.Stops(keyCode, sourceProcessId: 0).ShouldBeFalse();
}
