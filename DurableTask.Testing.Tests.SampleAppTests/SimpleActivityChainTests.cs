using FluentAssertions;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Xunit;
using DurableTask.Testing.Tests.SampleApp.Models;
using DurableTask.Testing.Tests.SampleApp.Orchestrators;

namespace DurableTask.Testing.Tests.SampleAppTests;

public class SimpleActivityChainTests : TestBase
{
    [Fact]
    public async Task Run_AllActivitiesSucceed_ReturnsSavedId()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(SimpleActivityChainOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new ChainRequest("test-data", 5);

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("SimpleActivityChain"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task Run_InvalidInput_ThrowsException()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(SimpleActivityChainOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new ChainRequest("", 5); // Empty string will fail validation

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("SimpleActivityChain"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Failed);
        result.FailureDetails.Should().NotBeNull();
        result.FailureDetails!.ErrorType.Should().Be("System.AggregateException");
    }

    [Fact]
    public async Task Run_WithZeroMultiplier_ProcessesCorrectly()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(SimpleActivityChainOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new ChainRequest("test", 0);

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("SimpleActivityChain"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }
}
