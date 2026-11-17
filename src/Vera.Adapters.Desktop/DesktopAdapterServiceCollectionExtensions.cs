using Microsoft.Extensions.DependencyInjection;

using Vera.Core;
using Vera.Input;

namespace Vera.Adapters.Desktop;

public static class DesktopAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="Func{IAppAdapter}"/> that makes a new <see cref="DesktopAdapter"/> each time. Needs an <see cref="IScreen"/> registered (<c>AddVeraInput</c> does it).
    /// The caller disposes each adapter; the container never holds one, since it can't dispose an only-async-disposable service when disposed synchronously.
    /// </summary>
    public static IServiceCollection AddVeraDesktopAdapter(this IServiceCollection services) =>
        services.AddSingleton<Func<IAppAdapter>>(sp =>
        {
            var screen = sp.GetRequiredService<IScreen>();
            return () => new DesktopAdapter(screen);
        });
}
