using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Vera.Input.Options;

namespace Vera.Input.Tests.Options;

public sealed class AddVeraInputTests
{
    [Fact]
    public void Without_a_VeraInput_section_the_defaults_are_used()
    {
        Options().ShouldBe(new VeraInputOptions());
    }

    [Fact]
    public void Values_from_configuration_override_the_defaults()
    {
        var options = Options(
            ("VeraInput:Display:MonitorIndex", "1"),
            ("VeraInput:Capture:DefaultScale", "0.5"),
            ("VeraInput:Rdp:Enabled", "true"));

        options.Display.MonitorIndex.ShouldBe(1);
        options.Capture.DefaultScale.ShouldBe(0.5);
        options.Rdp.Enabled.ShouldBeTrue();
        options.Rdp.RequireForeground.ShouldBeTrue();
    }

    [Theory]
    [InlineData("VeraInput:Display:MonitorIndex", "99", "MonitorIndex")]
    [InlineData("VeraInput:Capture:DefaultScale", "1.5", "DefaultScale")]
    [InlineData("VeraInput:Rdp:WindowClass", "", "WindowClass")]
    [InlineData("VeraInput:Delays:AfterClick", "-1", "AfterClick")]
    public void An_invalid_value_fails_naming_the_setting(string key, string value, string setting)
    {
        var e = Should.Throw<OptionsValidationException>(() => Options((key, value)));

        e.Message.ShouldContain(setting);
    }

    [Fact]
    public void A_misspelled_key_fails_naming_the_key()
    {
        var e = Should.Throw<InvalidOperationException>(() => Options(("VeraInput:Capture:DefaultScal", "0.5")));

        e.Message.ShouldContain("DefaultScal");
    }

    [Fact]
    public void Bad_values_are_found_at_startup_not_at_first_use()
    {
        using var services = Services(("VeraInput:Capture:DefaultScale", "1.5"));

        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    private static VeraInputOptions Options(params (string Key, string Value)[] settings)
    {
        using var services = Services(settings);
        return services.GetRequiredService<IOptions<VeraInputOptions>>().Value;
    }

    private static ServiceProvider Services(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return new ServiceCollection().AddVeraInput(config).BuildServiceProvider();
    }
}