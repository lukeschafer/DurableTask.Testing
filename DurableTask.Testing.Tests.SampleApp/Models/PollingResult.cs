namespace DurableTask.Testing.Tests.SampleApp.Models;

public record PollingResult(bool Success, string? Value = null, string? Error = null);
