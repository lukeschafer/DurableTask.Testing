using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using DurableTask.Testing.Tests.SampleApp.Activities;
using DurableTask.Testing.Tests.SampleApp.Models;

namespace DurableTask.Testing.Tests.SampleApp.Orchestrators;

public class RegionSubOrchestrator
{
    [Function("RegionSubOrchestrator")]
    public async Task<RegionResult> Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<RegionRequest>()!;

        var items = await context.CallActivityAsync<string[]>(
            nameof(SampleActivities.GetItemsForRegionActivity), request.Region);

        // Simulate processing items
        await context.CallActivityAsync<string>(
            nameof(SampleActivities.ProcessItemsActivity), $"{request.Region}:{items.Length}");

        return new RegionResult(request.Region, items.Length);
    }
}
