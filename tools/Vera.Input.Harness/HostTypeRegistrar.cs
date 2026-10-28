using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Spectre.Console.Cli;

namespace Vera.Input.Harness;

/// <summary>
/// Lets Spectre.Console.Cli create commands from the host's services. The host is built and started only when a
/// command runs (not for <c>--help</c>), and starting it checks the <c>VeraInput</c> configuration.
/// </summary>
internal sealed class HostTypeRegistrar(HostApplicationBuilder builder) : ITypeRegistrar
{
    public void Register(Type service, Type implementation) => builder.Services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => builder.Services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => builder.Services.AddSingleton(service, _ => factory());

    public ITypeResolver Build()
    {
        var host = builder.Build();
        try
        {
            host.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is OptionsValidationException or InvalidOperationException)
        {
            host.Dispose();
            throw new HarnessConfigurationException(e.Message, e);
        }

        return new HostTypeResolver(host);
    }

    private sealed class HostTypeResolver(IHost host) : ITypeResolver, IDisposable
    {
        public object? Resolve(Type? type) => type is null ? null : host.Services.GetService(type);

        public void Dispose()
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
        }
    }
}

/// <summary>The <c>VeraInput</c> configuration is invalid. The message says which value and why.</summary>
internal sealed class HarnessConfigurationException(string message, Exception innerException) : Exception(message, innerException);