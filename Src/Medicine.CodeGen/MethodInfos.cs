using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEngine;
using static System.Reflection.BindingFlags;
using Object = UnityEngine.Object;

namespace Medicine
{
    [SuppressMessage("ReSharper", "MemberHidesStaticFromOuterClass")]
    static class MethodInfos
    {
        internal static readonly MethodInfo LogException
            = typeof(Debug).GetMethod(nameof(Debug.LogException), new[] { typeof(Exception), typeof(Object) });

        internal static readonly MethodInfo LogError
            = typeof(Debug).GetMethod(nameof(Debug.LogError), new[] { typeof(string), typeof(Object) });

        internal static readonly MethodInfo GetTypeFromHandle
            = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), Public | Static);

        internal static readonly MethodInfo GetGameObject
            = typeof(Component).GetProperty("gameObject", Public | Instance).GetMethod;

        internal static readonly ConstructorInfo DefaultExecutionOrderConstructor
            = typeof(DefaultExecutionOrder).GetConstructor(new[] { typeof(int) });

        internal static readonly MethodInfo UnityObjectBoolOpImplicit
            = typeof(Object).GetMethod("op_Implicit", Public | Static);

        internal static readonly MethodInfo IMedicineInjectionInject
            = typeof(IMedicineComponent).GetMethod(nameof(IMedicineComponent.Inject));

        internal static class RuntimeHelpers
        {
            internal static readonly MethodInfo ValidateArray
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.ValidateArray), Public | Static);

            internal static readonly MethodInfo GetMainCamera
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.GetMainCamera), Public | Static);

            internal static readonly MethodInfo Inject
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.Inject), Public | Static);

            internal static readonly MethodInfo InjectArray
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectArray), Public | Static);

            internal static readonly MethodInfo InjectFromChildren
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectFromChildren), Public | Static);

            internal static readonly MethodInfo InjectFromChildrenIncludeInactive
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectFromChildrenIncludeInactive), Public | Static);

            internal static readonly MethodInfo InjectFromChildrenArray
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectFromChildrenArray), Public | Static);

            internal static readonly MethodInfo InjectFromChildrenArrayIncludeInactive
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectFromChildrenArrayIncludeInactive), Public | Static);

            internal static readonly MethodInfo InjectFromParents
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectFromParents), Public | Static);

            internal static readonly MethodInfo InjectFromParentsIncludingInactive
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectFromParentsIncludingInactive), Public | Static);

            internal static readonly MethodInfo InjectFromParentsArray
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectFromParentsArray), Public | Static);

            internal static readonly MethodInfo InjectFromParentsArrayIncludeInactive
                = typeof(Medicine.RuntimeHelpers).GetMethod(nameof(Medicine.RuntimeHelpers.InjectFromParentsArrayIncludeInactive), Public | Static);

            internal static class Collection
            {
                internal static readonly MethodInfo GetInstance
                    = typeof(Medicine.RuntimeHelpers.Collection<>).GetMethod(nameof(Medicine.RuntimeHelpers.Collection<Object>.GetInstances), Public | Static);

                internal static readonly MethodInfo RegisterInstance
                    = typeof(Medicine.RuntimeHelpers.Collection<>).GetMethod(nameof(Medicine.RuntimeHelpers.Collection<Object>.RegisterInstance), Public | Static);

                internal static readonly MethodInfo UnregisterInstance
                    = typeof(Medicine.RuntimeHelpers.Collection<>).GetMethod(nameof(Medicine.RuntimeHelpers.Collection<Object>.UnregisterInstance), Public | Static);
            }

            internal static class Lazy
            {
                internal static readonly MethodInfo InjectArray
                    = typeof(Medicine.RuntimeHelpers.Lazy).GetMethod(nameof(Medicine.RuntimeHelpers.Lazy.InjectArray), Public | Static);

                internal static readonly MethodInfo InjectFromChildrenArray
                    = typeof(Medicine.RuntimeHelpers.Lazy).GetMethod(nameof(Medicine.RuntimeHelpers.Lazy.InjectFromChildrenArray), Public | Static);

                internal static readonly MethodInfo InjectFromChildrenArrayIncludeInactive
                    = typeof(Medicine.RuntimeHelpers.Lazy).GetMethod(nameof(Medicine.RuntimeHelpers.Lazy.InjectFromChildrenArrayIncludeInactive), Public | Static);
            
                internal static readonly MethodInfo InjectFromParentsArray
                    = typeof(Medicine.RuntimeHelpers.Lazy).GetMethod(nameof(Medicine.RuntimeHelpers.Lazy.InjectFromParentsArray), Public | Static);
            
                internal static readonly MethodInfo InjectFromParentsArrayIncludeInactive
                    = typeof(Medicine.RuntimeHelpers.Lazy).GetMethod(nameof(Medicine.RuntimeHelpers.Lazy.InjectFromParentsArrayIncludeInactive), Public | Static);
            }

            internal static class Singleton
            {
                internal static readonly MethodInfo GetInstance
                    = typeof(Medicine.RuntimeHelpers.Singleton<>).GetMethod(nameof(Medicine.RuntimeHelpers.Singleton<Object>.GetInstance), Public | Static);

                internal static readonly MethodInfo RegisterInstance
                    = typeof(Medicine.RuntimeHelpers.Singleton<>).GetMethod(nameof(Medicine.RuntimeHelpers.Singleton<Object>.RegisterInstance), Public | Static);

                internal static readonly MethodInfo UnregisterInstance
                    = typeof(Medicine.RuntimeHelpers.Singleton<>).GetMethod(nameof(Medicine.RuntimeHelpers.Singleton<Object>.UnregisterInstance), Public | Static);
            }
        }
    }
}
