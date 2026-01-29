#if MEDICINE_EXTENSIONS_LIB
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Medicine.Internal;

namespace Medicine
{
    /// <summary>
    /// Implements an immutable, shared, singleton instance of a <see cref="List{T}"/> that is always empty.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <remarks>
    /// This list should never be modified. All operations that attempt to add or remove items will throw exceptions.
    /// <b>Casting this to <see cref="List{T}"/> circumvents these protections and will allow you to add items
    /// to the list; make sure you never do this!</b>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [SuppressMessage("ReSharper", "ValueParameterNotUsed")]
    public sealed class EmptyList<T> : List<T>, IList<T>, IReadOnlyList<T>, IList
    {
        static readonly EmptyList<T> instance = new();

        EmptyList() { }

        /// <summary> Returns the shared, immutable instance of the list. </summary>
        public static EmptyList<T> Instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var list = instance;
#if DEBUG
                if (list.Capacity > 0)
                    list.LogErrorAndForceZeroCapacity();
#endif
                return list;
            }
        }

#if DEBUG
        void LogErrorAndForceZeroCapacity()
        {
            UnityEngine.Debug.LogError($"Something mutated the EmptyList{typeof(T).Name} instance!");
            var listView = this.AsInternalsView();
            listView.Version++;
            listView.Count = 0;
            listView.Array = Array.Empty<T>();
        }
#endif

        // read-only APIs - for these, it's OK to return values indicating the list is empty

        public new bool Contains(T item) => false;
        public new void CopyTo(T[] array, int arrayIndex) { }
        public new int IndexOf(T item) => -1;
        public override string ToString() => $"EmptyList<{typeof(T).Name}>";
        int IList.IndexOf(object value) => -1;
        bool IList.Contains(object? value) => false;
        bool IList.IsFixedSize => true;
        bool IList.IsReadOnly => true;
        void ICollection.CopyTo(Array array, int index) { }
        int ICollection.Count => 0;
        int ICollection<T>.Count => 0;
        int IReadOnlyCollection<T>.Count => 0;
        bool ICollection<T>.IsReadOnly => true;

        // mutating APIs - using any of these usually indicates a mistake, so we (try to) throw

        [Obsolete(OpDisallowed, true)] public new void Insert(int index, T item) => NotSupported();
        [Obsolete(OpDisallowed, true)] public new void RemoveAt(int index) => NotSupported();
        [Obsolete(OpDisallowed, true)] public new bool Remove(T item) => (bool)NotSupported();
        [Obsolete(OpDisallowed, true)] public new T this[int index] { get => (T)OutOfRange(); set => OutOfRange(); }
        [Obsolete(OpDisallowed, true)] public new void Clear() => NotSupported();
        object IList.this[int index] { get => OutOfRange(); set => OutOfRange(); }
        T IReadOnlyList<T>.this[int index] => (T)OutOfRange();
        void ICollection<T>.Add(T item) => NotSupported();
        int IList.Add(object? value) => (int)NotSupported();
        void IList.Clear() => NotSupported();
        void IList.Insert(int index, object value) => NotSupported();
        void IList.Remove(object value) => NotSupported();
        void IList.RemoveAt(int index) => NotSupported();

        const string OpDisallowed = "EmptyList<T> is read-only. You should never attempt to modify it.";

        [MethodImpl(MethodImplOptions.NoInlining)]
        static object NotSupported() => throw new NotSupportedException(OpDisallowed);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static object OutOfRange() => throw new IndexOutOfRangeException(OpDisallowed);
    }
}
#endif