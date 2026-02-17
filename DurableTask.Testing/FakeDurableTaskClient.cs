using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.DurableTask.Entities;
using Moq;
using Microsoft.DurableTask.Client.Entities;
using NodaTime;
using DurableTask.Testing.Context;

namespace DurableTask.Testing
{
    public class FakeDurableTaskClient : DurableTaskClient
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<object?>> _orchestrations = new();

        private TimeSpan? _timerOverride = null;

        public FakeDurableTaskClient WithOverrideAllTimerTimes(TimeSpan? timerOverride)
        {
            _timerOverride = timerOverride;
            return this;
        }

        private FakeEntities _taskOrchestrationEntityFeature = new();

        public FakeDurableTaskClient WithOverrideEntities(FakeEntities taskOrchestrationEntities)
        {
            _taskOrchestrationEntityFeature = taskOrchestrationEntities;
            return this;
        }

        public FakeDurableTaskClient WithEntities(params (EntityInstanceId id, ITaskEntity taskEntity)[] entities)
        {
            foreach (var entity in entities)
                _taskOrchestrationEntityFeature.AddEntity(entity.id, entity.taskEntity);
            return this;
        }


        private Dictionary<string, FakeTaskOrchestrationContext> _knownContexts = new Dictionary<string, FakeTaskOrchestrationContext>();

        private Dictionary<string, object?> _eventPayloads = new Dictionary<string, object?>();
        private readonly IServiceProvider _serviceProvider;
        private readonly Assembly _functionAssembly;
        private readonly FunctionContext _functionContext;
        private readonly Func<DateTime> _getCurrentUtcTime;

        public FakeDurableTaskClient(IServiceProvider serviceProvider,
            Assembly functionAssembly,
            FunctionContext functionContext,
            Func<DateTime> getCurrentUtcTime) : base("fake")
        {
            _serviceProvider = serviceProvider;
            _functionAssembly = functionAssembly;
            _functionContext = functionContext;
            _getCurrentUtcTime = getCurrentUtcTime;
        }

        public FakeDurableTaskClient WithEventPayload(string eventName, object? payload)
        {
            _eventPayloads[eventName] = payload;
            return this;
        }

        public override DurableEntityClient Entities => new FakeDurableEntityClient(_taskOrchestrationEntityFeature);

