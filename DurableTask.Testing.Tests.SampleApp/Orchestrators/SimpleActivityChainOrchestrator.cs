using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using DurableTask.Testing.Tests.SampleApp.Activities;
using DurableTask.Testing.Tests.SampleApp.Models;

namespace DurableTask.Testing.Tests.SampleApp.Orchestrators;

public class SimpleActivityChainOrchestrator
{
    [Function("SimpleActivityChain")]
    public async Task<string> Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ChainRequest>();

        // Step 1: Validate input
        var validated = await context.CallActivityAsync<ValidatedData>(
            nameof(SampleActivities.ValidateActivity), input);

        // Step 2: Process data
        var processed = await context.CallActivityAsync<ProcessedData>(
            nameof(SampleActivities.ProcessActivity), validated);

        // Step 3: Save result
        var result = await context.CallActivityAsync<string>(
            nameof(SampleActivities.SaveActivity), processed);

        return result;
    }
}
