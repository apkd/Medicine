
namespace Medicine
{
    /// <summary>
    /// Provides a <see cref="TrackAttribute"/>-marked class with an InstanceIndex property
    /// which is automatically updated to represent the index to the object's data in the instance list,
    /// <see cref="IUnmanagedData{TData}"/> arrays, <see cref="UnityEngine.Jobs.TransformAccessArray"/>, etc.
    /// </summary>
    public interface IInstanceIndex
    {
        /// <summary>
        /// The index of this instance. This property is automatically updated.
        /// </summary>
        int InstanceIndex { get; set; }
    }

    /// <summary>
    /// Provides a <see cref="TrackAttribute"/>-marked class with an array of unmanaged structs,
    /// where each element is data related to a single instance of the class.
    /// </summary>
    /// <typeparam name="TData"></typeparam>
    public interface IUnmanagedData<TData> where TData : unmanaged
    {
        public void Initialize(out TData data)
            => data = default;
    }
}