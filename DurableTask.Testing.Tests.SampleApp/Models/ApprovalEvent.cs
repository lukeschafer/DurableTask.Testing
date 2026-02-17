namespace DurableTask.Testing.Tests.SampleApp.Models;

public record ApprovalEvent(bool Approved, string Approver, string? Reason = null);
