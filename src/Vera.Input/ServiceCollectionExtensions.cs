using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Vera.Input.Capture;
using Vera.Input.Options;
using Vera.Input.Platform;
using Vera.Input.Session;

namespace Vera.Input;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="VeraInputOptions"/> from the <c>VeraInput</c> section and checks it when the host starts.
    /// Out-of-range values throw <see cref="OptionsValidationException"/>; unknown keys (typos) throw <see cref="InvalidOperationException"/>.
    /// Also registers the building blocks of the Windows implementation. Needs logging (the host provides it).
    /// </summary>
    public static IServiceCollection AddVeraInput(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        services.AddOptions<VeraInputOptions>()
            .Bind(config.GetSection(VeraInputOptions.SectionName), binder => binder.ErrorOnUnknownConfiguration = true)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<VeraInputOptions>, VeraInputOptionsValidator>();
        services.AddSingleton<IDpiAwarenessCheck, DpiAwarenessCheck>();
        services.AddSingleton<IDisplayInfo, Win32DisplayInfo>();
        services.AddSingleton<IDisplayCheck, DisplayCheck>();
        services.AddSingleton<ICursorPosition, Win32CursorPosition>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IScreenGrabber, GdiScreenGrabber>();
        services.AddSingleton<ISessionProbe, Win32SessionProbe>();
        services.AddSingleton<ISessionGuard, SessionGuard>();
        services.AddSingleton<IFrameValidator, BlackFrameValidator>();
        services.AddSingleton<IFrameEncoder, GdiPngFrameEncoder>();

        // One instance behind both interfaces, so the frame sequence is shared. IPointer and IKeyboard follow with M4–M6.
        services.AddSingleton<DesktopScreen>();
        services.AddSingleton<IScreen>(sp => sp.GetRequiredService<DesktopScreen>());
        services.AddSingleton<IScreenshotSource>(sp => sp.GetRequiredService<DesktopScreen>());
        return services;
    }
}