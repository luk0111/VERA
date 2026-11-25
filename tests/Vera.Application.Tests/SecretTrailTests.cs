using SkiaSharp;

using Vera.Core;

namespace Vera.Application.Tests;

public sealed class SecretTrailTests
{
    private static readonly UiElement Window = new(0, "Window", "Shop", new ScreenRect(100, 100, 400, 300), true, "window");
    private static readonly UiElement Password = new(1, "Edit", "Password", new ScreenRect(110, 110, 200, 20), true, "pw", "");
    private static readonly UiElement Label = new(2, "Text", "Signed in", new ScreenRect(110, 150, 200, 20), true, "l");

    [Fact]
    public void Nothing_typed_leaves_the_reading_as_it_is()
    {
        var screen = Screen(Window, Password, Label);

        new SecretTrail().Hide(screen).ShouldBeSameAs(screen);
    }

    [Fact]
    public void A_typed_secret_is_masked_everywhere_and_painted_over_on_the_screenshot()
    {
        var trail = new SecretTrail();
        trail.Typed(Password, "s3cret");

        var hidden = trail.Hide(Screen(Window with { Name = "Shop s3cret" }, Password with { Value = "s3cret" }, Label with { Name = "Hi s3cret!" }));

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
        trail.Typed(Password, "s3cret");

        var hidden = trail.Hide(Screen(Window, Password with { Value = null }, Label));

        hidden.Elements[1].Value.ShouldBeNull();
        using var bitmap = SKBitmap.Decode(hidden.ScreenshotPng.ToArray());
        bitmap.GetPixel(20, 20).ShouldBe(SKColors.Black);
        bitmap.GetPixel(20, 60).ShouldBe(SKColors.White);
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
