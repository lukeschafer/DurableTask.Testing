using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using DurableTask.Testing.Tests.SampleApp.Activities;
using DurableTask.Testing.Tests.SampleApp.Orchestrators;

namespace DurableTask.Testing.Tests.SampleAppTests;

public abstract class TestBase : IAsyncLifetime
{
    protected ServiceProvider ServiceProvider { get; private set; } = null!;
    protected Mock<FunctionContext> MockFunctionContext { get; private set; } = null!;
    protected SampleActivities SampleActivities { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        MockFunctionContext = new Mock<FunctionContext>();
        MockFunctionContext.Setup(x => x.InvocationId).Returns(() => Guid.NewGuid().ToString());
        MockFunctionContext.Setup(x => x.InstanceServices).Returns(ServiceProvider);

        // Get the SampleActivities instance from the service provider
        SampleActivities = ServiceProvider.GetRequiredService<SampleActivities>();

        await Task.CompletedTask;
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Register orchestrators
        services.AddSingleton<SimpleActivityChainOrchestrator>();
        services.AddSingleton<TimerPollingOrchestrator>();
        services.AddSingleton<ManualInterventionOrchestrator>();
        services.AddSingleton<FanOutFanInOrchestrator>();
        services.AddSingleton<SubOrchestrationParent>();
        services.AddSingleton<RegionSubOrchestrator>();

        // Register activities as singleton within the test scope
        services.AddSingleton<SampleActivities>();
    }

    public Task DisposeAsync()
    {
        SampleActivities?.ResetTestState();
        ServiceProvider?.Dispose();
        return Task.CompletedTask;
    }
}
