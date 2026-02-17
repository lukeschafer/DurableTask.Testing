using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using DurableTask.Testing.Tests.SampleApp.Models;

namespace DurableTask.Testing.Tests.SampleApp.Orchestrators;

public class SubOrchestrationParent
{
    [Function("SubOrchestrationParent")]
    public async Task<ParentResult> Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<ParentRequest>()!;

        // Call sub-orchestrations for each region
        var regionTasks = request.Regions.Select(region =>
            context.CallSubOrchestratorAsync<RegionResult>(
                new TaskName("RegionSubOrchestrator"),
                new RegionRequest(region, request.Data)));

        var regionResults = await Task.WhenAll(regionTasks);

        return new ParentResult(
            RegionsProcessed: regionResults.Length,
            TotalItems: regionResults.Sum(r => r.ItemCount),
            Results: regionResults);
    }
}