        public override Task<string> ScheduleNewOrchestrationInstanceAsync(TaskName orchestratorName,
            object? input = null,
            StartOrchestrationOptions? options = null,
            CancellationToken cancellation = new())
        {
            var instanceId = options?.InstanceId ?? Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<object?>();
            _orchestrations[instanceId] = tcs;

            Task.Run(async () =>
                {
                    try
                    {
                        var orchestratorMethod = _functionAssembly
                            .GetTypes()
                            .SelectMany(x => x.GetMethods())
                            .FirstOrDefault(x => x.GetCustomAttribute<FunctionAttribute>()?.Name == orchestratorName.Name);
                        var orchestratorType = orchestratorMethod?.DeclaringType;

                        if (orchestratorType == null)
                        {
                            tcs.TrySetException(new InvalidOperationException($"No orchestrator found for {orchestratorName.Name}"));
                            return;
                        }

                        var orchestratorInstance = _serviceProvider.GetService(orchestratorType);
                        if (orchestratorInstance == null)
                        {
                            tcs.TrySetException(new InvalidOperationException($"No orchestrator registered for {orchestratorName.Name}"));
                            return;
                        }

                        var orchestrationMethod = orchestratorType.GetMethod(orchestratorMethod!.Name);
                        if (orchestrationMethod == null)
                        {
                            tcs.TrySetException(new InvalidOperationException($"Method 'OrchestrationTrigger' not found in {orchestratorName.Name}"));
                            return;
                        }

                        var context = new FakeTaskOrchestrationContext(instanceId,
                            input,
                            _serviceProvider.GetRequiredService<ILoggerFactory>(),
                            _serviceProvider,
                            _functionContext,
                            _functionAssembly,
                            _timerOverride,
                            _getCurrentUtcTime,
                            _eventPayloads,
                            () => ScheduleNewOrchestrationInstanceAsync(orchestratorName,
                                input,
                                new StartOrchestrationOptions { InstanceId = Guid.NewGuid().ToString() }),
                            _taskOrchestrationEntityFeature!,
                            _knownContexts);

                        _knownContexts.Add(instanceId, context);

                        await orchestrationMethod.InvokeWithDefaultsAsync(orchestratorInstance,
                            _serviceProvider,
                            [context, _functionContext, CancellationToken.None])!;

                        tcs.TrySetResult(context.Output);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                cancellation);

            return Task.FromResult(instanceId);
        }

        public override Task RaiseEventAsync(string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = new())
        {
            if (!_knownContexts.TryGetValue(instanceId, out var tcs))
                throw new InvalidOperationException("Instance not found");

            var instance = _knownContexts[instanceId];
            instance.SendEvent(instanceId, eventName, eventPayload ?? new object()!);
            return Task.CompletedTask;
        }

        public async Task<List<OrchestrationMetadata>> WaitForInstancesAndChildren()
        {
            var alreadyCheckedInstances = new HashSet<string>();
            var results = new List<OrchestrationMetadata>();

            for (var depth = 0; depth < 3; depth++)
            {
                var instances = GetInstances();
                foreach (var instance in instances)
                {
                    if (alreadyCheckedInstances.Contains(instance)) continue;
                    results.Add(await WaitForInstanceCompletionAsync(instance));
                    alreadyCheckedInstances.Add(instance);
                }

                await Task.Delay(10);
            }

            return results;
        }

        public string[] GetInstances()
        {
            return _orchestrations.Keys.ToArray();
        }

        public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
            string instanceId,
            bool getInputsAndOutputs = false,
            CancellationToken cancellation = new())
        {
            if (_orchestrations.TryGetValue(instanceId, out var tcs))
            {
                return tcs.Task.ContinueWith(t => new OrchestrationMetadata(instanceId, instanceId)
                    {
                        RuntimeStatus =
                            t.IsCanceled ? OrchestrationRuntimeStatus.Suspended :
                            t.IsFaulted ? OrchestrationRuntimeStatus.Failed :
                            OrchestrationRuntimeStatus.Completed,
                        FailureDetails = t.Exception == null ? null : TaskFailureDetails.FromException(t.Exception),
                    },
                    cancellation);
            }

            throw new InvalidOperationException("Instance not found");
        }

        public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(string instanceId,
            bool getInputsAndOutputs = false,
            CancellationToken cancellation = default)
        {
            if (_orchestrations.ContainsKey(instanceId))
            {
                return Task.FromResult(new OrchestrationMetadata(instanceId, instanceId) { RuntimeStatus = OrchestrationRuntimeStatus.Running });
            }

            throw new InvalidOperationException("Instance not found");
        }

        public override Task SuspendInstanceAsync(string instanceId, string? reason = null, CancellationToken cancellation = default)
        {
            if (_orchestrations.TryGetValue(instanceId, out var tcs))
            {
                tcs.TrySetCanceled();
                return Task.CompletedTask;
            }

            throw new InvalidOperationException("Instance not found");
        }

        public override Task ResumeInstanceAsync(string instanceId, string? reason = null, CancellationToken cancellation = default)
        {
            if (_orchestrations.ContainsKey(instanceId))
            {
                return Task.CompletedTask; // Simulate resumption.
            }

            throw new InvalidOperationException("Instance not found");
        }

        public override Task<OrchestrationMetadata?> GetInstancesAsync(string instanceId,
            bool getInputsAndOutputs = false,
            CancellationToken cancellation = default)
        {
            return Task.FromResult<OrchestrationMetadata?>(new OrchestrationMetadata(Guid.NewGuid().ToString(), instanceId));
        }

        public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
        {
            return new FakeOrchestrationMetadataAsyncPageable();
        }

        // Other overrides can remain unchanged or have similar custom handling.

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}