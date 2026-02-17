using FluentAssertions;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Xunit;
using DurableTask.Testing.Tests.SampleApp.Models;
using DurableTask.Testing.Tests.SampleApp.Orchestrators;

namespace DurableTask.Testing.Tests.SampleAppTests;

public class ManualInterventionTests : TestBase
{
    [Fact]
    public async Task Run_WithPreConfiguredApproval_Succeeds()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(ManualInterventionOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow)
            .WithEventPayload("ApprovalEvent", new ApprovalEvent(
                Approved: true,
                Approver: "Test Approver",
                Reason: "All good"));

        var input = new InterventionRequest("work-item-123", "requester@test.com");

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("ManualIntervention"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        SampleActivities.WasWorkCompleted("work-item-123").Should().BeTrue();
    }

    [Fact]
    public async Task Run_WithPreConfiguredRejection_ReturnsRejected()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(ManualInterventionOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow)
            .WithEventPayload("ApprovalEvent", new ApprovalEvent(
                Approved: false,
                Approver: "Test Approver",
                Reason: "Not approved"));

        var input = new InterventionRequest("work-item-456", "requester@test.com");

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("ManualIntervention"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        SampleActivities.WasWorkCompleted("work-item-456").Should().BeFalse();
        SampleActivities.WasWorkCancelled("work-item-456").Should().BeFalse();
    }

    [Fact]
    public async Task Run_WithNoEvent_TimesOut()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(ManualInterventionOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow)
            .WithOverrideAllTimerTimes(TimeSpan.FromMilliseconds(10));

        var input = new InterventionRequest("work-item-789", "requester@test.com");

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("ManualIntervention"),
            input);

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        SampleActivities.WasWorkCancelled("work-item-789").Should().BeTrue();
    }

    [Fact]
    public async Task Run_RaiseEventMidExecution_Succeeds()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            ServiceProvider,
            typeof(ManualInterventionOrchestrator).Assembly,
            MockFunctionContext.Object,
            () => DateTime.UtcNow);

        var input = new InterventionRequest("work-item-raise", "requester@test.com");

        // Act - Start orchestration
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("ManualIntervention"),
            input);

        // Give it time to reach the WaitForExternalEvent
        await Task.Delay(100);

        // Raise the event externally
        await durableClient.RaiseEventAsync(instanceId, "ApprovalEvent",
            new ApprovalEvent(Approved: true, Approver: "Manual Approver"));

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        SampleActivities.WasWorkCompleted("work-item-raise").Should().BeTrue();
    }
}
