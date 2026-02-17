using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using DurableTask.Testing.Tests.SampleApp.Activities;
using DurableTask.Testing.Tests.SampleApp.Models;

namespace DurableTask.Testing.Tests.SampleApp.Orchestrators;

public class FanOutFanInOrchestrator
{
    [Function("FanOutFanIn")]
    public async Task<AggregatedResult> Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<FanOutRequest>()!;

        // Fan out - process all items in parallel
        var tasks = request.Items.Select(item =>
            context.CallActivityAsync<ProcessedItem>(
                nameof(SampleActivities.ProcessItemActivity), item));

        // Fan in - wait for all to complete
        var results = await Task.WhenAll(tasks);

        // Aggregate results
        return new AggregatedResult(
            TotalItems: results.Length,
            SuccessfulItems: results.Count(r => r.Success),
            FailedItems: results.Count(r => !r.Success),
            Results: results);
    }
}
