#if UNITY_OLDER_THAN_2022_3_45f1 || (UNITY_2023_1_OR_NEWER && UNITY_OLDER_THAN_6000_0_17f)
using System;
using static System.AttributeTargets;

namespace JetBrains.Annotations
{
    [AttributeUsage(Class | Struct | Constructor | Method | Parameter)]
    sealed class MustDisposeResourceAttribute : Attribute
    {
        public bool Value { get; }

        public MustDisposeResourceAttribute()
            => Value = true;

        public MustDisposeResourceAttribute(bool value)
            => Value = value;
    }
}
#endif