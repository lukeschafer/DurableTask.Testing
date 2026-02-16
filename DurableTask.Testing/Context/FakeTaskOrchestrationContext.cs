using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.Reflection;

namespace DurableTask.Testing.Context
{
    public class FakeTaskOrchestrationContext(
            string instanceId,
            object? input,
            ILoggerFactory loggerFactory,
            IServiceProvider serviceProvider,
            FunctionContext functionContext,
            Assembly functionAssembly,
            TimeSpan? timerOverride,
            Func<DateTime> currentDateTimeUtcOverride,
            Dictionary<string, object?> eventPayloads,
            Func<Task> continueAsNew,
            TaskOrchestrationEntityFeature taskOrchestrationEntities,
            Dictionary<string, FakeTaskOrchestrationContext> knownContexts)
            : TaskOrchestrationContext
    {
        private readonly Dictionary<string, TaskCompletionSource<object>> _eventTasks = new();
        private readonly Dictionary<string, object?> _eventPayloads = eventPayloads;
        public object? Output { get; private set; }

        public override string InstanceId { get; } = instanceId;
        public override TaskName Name { get; } = new TaskName("FakeTaskOrchestration");

        public override ParentOrchestrationInstance? Parent => null;

        public override bool IsReplaying => false;

        protected override ILoggerFactory LoggerFactory { get; } = loggerFactory;

        public override TaskOrchestrationEntityFeature Entities => taskOrchestrationEntities;

        public override T GetInput<T>()
        {
            return (T)input!;
        }

        public override DateTime CurrentUtcDateTime => currentDateTimeUtcOverride();

        public void SetOutput(object? output)
        {
            Output = output;
        }

        private object? CallActivityInternalAsync(TaskName name, object? activityInput)
        {
            var activityMethod = functionAssembly
                .GetTypes()
                .SelectMany(x => x.GetMethods())
                .FirstOrDefault(x => x.GetCustomAttribute<FunctionAttribute>()?.Name == name.Name);
            var activityType = activityMethod?.DeclaringType;

            if (activityMethod == null || activityType == null)
                throw new InvalidOperationException($"No activity found for {name.Name}");

            var activityInstance = serviceProvider.GetService(activityType);
            if (activityInstance == null)
                throw new InvalidOperationException($"No service registered for {name.Name}");

            // Invoke the activity method and capture the result
            return activityMethod.InvokeWithDefaults(activityInstance, serviceProvider, [activityInput, functionContext, CancellationToken.None])!;
        }

        public override Task CallActivityAsync(TaskName name, object? activityInput = null, TaskOptions? options = null)
        {
            try
            {
                var result = CallActivityInternalAsync(name, activityInput);
                Task.Delay(10).Wait();
                if (result is Task taskResult)
                {
                    if (taskResult.IsFaulted) return Task.FromException(new TaskFailedException(name.Name, 1, taskResult.Exception));
                    return taskResult;
                }

                throw new InvalidOperationException($"Activity method {name.Name} returned an unexpected type");
            }
            catch (Exception ex)
            {
                return Task.FromException(new TaskFailedException(name.Name, 1, ex));
            }
        }

        public override Task<TResult> CallActivityAsync<TResult>(TaskName name, object? activityInput = null, TaskOptions? options = null)
        {
            // Simulate calling an activity
            try
            {
                var result = CallActivityInternalAsync(name, activityInput);
                Task.Delay(10).Wait();

                return result switch
                {
                    Task<TResult> taskResult => taskResult.IsFaulted
                        ? Task.FromException<TResult>(new TaskFailedException(name.Name, 1, taskResult.Exception))
                        : taskResult,
                    TResult directResult => Task.FromResult(directResult),
                    _ => throw new InvalidOperationException($"Activity method {name.Name} returned an unexpected type")
                };
            }
            catch (Exception ex)
            {
                return Task.FromException<TResult>(new TaskFailedException(name.Name, 1, ex));
            }
        }


        public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
        {
            if (timerOverride != null)
                return Task.Delay(timerOverride.Value, cancellationToken);

            // Simulate creating a timer.
            return Task.Delay(fireAt - DateTime.UtcNow, cancellationToken);
        }

        public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_eventTasks)
            {
                _eventTasks[eventName] = tcs;
            }

            if (cancellationToken != CancellationToken.None)
            {
                cancellationToken.Register(() =>
                {
                    if (tcs.TrySetCanceled())
                    {
                        Console.WriteLine($"WaitForExternalEvent for '{eventName}' was canceled.");
                    }
                });
            }

            // If the event payload is already available, send the event immediately
            if (_eventPayloads.TryGetValue(eventName, out var payload))
            {
                lock (_eventTasks)
                {
                    if (_eventTasks.Remove(eventName))
                    {
                        tcs.TrySetResult(payload!);
                    }
                }
            }

            return tcs.Task.ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    throw new TaskCanceledException($"The wait for external event '{eventName}' was canceled.");
                }

                return (T)t.Result;
            },
                cancellationToken);
        }

        public override void SendEvent(string instanceId, string eventName, object payload)
        {
            TaskCompletionSource<object>? tcs;

            lock (_eventTasks)
            {
                _eventTasks.TryGetValue(eventName, out tcs);
            }

            if (tcs == null)
            {
                _eventPayloads.Add(eventName, payload);
            }
            else
            {
                tcs?.TrySetResult(payload);
            }
        }

        public override void SetCustomStatus(object? customStatus)
        {
            // Custom status simulation.
        }

        public override async Task<TResult> CallSubOrchestratorAsync<TResult>(TaskName name,
            object? activityInput = null,
            TaskOptions? options = null)
        {
            var orchestrationMethod = functionAssembly
                .GetTypes()
                .SelectMany(x => x.GetMethods())
                .FirstOrDefault(x => x.GetCustomAttribute<FunctionAttribute>()?.Name == name.Name);
            var orchestrationType = orchestrationMethod?.DeclaringType;

            if (orchestrationMethod == null || orchestrationType == null)
                throw new InvalidOperationException($"No orchestration found for {name.Name}");

            var orchestratorInstance = serviceProvider.GetService(orchestrationType);
            if (orchestratorInstance == null)
                throw new InvalidOperationException($"No service registered for {name.Name}");

            var newContextId = Guid.NewGuid().ToString();
            var context = new FakeTaskOrchestrationContext(newContextId,
                activityInput,
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                serviceProvider,
                functionContext,
                functionAssembly,
                timerOverride,
                currentDateTimeUtcOverride,
                _eventPayloads,
                () => CallSubOrchestratorAsync(name, activityInput, options),
                taskOrchestrationEntities,
                knownContexts);

            knownContexts.Add(newContextId, context);

            await orchestrationMethod.InvokeWithDefaultsAsync(orchestratorInstance,
                serviceProvider,
                [context, functionContext, CancellationToken.None])!;

            return default!;
        }

        public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
        {
            continueAsNew().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public override Guid NewGuid()
        {
            return Guid.NewGuid();
        }
    }
}
