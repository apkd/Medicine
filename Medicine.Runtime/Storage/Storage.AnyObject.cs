using System;
using System.ComponentModel;
using static System.ComponentModel.EditorBrowsableState;

namespace Medicine.Internal
{
    public static partial class Storage
    {
        [EditorBrowsable(Never)]
        public static class AnyObject<T> where T : class
        {
            public static WeakReference<T> WeakReference;
        }
    }
}