using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Vera.Files;

namespace Vera.Files.Tests;

public sealed class FilesServiceCollectionExtensionsTests
{
    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddVeraFiles(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Without_a_Files_section_the_defaults_apply()
    {
        using var provider = Build([]);

        var options = provider.GetRequiredService<IOptions<FileAccessOptions>>().Value;

        options.MaxFileSizeMb.ShouldBe(20);
        options.MaxCharsPerRead.ShouldBe(4000);
        provider.GetRequiredService<IFileReader>().ShouldNotBeNull();
    }

    [Fact]
    public void The_Files_section_sets_the_limits()
    {
        using var provider = Build(new() { ["Files:MaxFileSizeMb"] = "5", ["Files:MaxCharsPerRead"] = "12000" });

        var options = provider.GetRequiredService<IOptions<FileAccessOptions>>().Value;

        options.MaxFileSizeMb.ShouldBe(5);
        options.MaxCharsPerRead.ShouldBe(12000);
    }

    [Fact]
    public void A_limit_of_zero_is_refused()
    {
        using var provider = Build(new() { ["Files:MaxCharsPerRead"] = "0" });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<FileAccessOptions>>().Value);
    }

    [Fact]
    public void A_misspelled_key_is_refused()
    {
        using var provider = Build(new() { ["Files:MaxCharsPerReed"] = "4000" });

        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<IOptions<FileAccessOptions>>().Value);
    }
}
