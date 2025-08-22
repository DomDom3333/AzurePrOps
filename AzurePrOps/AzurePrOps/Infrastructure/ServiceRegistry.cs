using System;
using System.Collections.Concurrent;

namespace AzurePrOps.Infrastructure
{
    /// <summary>
    /// Minimalistic service registry to decouple UI from concrete implementations
    /// without introducing a full DI container. Intended as a composition root.
    /// </summary>
    public static class ServiceRegistry
    {
        private static readonly ConcurrentDictionary<Type, object> _services = new();

        public static void Register<T>(T instance) where T : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _services[typeof(T)] = instance;
        }

        public static T? Resolve<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var instance))
            {
                return (T)instance;
            }
            return null;
        }

        public static bool TryResolve<T>(out T? instance) where T : class
        {
            var resolved = Resolve<T>();
            instance = resolved;
            return resolved != null;
        }

        public static void Clear()
        {
            _services.Clear();
        }
    }
}