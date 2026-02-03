#if MEDICINE_EXTENSIONS_LIB
#nullable enable
using System.Runtime.CompilerServices;
using static System.Runtime.CompilerServices.MethodImplOptions;
using static Medicine.Internal.Utility;

namespace Medicine
{
    public static partial class MedicineExtensions
    {
        /// <summary>
        /// Returns true when the Unity object is not null and not destroyed.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static bool IsValid(this UnityEngine.Object? obj)
            => IsNativeObjectAlive(obj);

        /// <summary>
        /// Returns true when the Unity object is null or a reference to a destroyed object.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static bool IsInvalid(this UnityEngine.Object? obj)
            => IsNativeObjectDead(obj);
    }
}
#endif