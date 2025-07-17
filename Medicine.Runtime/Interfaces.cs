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