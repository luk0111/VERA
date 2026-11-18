using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Vera.Files;

public static class FilesServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="FileAccessOptions"/> from the <c>Files</c> section (not set means the defaults) and registers <see cref="IFileReader"/>.
    /// Out-of-range values throw <see cref="OptionsValidationException"/>; unknown keys (typos) throw <see cref="InvalidOperationException"/>.
    /// Needs logging (the host provides it).
    /// </summary>
    public static IServiceCollection AddVeraFiles(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        services.AddOptions<FileAccessOptions>()
            .Bind(config.GetSection(FileAccessOptions.SectionName), binder => binder.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFileReader, FileReader>();
        return services;
    }
}
