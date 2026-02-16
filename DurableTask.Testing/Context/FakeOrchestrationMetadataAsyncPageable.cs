using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace DurableTask.Testing.Context
{
    public class FakeOrchestrationMetadataAsyncPageable : AsyncPageable<OrchestrationMetadata>
    {
        public override async IAsyncEnumerable<Page<OrchestrationMetadata>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
