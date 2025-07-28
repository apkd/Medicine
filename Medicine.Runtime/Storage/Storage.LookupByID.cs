using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static class LookupByID<T, TId>
            where T : class, IFindByID<TId>
            where TId : unmanaged, IEquatable<TId>
        {
            public static readonly Dictionary<TId, T> Map = new(capacity: 8);

            public static void Register(T target)
                => Map[target.ID] = target;

            public static void Unregister(T target)
                => Map.Remove(target.ID);

            public static void Reinitialize()
            {
                Map.Clear();
                foreach (var instance in Find.Instances<T>())
                    Map[instance.ID] = instance;
            }
        }
    }
}
