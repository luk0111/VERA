using Vera.Adapters.Desktop;
using Vera.Adapters.Mac;
using Vera.Core;

namespace Vera.Adapters.Mac.Tests;

public sealed class MacRolesTests
{
    [Theory]
    [InlineData("AXButton", "Button")]
    [InlineData("AXTextField", "Edit")]
    [InlineData("AXPopUpButton", "ComboBox")]
    [InlineData("AXLink", "Hyperlink")]
    [InlineData("AXGroup", "Group")]
    public void Roles_use_the_same_words_as_Windows(string role, string expected) =>
        MacRoles.ToCommon(role).ShouldBe(expected);

    [Theory]
    [InlineData("Save", "Saves the file", "Help", "Save")]
    [InlineData("", "Back", "Help", "Back")]
    [InlineData(null, null, "Help", "Help")]
    [InlineData(null, null, null, "")]
    public void The_name_is_the_first_text_the_element_has(string? title, string? description, string? help, string expected) =>
        MacRoles.Name(title, description, help).ShouldBe(expected);

    [Fact]
    public void Points_become_image_pixels_on_a_Retina_screen() =>
        MacRoles.ToImagePixels(new ScreenRect(10, 20, 30, 40), 2).ShouldBe(new ScreenRect(20, 40, 60, 80));

    [Fact]
    public void A_Mac_button_gets_a_number_like_a_Windows_one()
    {
        var window = new ScreenRect(0, 0, 200, 200);
        var button = new RawElement(MacRoles.ToCommon("AXButton"), "Save", new ScreenRect(10, 10, 50, 20), true, false, "", null);

        var elements = ElementNumbering.Number("Editor", window, [button]);

        elements.Count.ShouldBe(2);
        elements[1].Id.ShouldBe(1);
        elements[1].Name.ShouldBe("Save");
    }
}
