using Microsoft.Extensions.DependencyInjection;

using Vera.Core;

namespace Vera.Secrets;

public static class SecretsServiceCollectionExtensions
{
    /// <summary>Registers <see cref="WindowsCredentialSecretStore"/> as the <see cref="ISecretStore"/>.</summary>
    public static IServiceCollection AddVeraSecrets(this IServiceCollection services)
    {
        services.AddSingleton<ISecretStore, WindowsCredentialSecretStore>();
        return services;
    }
}
