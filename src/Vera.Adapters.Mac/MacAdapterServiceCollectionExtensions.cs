using Microsoft.Extensions.DependencyInjection;

using Vera.Core;
using Vera.Input;

namespace Vera.Adapters.Mac;

public static class MacAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers macOS screenshots, the running app list and a <see cref="Func{IAppAdapter}"/> that makes a new <see cref="MacAdapter"/> each time.
    /// The caller disposes each adapter; the container never holds one, as with the desktop adapter.
    /// </summary>
    public static IServiceCollection AddVeraMacAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IScreenshotSource, MacScreenshotSource>();
        services.AddSingleton<IRunningAppSource, MacRunningApps>();
        services.AddSingleton<Func<IAppAdapter>>(sp =>
        {
            var screen = sp.GetRequiredService<IScreenshotSource>();
            return () => new MacAdapter(screen);
        });
        return services;
    }
}
