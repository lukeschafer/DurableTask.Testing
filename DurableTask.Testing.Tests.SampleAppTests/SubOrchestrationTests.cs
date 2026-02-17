using FluentAssertions;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Xunit;
using DurableTask.Testing.Tests.SampleApp.Models;
using DurableTask.Testing.Tests.SampleApp.Orchestrators;

namespace DurableTask.Testing.Tests.SampleAppTests;

public class SubOrchestrationTests : TestBase
{
    [Fact]
    public async Task Run_SingleSubOrchestration_CompletesSuccessfully()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(SubOrchestrationParent).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new ParentRequest(["US"], "test-data");

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("SubOrchestrationParent"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task Run_MultipleSubOrchestrations_CompletesAll()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(SubOrchestrationParent).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new ParentRequest(["US", "EU", "APAC"], "test-data");

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("SubOrchestrationParent"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task Run_WaitForInstancesAndChildren_IncludesAllInstances()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(SubOrchestrationParent).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new ParentRequest(["US", "EU", "APAC", "LATAM"], "test-data");

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("SubOrchestrationParent"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }
}
