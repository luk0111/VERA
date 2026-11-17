using System.ComponentModel.DataAnnotations;

using Vera.Files;

namespace Vera.Files.Tests;

public sealed class FileAccessOptionsTests
{
    private static bool IsValid(FileAccessOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), validationResults: null, validateAllProperties: true);

    [Fact]
    public void The_defaults_are_20_MB_and_4000_characters_and_valid()
    {
        var options = new FileAccessOptions();

        options.MaxFileSizeMb.ShouldBe(20);
        options.MaxCharsPerRead.ShouldBe(4000);
        options.MaxFileSizeBytes.ShouldBe(20L * 1024 * 1024);
        IsValid(options).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 4000)]
    [InlineData(-1, 4000)]
    [InlineData(20, 0)]
    [InlineData(20, -5)]
    public void A_limit_of_zero_or_less_is_invalid(int sizeMb, int chars)
    {
        IsValid(new FileAccessOptions { MaxFileSizeMb = sizeMb, MaxCharsPerRead = chars }).ShouldBeFalse();
    }
}
