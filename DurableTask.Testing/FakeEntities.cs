using Microsoft.DurableTask;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using Moq;

namespace DurableTask.Testing
{
    public class FakeEntities : TaskOrchestrationEntityFeature
    {
        public FakeEntities()
        {
        }

        public FakeEntities((EntityInstanceId id, ITaskEntity taskEntity)[] entities)
        {
            foreach (var entity in entities)
            {
                AddEntity(entity.id, entity.taskEntity);
            }
        }

        public readonly Dictionary<string, ITaskEntity> Tasks = new();

        public void AddEntity(EntityInstanceId id, ITaskEntity taskEntity)
        {
            var key = $"{id.Key}-{id.Name}";
            if (Tasks.ContainsKey(key))
                Tasks[key] = taskEntity;
            else Tasks.Add(key, taskEntity);
        }

        public override Task<TResult> CallEntityAsync<TResult>(EntityInstanceId id,
            string operationName,
            object? input = null,
            CallEntityOptions? options = null)
        {
            var entity = Tasks[$"{id.Key}-{id.Name}"];
            var method = entity.GetType().GetMethod(operationName);
            return (Task<TResult>)method!.Invoke(entity, input == null ? [] : [input])!;
        }

        public override Task CallEntityAsync(EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null)
        {
            var entity = Tasks[$"{id.Key}-{id.Name}"];
            var method = entity.GetType().GetMethod(operationName);
            return (Task)method!.Invoke(entity, [input!])!;
        }

        public override bool InCriticalSection([NotNullWhen(true)] out IReadOnlyList<EntityInstanceId>? entityIds)
        {
            throw new NotImplementedException();
        }

        public override Task<IAsyncDisposable> LockEntitiesAsync(IEnumerable<EntityInstanceId> entityIds)
        {
            return Task.FromResult(new Mock<IAsyncDisposable>().Object);
        }

        public override Task SignalEntityAsync(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null)
        {
            throw new NotImplementedException();
        }
    }

    public class FakeDurableEntityClient(FakeEntities fakeEntities) : DurableEntityClient("Test")
    {
        public override Task<CleanEntityStorageResult> CleanEntityStorageAsync(CleanEntityStorageRequest? request = null,
            bool continueUntilComplete = true,
            CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override AsyncPageable<EntityMetadata> GetAllEntitiesAsync(EntityQuery? filter = null)
        {
            throw new NotImplementedException();
        }

        public override AsyncPageable<EntityMetadata<T>> GetAllEntitiesAsync<T>(EntityQuery? filter = null)
        {
            throw new NotImplementedException();
        }

        public override async Task<EntityMetadata?> GetEntityAsync(EntityInstanceId id,
            bool includeState = true,
            CancellationToken cancellation = default)
        {
            var task = fakeEntities.Tasks[$"{id.Key}-{id.Name}"];
            await Task.CompletedTask;
            var method = task.GetType().GetMethod("GetState");
            var state = method!.Invoke(task, []);
            return new EntityMetadata(id, new Microsoft.DurableTask.Client.SerializedData(JsonConvert.SerializeObject(state)));
        }

        public override async Task<EntityMetadata<T>?> GetEntityAsync<T>(EntityInstanceId id,
            bool includeState = true,
            CancellationToken cancellation = default)
        {
            var task = fakeEntities.Tasks[$"{id.Key}-{id.Name}"] as ITestableEntity<T>;
            await Task.CompletedTask;

            return new EntityMetadata<T>(id, task!.GetState());
        }

        public override async Task SignalEntityAsync(EntityInstanceId id,
            string operationName,
            object? input = null,
            SignalEntityOptions? options = null,
            CancellationToken cancellation = default)
        {
            await fakeEntities.CallEntityAsync(id, operationName, input);
        }
    }
}