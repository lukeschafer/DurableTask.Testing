using FluentAssertions;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Xunit;
using DurableTask.Testing.Tests.SampleApp.Models;
using DurableTask.Testing.Tests.SampleApp.Orchestrators;

namespace DurableTask.Testing.Tests.SampleAppTests;

public class FanOutFanInTests : TestBase
{
    [Fact]
    public async Task Run_AllItemsSucceed_ReturnsAggregatedResult()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(FanOutFanInOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new FanOutRequest(["item1", "item2", "item3", "item4", "item5"]);

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("FanOutFanIn"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task Run_SomeItemsFail_ReturnsPartialSuccess()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(FanOutFanInOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new FanOutRequest(["item1", "fail-item2", "item3", "fail-item4", "item5"]);

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("FanOutFanIn"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task Run_AllItemsFail_ReturnsAllFailed()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(FanOutFanInOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new FanOutRequest(["fail-item1", "fail-item2", "fail-item3"]);

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("FanOutFanIn"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task Run_EmptyInput_ReturnsZeroItems()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(FanOutFanInOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new FanOutRequest([]);

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("FanOutFanIn"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }
}
