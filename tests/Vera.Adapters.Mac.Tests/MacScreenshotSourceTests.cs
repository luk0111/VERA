using Vera.Adapters.Mac;

namespace Vera.Adapters.Mac.Tests;

public sealed class MacScreenshotSourceTests
{
    [Fact]
    public void The_size_is_read_from_the_png_header()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x05, 0x00, 0, 0, 0x02, 0xD0];

        MacScreenshotSource.ReadPngSize(png).ShouldBe((1280, 720));
    }

    [Fact]
    public void Other_data_is_rejected() =>
        Should.Throw<InvalidDataException>(() => MacScreenshotSource.ReadPngSize(new byte[24]));

    [Fact]
    public void An_area_can_be_on_a_monitor_left_of_the_main_one() =>
        MacScreenshotSource.FormatArea(-100, 20, 300, 400).ShouldBe("-R-100,20,300,400");
}
