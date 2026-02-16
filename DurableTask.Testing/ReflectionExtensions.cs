using System.Reflection;

namespace DurableTask.Testing
{
    public static class ReflectionExtensions
    {
        public static async Task<object?> InvokeWithDefaultsAsync(this MethodInfo method, object? instance, IServiceProvider serviceProvider, params object?[] possibleArguments)
        {
            var invokeResult = method.InvokeWithDefaults(instance, serviceProvider, possibleArguments);

            if (invokeResult is Task task)
            {
                await task;
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task);
            }

            return invokeResult;
        }

        public static object? InvokeWithDefaults(this MethodInfo method, object? instance, IServiceProvider serviceProvider, params object?[] possibleArguments)
        {
            var parameterInfos = method.GetParameters();
            var finalArgs = new object?[parameterInfos.Length];

            for (var i = 0; i < parameterInfos.Length; i++)
            {
                var paramType = parameterInfos[i].ParameterType;
                var matchedArg = FindMatchingArgument(paramType, possibleArguments)
                                 ?? serviceProvider.GetService(paramType)
                                 ?? GetDefaultValue(paramType);

                finalArgs[i] = matchedArg;
            }

            return method.Invoke(instance, finalArgs);
        }

        private static object? FindMatchingArgument(Type targetType, object?[] possibleArguments)
        {
            foreach (var arg in possibleArguments)
            {
                if (arg != null && targetType.IsInstanceOfType(arg))
                {
                    return arg;
                }
            }
            return null;
        }

        private static object? GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
