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
        public static bool IsNotNull(this UnityEngine.Object? obj)
            => IsNativeObjectAlive(obj);

        [MethodImpl(AggressiveInlining)]
        public static bool IsNull(this UnityEngine.Object? obj)
            => IsNativeObjectDead(obj);
    }
}
#endif