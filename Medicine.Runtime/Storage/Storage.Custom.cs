#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using static System.ComponentModel.EditorBrowsableState;
using static System.Runtime.CompilerServices.MethodImplOptions;
using static UnityEngine.Debug;

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(Never)]
        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public static class Custom<T, TStorage> where T : class, ICustomStorage<TStorage>
        {
            public static TStorage StorageData = default!;
            static int registeredCount;

            public static ref TStorage Storage
            {
                [MethodImpl(AggressiveInlining)]
                get => ref StorageData;
            }

            public static void Register(T instance)
            {
#if DEBUG
                if (ReferenceEquals(instance, null))
                {
                    LogError(
                        $"Tried to register a null instance of {typeof(T).Name} in custom storage. " +
                        "This probably indicates a logic error in your code."
                    );
                    return;
                }
#endif
                if (registeredCount is 0)
                    InitializeStorage(instance);

                RegisterInstance(instance);
                registeredCount++;
            }

            public static void Unregister(T instance, int instanceIndex)
            {
                if (instanceIndex < 0)
                    return;

#if DEBUG
                if (ReferenceEquals(instance, null))
                {
                    LogError(
                        $"Tried to unregister a null instance of {typeof(T).Name} from custom storage. " +
                        "This probably indicates a logic error in your code."
                    );
                    return;
                }

                if (registeredCount <= 0)
                {
                    LogError(
                        $"Tried to unregister {typeof(T).Name} from custom storage with zero registered instances. " +
                        "This probably indicates a logic error in your code."
                    );
                    return;
                }
#endif
                UnregisterInstance(instance, instanceIndex);
                registeredCount--;

                if (registeredCount is 0)
                    DisposeStorage(instance);
            }

            static void RegisterInstance(T instance)
            {
                try
                {
                    instance.RegisterInstance(ref StorageData);
                }
                catch (Exception ex)
                {
                    LogException(ex);
                }
            }

            static void UnregisterInstance(T instance, int instanceIndex)
            {
                try
                {
                    instance.UnregisterInstance(ref StorageData, instanceIndex);
                }
                catch (Exception ex)
                {
                    LogException(ex);
                }
            }

            [MethodImpl(NoInlining)]
            static void InitializeStorage(T instance)
            {
                try
                {
                    instance.InitializeStorage(ref StorageData);
                }
                catch (Exception ex)
                {
                    StorageData = default!;
                    LogException(ex);
                }
            }

            [MethodImpl(NoInlining)]
            static void DisposeStorage(T instance)
            {
                try
                {
                    instance.DisposeStorage(ref StorageData);
                }
                catch (Exception ex)
                {
                    LogException(ex);
                }
                StorageData = default!;
            }
        }
    }
}
