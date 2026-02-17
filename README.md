# DurableTask.Testing

A fake task client for testing Azure Durable Functions without requiring the actual Azure Functions runtime or Durable Task infrastructure.

---

## Overview

`DurableTask.Testing` provides a `FakeDurableTaskClient` that implements the `DurableTaskClient` interface from Microsoft.DurableTask.Client. This allows you to write unit tests for your Durable Functions orchestrations and activities without spinning up the full Azure Functions host.

### Key Features

- **Test orchestrations in-process** - No Azure Functions host required
- **Override timers** - Speed up tests by replacing long delays with short ones
- **Pre-configure external events** - Test manual intervention patterns without waiting
- **Mock durable entities** - Control entity state in tests
- **Full activity execution** - Your actual activity code runs with real dependencies
- **Sub-orchestration support** - Test nested orchestrations
- **Wait for completion** - Query orchestration status and results

---

## Preconditions

Before using `FakeDurableTaskClient`, you need to set up a few things:

### 1. Configure Dependency Injection

Your functions and their dependencies must be registered in a `ServiceProvider`. The fake client uses this service provider to instantiate your orchestrators and activities.

```csharp
var services = new ServiceCollection();

// Register your orchestrators
services.AddSingleton<MyOrchestrator>();
services.AddSingleton<AnotherOrchestrator>();

// Register your activities
services.AddSingleton<MyActivities>();
services.AddSingleton<AnotherActivity>();

// Register any dependencies your functions use
services.AddSingleton<ILoggerFactory>(new LoggerFactory());
services.AddSingleton<ISomeDependency, SomeDependency>();

var serviceProvider = services.BuildServiceProvider();
```

### 2. Provide the Functions Assembly

The fake client needs to know which assembly contains your functions so it can find them by name.

```csharp
var functionAssembly = typeof(MyOrchestrator).Assembly;
```

### 3. Create a Mock FunctionContext

The `FunctionContext` is required by the Azure Functions framework. You can mock it using Moq:

```csharp
var mockFunctionContext = new Mock<FunctionContext>();
mockFunctionContext.Setup(x => x.InvocationId).Returns(Guid.NewGuid().ToString());
mockFunctionContext.Setup(x => x.InstanceServices).Returns(serviceProvider);
```

### 4. Mock External Dependencies

The fake client will execute your activity code with real dependencies. You should mock services that cross boundaries:

- External APIs (HTTP clients, SDK clients)
- Database connections
- Storage services (blobs, queues, tables)
- Third-party services

```csharp
// Example: Mock an HTTP client
var mockHttpClient = new Mock<IHttpClient>();
mockHttpClient.Setup(x => x.GetAsync(It.IsAny<string>()))
    .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

services.AddSingleton(mockHttpClient.Object);
```

### 5. Configure Logging (Optional)

For better test debugging, configure logging:

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});
```

---

## Basic Usage

```csharp
// Arrange
var services = new ServiceCollection();
services.AddSingleton<MyOrchestrator>();
services.AddSingleton<MyActivities>();
// ... register other dependencies

var serviceProvider = services.BuildServiceProvider();
var mockFunctionContext = new Mock<FunctionContext>();
mockFunctionContext.Setup(x => x.InstanceServices).Returns(serviceProvider);

var durableClient = new FakeDurableTaskClient(
    serviceProvider,
    typeof(MyOrchestrator).Assembly,
    mockFunctionContext.Object,
    () => DateTime.UtcNow);

// Act
var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
    TaskName.From("MyOrchestrator"),
    new MyInput { Value = "test" });

var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

