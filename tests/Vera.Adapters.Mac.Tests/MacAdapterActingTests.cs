using Vera.Input.Testing;

namespace Vera.Adapters.Mac.Tests;

public sealed class MacAdapterActingTests
{
    /// <summary>A 1×1 PNG: FakeScreen needs one picture, though nothing here takes a screenshot.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Acting_before_reading_the_screen_throws_and_does_nothing()
    {
        var screen = new FakeScreen([OnePixelPng]);
        await using var adapter = new MacAdapter(screen);

        (await Should.ThrowAsync<InvalidOperationException>(() => adapter.ClickAsync(1, Ct))).Message.ShouldBe("Read the screen first.");
        (await Should.ThrowAsync<InvalidOperationException>(() => adapter.TypeAsync(1, "hi", Ct))).Message.ShouldBe("Read the screen first.");
        (await Should.ThrowAsync<InvalidOperationException>(() => adapter.PressKeyAsync("enter", Ct))).Message.ShouldBe("Read the screen first.");
        screen.Actions.ShouldBeEmpty();
    }
}
