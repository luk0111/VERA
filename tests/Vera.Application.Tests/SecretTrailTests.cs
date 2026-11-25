using SkiaSharp;

using Vera.Core;

namespace Vera.Application.Tests;

public sealed class SecretTrailTests
{
    private const int App = 42;

    private static readonly UiElement Window = new(0, "Window", "Shop", new ScreenRect(100, 100, 400, 300), true, "window");
    private static readonly UiElement Password = new(1, "Edit", "Password", new ScreenRect(110, 110, 200, 20), true, "pw", "");
    private static readonly UiElement Label = new(2, "Text", "Signed in", new ScreenRect(110, 150, 200, 20), true, "l");

    [Fact]
    public void Nothing_typed_leaves_the_reading_as_it_is()
    {
        var screen = Screen(Window, Password, Label);

        new SecretTrail().Hide(App, screen).ShouldBeSameAs(screen);
    }

    [Fact]
    public void A_typed_secret_is_masked_everywhere_and_painted_over_on_the_screenshot()
    {
        var trail = new SecretTrail();
        trail.Typed(App, Password, ["s3cret"]);

        var hidden = trail.Hide(App, Screen(Window with { Name = "Shop s3cret" }, Password with { Value = "s3cret" }, Label with { Name = "Hi s3cret!" }));

        hidden.WindowTitle.ShouldBe("Shop ***");
        hidden.Elements[1].Value.ShouldBe("***");
        hidden.Elements[2].Name.ShouldBe("Hi ***!");
        using var bitmap = SKBitmap.Decode(hidden.ScreenshotPng.ToArray());
        bitmap.GetPixel(20, 20).ShouldBe(SKColors.Black);
        bitmap.GetPixel(20, 60).ShouldBe(SKColors.Black);
        bitmap.GetPixel(20, 100).ShouldBe(SKColors.White);
    }

    [Fact]
    public void The_field_typed_into_is_painted_over_even_when_its_text_cannot_be_read()
    {
        var trail = new SecretTrail();
        trail.Typed(App, Password, ["s3cret"]);

        var hidden = trail.Hide(App, Screen(Window, Password with { Value = null }, Label));

        hidden.Elements[1].Value.ShouldBeNull();
        using var bitmap = SKBitmap.Decode(hidden.ScreenshotPng.ToArray());
        bitmap.GetPixel(20, 20).ShouldBe(SKColors.Black);
        bitmap.GetPixel(20, 60).ShouldBe(SKColors.White);
    }

    [Theory]
    [InlineData("1234", "Order 1234", "Order ***")]
    [InlineData("1234", "Order 12345", "Order 12345")]
    [InlineData("1234", "Year 2012", "Year 2012")]
    [InlineData("12", "Order 12", "Order 12")]
    [InlineData("s3cret", "user s3cret123", "user ***123")]
    [InlineData("p@ss!", "key p@ss! here", "key *** here")]
    public void Short_secrets_are_masked_only_as_whole_words_and_very_short_ones_not_at_all_outside_their_field(string secret, string text, string shown)
    {
        var trail = new SecretTrail();
        trail.Typed(App, Password, [secret]);

        trail.Hide(App, Screen(Window, Password, Label with { Name = text })).Elements[2].Name.ShouldBe(shown);
    }

    [Fact]
    public void The_field_a_short_secret_went_into_shows_only_the_mask()
    {
        var trail = new SecretTrail();
        trail.Typed(App, Password, ["12"]);

        trail.Hide(App, Screen(Window, Password with { Value = "12" }, Label)).Elements[1].Value.ShouldBe("***");
    }

    [Fact]
    public void Plain_text_typed_later_into_the_field_can_be_read_again_while_the_secret_stays_hidden()
    {
        var trail = new SecretTrail();
        trail.Typed(App, Password, ["s3cret"]);
        trail.Forget(App, Password);

        var hidden = trail.Hide(App, Screen(Window, Password with { Value = "cats" }, Label with { Name = "Hi s3cret" }));

        hidden.Elements[1].Value.ShouldBe("cats");
        hidden.Elements[2].Name.ShouldBe("Hi ***");
    }

    [Fact]
    public void The_values_are_hidden_in_every_app_but_the_field_only_in_its_own()
    {
        var trail = new SecretTrail();
        trail.Typed(App, Password, ["s3cret"]);

        var other = trail.Hide(7, Screen(Window, Password with { Value = "cats" }, Label with { Name = "s3cret" }));

        other.Elements[1].Value.ShouldBe("cats");
        other.Elements[2].Name.ShouldBe("***");
    }

    private static ScreenState Screen(params UiElement[] elements)
    {
        using var bitmap = new SKBitmap(400, 300);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ScreenState(elements[0].Name, elements, png.ToArray(), []);
    }
}