// Assert
result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
```

---

## Feature Documentation

### Testing with Timer Override

**Use Case:** Testing orchestrations with `CreateTimer` delays

Long-running orchestrations often use timers for delays, retries, or polling. In tests, you don't want to wait for real-time delays.

```csharp
[Fact]
public async Task MyOrchestrator_WithTimerOverride_CompletesQuickly()
{
    // Arrange
    var durableClient = new FakeDurableTaskClient(
        serviceProvider,
        typeof(MyOrchestrator).Assembly,
        mockFunctionContext.Object,
        () => DateTime.UtcNow)
        .WithOverrideAllTimerTimes(TimeSpan.FromMilliseconds(10));

    // Act
    var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
        TaskName.From("MyOrchestrator"),
        new MyInput());

    var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

    // Assert
    result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
}
```

**What it does:** Every `context.CreateTimer()` call is replaced with `Task.Delay(10ms)` instead of the actual delay time.

**Common patterns to test:**
- Retry loops with exponential backoff
- Polling external APIs
- Timeout scenarios

---

### Testing with External Events

**Use Case:** Testing orchestrations that wait for human approval or external signals

Your orchestration may use `WaitForExternalEvent<T>()` to pause and wait for input. You can pre-configure the event payload so the test doesn't block.

```csharp
[Fact]
public async Task ApprovalOrchestrator_WithPreConfiguredApproval_Succeeds()
{
    // Arrange
    var durableClient = new FakeDurableTaskClient(
        serviceProvider,
        typeof(ApprovalOrchestrator).Assembly,
        mockFunctionContext.Object,
        () => DateTime.UtcNow)
        .WithEventPayload("ApprovalEvent", new ApprovalEvent
        {
            Approved = true,
            Approver = "Test User",
            Reason = "Approved in test"
        });

    // Act
    var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
        TaskName.From("ApprovalOrchestrator"),
        new ApprovalRequest { RequestId = "123" });

    var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

    // Assert
    result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
}
```

**What it does:** When `WaitForExternalEvent("ApprovalEvent")` is called, it immediately returns the pre-configured payload instead of waiting.

**Alternative: Raise events mid-execution**

```csharp
[Fact]
public async Task ApprovalOrchestrator_RaiseEventDuringExecution_Succeeds()
{
    // Arrange
    var durableClient = new FakeDurableTaskClient(
        serviceProvider,
        typeof(ApprovalOrchestrator).Assembly,
        mockFunctionContext.Object,
        () => DateTime.UtcNow);

    // Act - Start orchestration
    var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
        TaskName.From("ApprovalOrchestrator"),
        new ApprovalRequest { RequestId = "123" });

    // Give it time to reach the WaitForExternalEvent
    await Task.Delay(100);

    // Raise the event externally
    await durableClient.RaiseEventAsync(instanceId, "ApprovalEvent",
        new ApprovalEvent { Approved = true, Approver = "Manual Approver" });

    var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

    // Assert
    result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
}
```

---

### Testing with Durable Entities

**Use Case:** Testing orchestrations that interact with durable entities

Your orchestration may use entities for state management, rate limiting, or distributed locking.

```csharp
[Fact]
public async Task EntityOrchestrator_WithMockEntity_CorrectlyIncrements()
{
    // Arrange
    var mockEntity = new MockCounterEntity
    {
        InitialValue = 100
    };

    var durableClient = new FakeDurableTaskClient(
        serviceProvider,
        typeof(EntityOrchestrator).Assembly,
        mockFunctionContext.Object,
        () => DateTime.UtcNow)
        .WithEntities((
            new EntityInstanceId("CounterEntity", "test-counter"),
            mockEntity));

    // Act
    var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
        TaskName.From("EntityOrchestrator"),
        new EntityRequest { CounterId = "test-counter" });

    var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

    // Assert
    result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);

    // Verify entity state after execution
    var entityState = await durableClient.Entities.GetEntityAsync<CounterState>(
        new EntityInstanceId("CounterEntity", "test-counter"));

    entityState.State.Value.Should().Be(106); // 100 + 1 + 1+2+3+4+5
}
```

**For entities implementing `ITestableEntity<T>`:**

```csharp
public class CounterEntity : TaskEntity<CounterState>, ITestableEntity<CounterState>
{
    public Task<int> Get() => Task.FromResult(State.Value);

    public CounterState GetState() => State;
}
```

**What it does:**
- `WithEntities()` registers your mock or real entity with the fake client
- Entity operations (`CallEntityAsync`, `SignalEntityAsync`) work against the provided entity
- `GetEntityAsync<T>()` retrieves the entity's state after execution

**Mock entity example:**

```csharp
public class MockCounterEntity : ITestableEntity<CounterState>
{
    private int _value = 0;

    public Task Increment(int amount)
    {
        _value += amount;
        return Task.CompletedTask;
    }

    public Task<int> Get() => Task.FromResult(_value);

    public CounterState GetState() => new() { Value = _value };
}
```

---

### Testing Sub-Orchestrations

**Use Case:** Testing orchestrations that call other orchestrations

```csharp
[Fact]
public async Task ParentOrchestrator_WaitForAllInstances_CompletesParentAndChildren()
{
    // Arrange
    var durableClient = new FakeDurableTaskClient(
        serviceProvider,
        typeof(ParentOrchestrator).Assembly,
        mockFunctionContext.Object,
        () => DateTime.UtcNow);

    // Act
    var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
        TaskName.From("ParentOrchestrator"),
        new ParentRequest { Regions = new[] { "US", "EU", "APAC" } });

    // Wait for parent and all child orchestrations
    var results = await durableClient.WaitForInstancesAndChildren();

    // Assert
    results.Should().HaveCountGreaterThan(1); // Parent + children
    results.All(r => r.RuntimeStatus == OrchestrationRuntimeStatus.Completed)
        .Should().BeTrue();
}
```

**What it does:** `WaitForInstancesAndChildren()` waits for the parent orchestration and all sub-orchestrations spawned during execution to complete.

---

### Testing Error Handling

**Use Case:** Testing how your orchestration handles activity failures

```csharp
[Fact]
public async Task MyOrchestrator_WhenActivityFails_HandlesGracefully()
{
    // Arrange
    var durableClient = new FakeDurableTaskClient(
        serviceProvider,
        typeof(MyOrchestrator).Assembly,
        mockFunctionContext.Object,
        () => DateTime.UtcNow);

    // Act
    var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
        TaskName.From("MyOrchestrator"),
        new MyInput { ShouldFail = true });

    var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

    // Assert
    result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Failed);
    result.FailureDetails.Should().NotBeNull();
    result.FailureDetails.ExceptionType.Should().Be("System.InvalidOperationException");
    result.FailureDetails.Message.Should().Contain("Something went wrong");
}
```

---

### Getting Orchestration Instances

```csharp
// Get all instance IDs created during the test
var instances = durableClient.GetInstances();

