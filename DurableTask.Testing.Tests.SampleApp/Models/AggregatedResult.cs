namespace DurableTask.Testing.Tests.SampleApp.Models;

public record AggregatedResult(int TotalItems, int SuccessfulItems, int FailedItems, ProcessedItem[] Results);
