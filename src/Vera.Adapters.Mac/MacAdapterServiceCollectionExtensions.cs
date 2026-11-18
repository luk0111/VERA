using Microsoft.Extensions.DependencyInjection;

using Vera.Core;
using Vera.Input;

namespace Vera.Adapters.Mac;

public static class MacAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers macOS screenshots, mouse and keyboard (<see cref="MacScreen"/>), the running app list and a <see cref="Func{IAppAdapter}"/> that makes a new <see cref="MacAdapter"/> each time.
    /// The caller disposes each adapter; the container never holds one, as with the desktop adapter.
    /// </summary>
    public static IServiceCollection AddVeraMacAdapter(this IServiceCollection services)
    {
        // One instance for both, so clicks are mapped with the latest screenshot.
        services.AddSingleton<MacScreen>();
        services.AddSingleton<IScreen>(sp => sp.GetRequiredService<MacScreen>());
        services.AddSingleton<IScreenshotSource>(sp => sp.GetRequiredService<MacScreen>());
        services.AddSingleton<IRunningAppSource, MacRunningApps>();
        services.AddSingleton<Func<IAppAdapter>>(sp =>
        {
            var screen = sp.GetRequiredService<IScreen>();
            return () => new MacAdapter(screen);
        });
        return services;
    }
}
