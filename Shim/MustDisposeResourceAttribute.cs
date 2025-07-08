#if OLDER_THAN_2022_3_45f1
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