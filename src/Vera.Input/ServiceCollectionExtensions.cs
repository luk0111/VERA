using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Vera.Input.Options;

namespace Vera.Input;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="VeraInputOptions"/> from the <c>VeraInput</c> section and checks it when the host starts.
    /// Out-of-range values throw <see cref="OptionsValidationException"/>; unknown keys (typos) throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public static IServiceCollection AddVeraInput(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        services.AddOptions<VeraInputOptions>()
            .Bind(config.GetSection(VeraInputOptions.SectionName), binder => binder.ErrorOnUnknownConfiguration = true)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<VeraInputOptions>, VeraInputOptionsValidator>();
        return services;
    }
}