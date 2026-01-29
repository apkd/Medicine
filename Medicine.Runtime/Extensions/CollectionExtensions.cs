#if MEDICINE_EXTENSIONS_LIB
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static System.Runtime.CompilerServices.MethodImplOptions;
using static Medicine.Internal.Utility;

namespace Medicine
{
    public static partial class MedicineExtensions
    {
        [MethodImpl(AggressiveInlining)]
        public static List<T> OrEmpty<T>(this List<T>? list)
            => list ?? PooledList.Empty<T>();

        [MethodImpl(AggressiveInlining)]
        public static T[] OrEmpty<T>(this T[]? list)
            => list ?? Array.Empty<T>();
    }
}
#endif