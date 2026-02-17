namespace DurableTask.Testing.Tests.SampleApp.Models;

public record OperationStatus(bool IsComplete, string? Value = null, string? Error = null);
