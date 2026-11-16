namespace Vera.Adapters.Desktop.Tests;

public sealed class ForegroundSwitchTests
{
    private const nint Window = 42;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_app_already_in_front_is_left_alone()
    {
        var api = new FakeForegroundApi(worksAfter: 0);

        (await Switch(api).BringToFrontAsync(Window, Ct)).ShouldBeFalse();

        api.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_plain_request_that_works_is_enough()
    {
        var api = new FakeForegroundApi(worksAfter: 1);

        (await Switch(api).BringToFrontAsync(Window, Ct)).ShouldBeTrue();

        api.Calls.ShouldBe(["request"]);
    }

    [Fact]
    public async Task A_refused_request_is_tried_again_after_an_empty_input()
    {
        var api = new FakeForegroundApi(worksAfter: 3);

        (await Switch(api).BringToFrontAsync(Window, Ct)).ShouldBeTrue();

        api.Calls.ShouldBe(["request", "input", "request"]);
    }

    [Fact]
    public async Task The_last_try_shares_the_input_of_the_window_in_front()
    {
        var api = new FakeForegroundApi(worksAfter: 4);

        (await Switch(api).BringToFrontAsync(Window, Ct)).ShouldBeTrue();

        api.Calls.ShouldBe(["request", "input", "request", "attached"]);
    }

    [Fact]
    public async Task A_window_that_never_comes_to_the_front_is_an_error()
    {
        var api = new FakeForegroundApi(worksAfter: int.MaxValue);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => Switch(api).BringToFrontAsync(Window, Ct));

        error.Message.ShouldBe(ForegroundSwitch.Refused);
        api.Calls.ShouldBe(["request", "input", "request", "attached"]);
    }

    [Fact]
    public async Task A_window_that_comes_to_the_front_late_is_waited_for()
    {
        var api = new FakeForegroundApi(worksAfter: 1, checksUntilInFront: 3);

        (await new ForegroundSwitch(api, TimeSpan.FromSeconds(5)).BringToFrontAsync(Window, Ct)).ShouldBeTrue();

        api.Calls.ShouldBe(["request"]);
    }

    private static ForegroundSwitch Switch(FakeForegroundApi api) => new(api, TimeSpan.Zero);

    /// <summary>The app is in front once <paramref name="worksAfter"/> calls were made, and then only from the <paramref name="checksUntilInFront"/>-th check on.</summary>
    private sealed class FakeForegroundApi(int worksAfter, int checksUntilInFront = 1) : IForegroundApi
    {
        private int _checks;

        public List<string> Calls { get; } = [];

        public bool IsInFront(nint window) => Calls.Count >= worksAfter && ++_checks >= checksUntilInFront;

        public void Request(nint window) => Calls.Add("request");

        public void SendEmptyInput() => Calls.Add("input");

        public void RequestAttached(nint window) => Calls.Add("attached");
    }
}
