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
            ("VeraInput:Display:ExpectedWidth", "2560"),
            ("VeraInput:Capture:DefaultScale", "0.5"),
            ("VeraInput:Rdp:Enabled", "false"),
            ("VeraInput:Delays:AfterClick", "500"),
            ("VeraInput:Text:DefaultMethod", "Clipboard"),
            ("VeraInput:Journal:Folder", "D:/runs"));

        options.Display.ExpectedWidth.ShouldBe(2560);
        options.Capture.DefaultScale.ShouldBe(0.5);
        options.Rdp.Enabled.ShouldBeFalse();
        options.Delays.AfterClick.ShouldBe(500);
        options.Text.DefaultMethod.ShouldBe(TextInputMethod.Clipboard);
        options.Journal.Folder.ShouldBe("D:/runs");
        options.Delays.AfterMove.ShouldBe(50);
    }

    [Theory]
    [InlineData("VeraInput:Delays:AfterClick", "-5", "AfterClick")]
    [InlineData("VeraInput:Delays:DragSteps", "0", "DragSteps")]
    [InlineData("VeraInput:Display:ExpectedScalePercent", "50", "ExpectedScalePercent")]
    [InlineData("VeraInput:Capture:DefaultScale", "1.5", "DefaultScale")]
    [InlineData("VeraInput:Capture:MaxLongEdge", "8", "MaxLongEdge")]
    [InlineData("VeraInput:Rdp:WindowClass", "", "WindowClass")]
    [InlineData("VeraInput:Journal:ScreenshotScale", "0", "ScreenshotScale")]
    [InlineData("VeraInput:Text:AutoClipboardFor", "7", "AutoClipboardFor")]
    public void An_invalid_value_fails_naming_the_setting(string key, string value, string setting)
    {
        var e = Should.Throw<OptionsValidationException>(() => Options((key, value)));

        e.Message.ShouldContain(setting);
    }

    [Fact]
    public void A_misspelled_key_fails_naming_the_key()
    {
        var e = Should.Throw<InvalidOperationException>(() => Options(("VeraInput:Delays:AfterClik", "5")));

        e.Message.ShouldContain("AfterClik");
    }

    [Fact]
    public void Bad_values_are_found_at_startup_not_at_first_use()
    {
        using var services = Services(("VeraInput:Delays:AfterClick", "-5"));

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