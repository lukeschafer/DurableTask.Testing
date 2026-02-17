using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using DurableTask.Testing.Tests.SampleApp.Activities;
using DurableTask.Testing.Tests.SampleApp.Models;

namespace DurableTask.Testing.Tests.SampleApp.Orchestrators;

public class TimerPollingOrchestrator
{
    private const int _maxAttempts = 5;

    [Function("TimerPolling")]
    public async Task<PollingResult> Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<PollingRequest>()!;
        var attempt = 0;

        while (attempt < _maxAttempts)
        {
            var status = await context.CallActivityAsync<OperationStatus>(
                nameof(SampleActivities.CheckStatusActivity), request.Id);

            if (status.IsComplete)
            {
                return new PollingResult(true, status.Value);
            }

            // Wait before next poll
            await context.CreateTimer(
                context.CurrentUtcDateTime.AddSeconds(30),
                CancellationToken.None);

            attempt++;
        }

        return new PollingResult(false, Error: "Timeout after max attempts");
    }
}
