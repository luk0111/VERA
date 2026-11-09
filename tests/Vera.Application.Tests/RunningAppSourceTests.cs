using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Vera.Core;

namespace Vera.Application.Tests;

public sealed class RunningAppSourceTests
{
    [Fact]
    public void Every_platform_build_registers_a_running_app_source()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:DefaultProfile"] = "local",
            ["Llm:Profiles:local:Provider"] = "Ollama",
            ["Llm:Profiles:local:Endpoint"] = "http://localhost:11434",
            ["Llm:Profiles:local:Model"] = "qwen3-vl:8b",
        }).Build();
        using var provider = new ServiceCollection().AddVeraApplication(config).BuildServiceProvider();

        provider.GetRequiredService<IRunningAppSource>().ShouldNotBeNull();
    }
}
