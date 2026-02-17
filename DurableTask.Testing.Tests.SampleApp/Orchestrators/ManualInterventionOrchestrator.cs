using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using DurableTask.Testing.Tests.SampleApp.Activities;
using DurableTask.Testing.Tests.SampleApp.Models;

namespace DurableTask.Testing.Tests.SampleApp.Orchestrators;

public class ManualInterventionOrchestrator
{
    [Function("ManualIntervention")]
    public async Task<InterventionResult> Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<InterventionRequest>();

        // Start the work
        await context.CallActivityAsync(nameof(SampleActivities.StartWorkActivity), request);

        // Create a timeout task (24 hours)
        var timeoutTask = context.CreateTimer(
            context.CurrentUtcDateTime.AddHours(24),
            CancellationToken.None);

        // Wait for approval
        var approvalTask = context.WaitForExternalEvent<ApprovalEvent>("ApprovalEvent");

        // Wait for either approval or timeout
        var winner = await Task.WhenAny((Task)approvalTask, timeoutTask);

        if (winner == approvalTask)
        {
            var approval = await approvalTask;
            if (approval.Approved)
            {
                await context.CallActivityAsync(nameof(SampleActivities.CompleteWorkActivity), request);
                return new InterventionResult(true);
            }
            return new InterventionResult(false, approval.Reason ?? "Rejected by approver");
        }

        await context.CallActivityAsync(nameof(SampleActivities.CancelWorkActivity), request);
        return new InterventionResult(false, "Timeout - no response received");
    }
}
