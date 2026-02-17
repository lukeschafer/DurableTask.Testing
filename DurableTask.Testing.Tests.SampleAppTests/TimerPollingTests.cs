using FluentAssertions;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Xunit;
using DurableTask.Testing.Tests.SampleApp.Models;
using DurableTask.Testing.Tests.SampleApp.Orchestrators;
using System.Diagnostics;

namespace DurableTask.Testing.Tests.SampleAppTests;

public class TimerPollingTests : TestBase
{
    [Fact]
    public async Task Run_WithTimerOverride_CompletesQuickly()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(TimerPollingOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow)
            .WithOverrideAllTimerTimes(TimeSpan.FromMilliseconds(10));

        var input = new PollingRequest("test-operation", "TestResource");
        var stopwatch = Stopwatch.StartNew();

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("TimerPolling"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);
        stopwatch.Stop();

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // Should complete in under 1 second
    }

    [Fact]
    public async Task Run_CompletesAfterRetries_ReturnsSuccess()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(TimerPollingOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow)
            .WithOverrideAllTimerTimes(TimeSpan.FromMilliseconds(10));

        var input = new PollingRequest("retry-operation", "TestResource");

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("TimerPolling"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }
}
