using Microsoft.Azure.Functions.Worker;
using DurableTask.Testing.Tests.SampleApp.Models;

namespace DurableTask.Testing.Tests.SampleApp.Activities;

public class SampleActivities
{
    // Instance-level tracking for test verification
    private readonly List<string> _startedWork = new();
    private readonly List<string> _completedWork = new();
    private readonly List<string> _cancelledWork = new();
    private readonly Dictionary<string, int> _pollAttempts = new();
    private readonly object _lock = new();

    // SimpleActivityChain activities
    [Function("ValidateActivity")]
    public Task<ValidatedData> ValidateActivity([ActivityTrigger] ChainRequest input)
    {
        var isValid = !string.IsNullOrWhiteSpace(input.InputData);
        return Task.FromResult(new ValidatedData(input.InputData, isValid, input.Multiplier));
    }

    [Function("ProcessActivity")]
    public Task<ProcessedData> ProcessActivity([ActivityTrigger] ValidatedData input)
    {
        if (!input.IsValid)
        {
            throw new ArgumentException("Invalid data");
        }

        var result = new ProcessedData(
            input.OriginalData.ToUpperInvariant(),
            input.OriginalData.Length * input.Multiplier,
            DateTime.UtcNow);

        return Task.FromResult(result);
    }

    [Function("SaveActivity")]
    public Task<string> SaveActivity([ActivityTrigger] ProcessedData input)
    {
        var savedId = $"SAVED-{input.Value}-{Guid.NewGuid():N}";
        return Task.FromResult(savedId);
    }

    // TimerPolling activities
    [Function("CheckStatusActivity")]
    public Task<OperationStatus> CheckStatusActivity([ActivityTrigger] string id)
    {
        lock (_lock)
        {
            _pollAttempts.TryGetValue(id, out var attempts);
            attempts++;

            // Simulate completion after 3 attempts
            var isComplete = attempts >= 3;
            _pollAttempts[id] = attempts;

            if (isComplete)
            {
                _pollAttempts.Remove(id);
                return Task.FromResult(new OperationStatus(true, $"Completed-{id}"));
            }

            return Task.FromResult(new OperationStatus(false));
        }
    }

    // ManualIntervention activities
    [Function("StartWorkActivity")]
    public Task StartWorkActivity([ActivityTrigger] InterventionRequest request)
    {
        _startedWork.Add(request.WorkItemId);
        return Task.CompletedTask;
    }

    [Function("CompleteWorkActivity")]
    public Task CompleteWorkActivity([ActivityTrigger] InterventionRequest request)
    {
        _completedWork.Add(request.WorkItemId);
        return Task.CompletedTask;
    }

    [Function("CancelWorkActivity")]
    public Task CancelWorkActivity([ActivityTrigger] InterventionRequest request)
    {
        _cancelledWork.Add(request.WorkItemId);
        return Task.CompletedTask;
    }

    // FanOutFanIn activities
    [Function("ProcessItemActivity")]
    public Task<ProcessedItem> ProcessItemActivity([ActivityTrigger] string item)
    {
        // Simulate failure for items starting with "fail"
        var success = !item.StartsWith("fail", StringComparison.OrdinalIgnoreCase);
        var error = success ? null : $"Item '{item}' is marked to fail";

        return Task.FromResult(new ProcessedItem(item, success, error));
    }

    // SubOrchestration activities
    [Function("GetItemsForRegionActivity")]
    public Task<string[]> GetItemsForRegionActivity([ActivityTrigger] string region)
    {
        // Return different number of items per region
        var count = region.Length;
        var items = Enumerable.Range(1, count)
            .Select(i => $"{region}-Item-{i}")
            .ToArray();

        return Task.FromResult(items);
    }

    [Function("ProcessItemsActivity")]
    public Task<string> ProcessItemsActivity([ActivityTrigger] string input)
    {
        return Task.FromResult($"PROCESSED:{input}");
    }

    // Instance methods for test verification
    public void ResetTestState()
    {
        lock (_lock)
        {
            _pollAttempts.Clear();
        }
        _startedWork.Clear();
        _completedWork.Clear();
        _cancelledWork.Clear();
    }

    public bool WasWorkStarted(string workItemId) => _startedWork.Contains(workItemId);
    public bool WasWorkCompleted(string workItemId) => _completedWork.Contains(workItemId);
    public bool WasWorkCancelled(string workItemId) => _cancelledWork.Contains(workItemId);
}