// Wait for a specific instance
var metadata = await durableClient.WaitForInstanceCompletionAsync(instanceId);

// Check runtime status
metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);

// Check failure details if failed
if (metadata.RuntimeStatus == OrchestrationRuntimeStatus.Failed)
{
    metadata.FailureDetails.Should().NotBeNull();
    Console.WriteLine(metadata.FailureDetails.Message);
}
```

---

## Complete Test Example

Here's a complete example showing all the pieces together:

```csharp
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class MyOrchestratorTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private Mock<FunctionContext> _mockFunctionContext = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // Configure logging
        services.AddLogging(builder => builder.AddConsole());

        // Register orchestrators
        services.AddSingleton<MyOrchestrator>();
        services.AddSingleton<ChildOrchestrator>();

        // Register activities
        services.AddSingleton<MyActivities>();

        // Register mocked dependencies
        var mockHttpClient = new Mock<IHttpClient>();
        mockHttpClient.Setup(x => x.GetAsync(It.IsAny<string>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        services.AddSingleton(mockHttpClient.Object);

        _serviceProvider = services.BuildServiceProvider();

        _mockFunctionContext = new Mock<FunctionContext>();
        _mockFunctionContext.Setup(x => x.InvocationId).Returns(Guid.NewGuid().ToString());
        _mockFunctionContext.Setup(x => x.InstanceServices).Returns(_serviceProvider);
    }

    public Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MyOrchestrator_WithValidInput_CompletesSuccessfully()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            _serviceProvider,
            typeof(MyOrchestrator).Assembly,
            _mockFunctionContext.Object,
            () => DateTime.UtcNow)
            .WithOverrideAllTimerTimes(TimeSpan.FromMilliseconds(10));

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            TaskName.From("MyOrchestrator"),
            new MyInput { Value = "test" });

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task MyOrchestrator_WithExternalEvent_Succeeds()
    {
        // Arrange
        var durableClient = new FakeDurableTaskClient(
            _serviceProvider,
            typeof(MyOrchestrator).Assembly,
            _mockFunctionContext.Object,
            () => DateTime.UtcNow)
            .WithEventPayload("ApprovalEvent", new ApprovalEvent { Approved = true });

        // Act
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            TaskName.From("MyOrchestrator"),
            new MyInput { NeedsApproval = true });

        var result = await durableClient.WaitForInstanceCompletionAsync(instanceId);

        // Assert
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }
}
```

---

## API Reference

### FakeDurableTaskClient

#### Constructor

```csharp
public FakeDurableTaskClient(
    IServiceProvider serviceProvider,
    Assembly functionAssembly,
    FunctionContext functionContext,
    Func<DateTime> getCurrentUtcTime)
```

**Parameters:**
- `serviceProvider` - The DI container containing your orchestrators, activities, and their dependencies
- `functionAssembly` - The assembly containing your orchestrator functions (use `typeof(MyOrchestrator).Assembly`)
- `functionContext` - The mocked Azure Functions `FunctionContext`
- `getCurrentUtcTime` - A function that returns the current UTC time (use `() => DateTime.UtcNow` for real time, or provide a fixed value for deterministic tests)

#### Configuration Methods

| Method | Description |
|--------|-------------|
| `WithOverrideAllTimerTimes(TimeSpan?)` | Replaces all `CreateTimer` delays with the specified duration |
| `WithEventPayload(string eventName, object? payload)` | Pre-configures an external event response |
| `WithEntities(params (EntityInstanceId, ITaskEntity)[])` | Registers entities for use in orchestrations |

#### Execution Methods

| Method | Description |
|--------|-------------|
| `ScheduleNewOrchestrationInstanceAsync(...)` | Starts a new orchestration instance |
| `WaitForInstanceCompletionAsync(string instanceId)` | Waits for a single instance to complete |
| `WaitForInstancesAndChildren()` | Waits for parent and all sub-orchestrations |
| `RaiseEventAsync(string instanceId, string eventName, object? payload)` | Sends an external event to a running orchestration |
| `GetInstances()` | Returns all instance IDs created during the test |

---

## Limitations

- **No actual timer accuracy:** Timers are replaced with `Task.Delay`, so they're not wall-clock accurate
- **No replay behavior:** The fake doesn't simulate the deterministic replay that real Durable Functions performs
- **In-memory only:** All state is kept in memory and lost after the test
- **No cross-instance communication:** Some cross-instance patterns may not work exactly as in production

---

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

---

## License

MIT License

Copyright (c) 2025 DurableTask.Testing Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

