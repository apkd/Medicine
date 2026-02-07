#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static System.Runtime.CompilerServices.MethodImplOptions;
using static UnityEngine.Debug;

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static class LookupByID<T, TId>
            where T : class, IFindByID<TId>
            where TId : unmanaged, IEquatable<TId>
        {
            [EditorBrowsable(EditorBrowsableState.Never)]
            public static readonly Dictionary<TId, T> Map = new(capacity: 8);

            [MethodImpl(NoInlining)]
            static void LogIdMismatch(TId id, T instance)
                => LogError($"ID mismatch for {typeof(T).Name}: {id} != {instance.ID} (instance: {instance})");

            [MethodImpl(AggressiveInlining)]
            public static T? Find(TId id)
            {
                bool ok = Map.TryGetValue(id, out T? result);
#if DEBUG
                if (ok)
                    if (!result.ID.Equals(id))
                        LogIdMismatch(id, result);
#endif
                return result;
            }

            [MethodImpl(AggressiveInlining)]
            public static bool TryFind(TId id, [NotNullWhen(true)] out T? result)
            {
                bool ok = Map.TryGetValue(id, out result);
#if DEBUG
                if (ok)
                    if (!result.ID.Equals(id))
                        LogIdMismatch(id, result);
#endif
                return ok;
            }

            public static void Register(T target)
            {
                var id = target.ID;
#if DEBUG
                if (Map.TryGetValue(id, out T? other))
                    LogError($"Duplicate instance with ID {id} for {typeof(T).Name}: '{target}' vs '{other}'");
#endif
                Map[id] = target;
            }

            public static void Unregister(T target)
            {
                var id = target.ID;
#if DEBUG
                if (!Map.TryGetValue(id, out var stored) || stored is null)
                {
                    LogError($"Trying to unregister a missing instance: {typeof(T).Name} with ID {id} not found.");
                }
                else
                {
                    // detect that the target's ID changed after registration
                    if (!stored.ID.Equals(id))
                        LogIdMismatch(id, stored);

                    // detect that a different instance is stored under the target's ID
                    if (!ReferenceEquals(stored, target))
                    {
                        var type = typeof(T).Name;
                        LogError($"Instance mismatch for {type} with ID {id}: map has '{stored}', tried to unregister '{target}'.");
                    }
                }
#endif
                Map.Remove(id);
            }

            public static void Reinitialize()
            {
                var instances = Medicine.Find.Instances<T>();

                Map.Clear();
                Map.EnsureCapacity(instances.Count);

                foreach (var instance in instances)
                    Map[instance.ID] = instance;
            }
        }
    }
}
