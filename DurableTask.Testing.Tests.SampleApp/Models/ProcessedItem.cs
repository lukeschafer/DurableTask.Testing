namespace DurableTask.Testing.Tests.SampleApp.Models;

public record ProcessedItem(string Item, bool Success, string? Error = null);
