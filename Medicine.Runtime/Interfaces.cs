using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Unity.Mathematics;

namespace Medicine
{
    /// <summary>
    /// Provides a <see cref="TrackAttribute"/>-marked class with an <c>InstanceIndex</c> property
    /// which is automatically updated to represent the index to the object's data in the instance list,
    /// <see cref="IUnmanagedData{TData}"/> arrays, <see cref="UnityEngine.Jobs.TransformAccessArray"/>, etc.
    /// </summary>
    public interface IInstanceIndex
    {
        /// <summary>
        /// The index of this instance. This property is automatically updated.
        /// </summary>
        int InstanceIndex
        {
            get => -1;
            set => _ = value;
        }
    }

    namespace Internal
    {
        /// <summary>
        /// In cases where multiple classes in the inheritance chain implement <see cref="IInstanceIndex"/>,
        /// it is difficult to access the correct implementation generically.
        /// This interface is used to disambiguate the <see cref="InstanceIndex"/> implementation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface IInstanceIndex<T>
        {
            int InstanceIndex { get; set; }
        }

        /// <summary>
        /// Marker interface implemented by all generated tracked class and interfaces.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface ITracked { }

        /// <summary>
        /// Marker interface implemented by all generated tracked class and interfaces.
        /// </summary>
        /// <typeparam name="TSelf">The tracked type itself.</typeparam>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface ITracked<TSelf> : ITracked where TSelf : ITracked<TSelf> { }

        /// <summary>
        /// Marker interface implemented by tracked types that use TransformAccess tracking.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface ITrackedTransformAccessArray : ITracked { }

        /// <summary>
        /// Marker interface implemented by tracked types that use TransformAccess tracking.
        /// </summary>
        /// <typeparam name="TSelf">The tracked type itself.</typeparam>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface ITrackedTransformAccessArray<TSelf> : ITracked<TSelf> where TSelf : ITrackedTransformAccessArray<TSelf> { }

        /// <summary>
        /// Marker interface implemented by tracked types that use unmanaged instance data tracking.
        /// </summary>
        /// <typeparam name="TSelf">The tracked type itself.</typeparam>
        /// <typeparam name="TData">The unmanaged data type.</typeparam>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface ITrackedUnmanagedData<TSelf, TData> : ITracked<TSelf>
            where TSelf : ITrackedUnmanagedData<TSelf, TData>, IUnmanagedData<TData>
            where TData : unmanaged { }
    }

    public interface IFindByID<out T> where T : unmanaged, IEquatable<T>
    {
        public T ID { get; }
    }

    /// <summary>
    /// Provides a <see cref="TrackAttribute"/>-marked ScriptableObject with a generated asset GUID ID.
    /// </summary>
    public interface IFindByAssetID : IFindByID<uint4> { }

    /// <summary>
    /// Provides a <see cref="TrackAttribute"/>-marked class with a storage container that can be
    /// used to track the active instances of the type.
    /// </summary>
    /// <typeparam name="TStorage">Type of custom storage.</typeparam>
    public interface ICustomStorage<TStorage>
    {
        void InitializeStorage(ref TStorage storage)
            => storage = Activator.CreateInstance<TStorage>();

        void DisposeStorage(ref TStorage storage)
        {
            if (storage is IDisposable disposable)
                disposable.Dispose();

            storage = default;
        }

        void RegisterInstance(ref TStorage storage);
        void UnregisterInstance(ref TStorage storage, int instanceIndex);
    }

    /// <summary>
    /// Provides a <see cref="TrackAttribute"/>-marked class with a list of tracked instance IDs.
    /// </summary>
    [SuppressMessage("ReSharper", "SuspiciousTypeConversion.Global")]
    public interface ITrackInstanceIDs : ICustomStorage<ITrackInstanceIDs.InstanceIDStorage>
    {
        void ICustomStorage<InstanceIDStorage>.InitializeStorage(ref InstanceIDStorage storage)
            => storage = new();

        void ICustomStorage<InstanceIDStorage>.DisposeStorage(ref InstanceIDStorage storage)
            => storage.InstanceIDs.Dispose();

        void ICustomStorage<InstanceIDStorage>.RegisterInstance(ref InstanceIDStorage storage)
            => storage.InstanceIDs.Add(((UnityEngine.Object)this).GetInstanceID());

        void ICustomStorage<InstanceIDStorage>.UnregisterInstance(ref InstanceIDStorage storage, int instanceIndex)
            => storage.InstanceIDs.RemoveAtSwapBack(instanceIndex);

        public sealed class InstanceIDStorage
        {
            public Unity.Collections.NativeList<int> InstanceIDs
                = new(initialCapacity: 8, Unity.Collections.Allocator.Persistent);
        }
    }

    /// <summary>
    /// Provides a <see cref="TrackAttribute"/>-marked class with an array of unmanaged structs,
    /// where each element is data related to a single instance of the class.
    /// </summary>
    /// <typeparam name="TData">The type of struct attached to this tracked class.</typeparam>
    public interface IUnmanagedData<TData> where TData : unmanaged
    {
        /// <summary>
        /// Initializes the specified data structure for the associated instance.
        /// </summary>
        /// <remarks>
        /// This method will be called when the object joins the set of tracked objects
        /// (typically in <c>OnEnable</c>).
        /// </remarks>
        public void Initialize(out TData data)
            => data = default;

        /// <summary>
        /// This method should release or reset any allocated resources.
        /// For example, this is a good place to call <see cref="System.IDisposable.Dispose"/>
        /// on any allocated structures stored in the <typeparamref name="TData"/> struct.
        /// </summary>
        /// <remarks>
        /// This method will be called when the object leaves the set of tracked objects
        /// (typically in <c>OnDisable</c>).
        /// </remarks>
        public void Cleanup(ref TData data) { }
    }
}
