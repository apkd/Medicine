#if MEDICINE_EXTENSIONS_LIB
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine
{
    public static partial class MedicineExtensions
    {
        /// <summary> Returns the provided list or an empty list if the input is null. </summary>
        /// <returns>A non-null list. If the input is null, an empty list will be returned.</returns>
        [MethodImpl(AggressiveInlining)]
        public static List<T> OrEmpty<T>(this List<T>? list)
            => list ?? PooledList.Empty<T>();

        /// <summary> Returns the provided array or an empty array if the input is null. </summary>
        /// <returns>A non-null array. If the input is null, an empty array will be returned.</returns>
        [MethodImpl(AggressiveInlining)]
        public static T[] OrEmpty<T>(this T[]? list)
            => list ?? Array.Empty<T>();
    }
}
#endif