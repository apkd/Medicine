#nullable enable
#define MEDICINE_V3_MIGRATION
using System;
using System.ComponentModel;
using JetBrains.Annotations;
using static System.AttributeTargets;
using static System.ComponentModel.EditorBrowsableState;
using static JetBrains.Annotations.ImplicitUseTargetFlags;

// ReSharper disable MemberHidesStaticFromOuterClass
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable GrammarMistakeInComment

namespace Medicine
{
    /// <summary>
    /// Marks a method where the Medicine injection property is declared.
    /// </summary>
    /// <remarks>
    /// Mark a method with <c>[Inject]</c> and simply write assignments like below.
    /// The source generator will automatically generate backing fields,
    /// null checks, etc. based on your code.
    /// </remarks>
    /// <example><code>
    /// [Inject]
    /// void Awake()
    /// {
    ///     Colliders = GetComponentsInChildren&lt;Collider&gt;(includeInactive: true);
    ///     Rigidbody = GetComponent&lt;Rigidbody&gt;().Optional();
    ///     Manager = Find.Singleton&lt;GameManager&gt;();
    ///     AllEnemies = Enemy.Instances;
    /// }
    /// </code></example>
    [MeansImplicitUse]
    [AttributeUsage(
        Method
#if MEDICINE_V3_MIGRATION
        | Property
#endif
    )]
    public sealed class InjectAttribute : Attribute
    {
        /// <param name="makePublic">Whether the generated properties should have public getters.</param>
        public InjectAttribute(bool makePublic = true) { }

        [Obsolete("This property exists for migration only."), EditorBrowsable(Never)]
        public bool Optional { get; set; }
    }

    [AttributeUsage(Class | Interface)]
    public sealed class SingletonAttribute : Attribute
    {
        /// <param name="manual">
        /// Setting this to <c>true</c> disables automatic tracking - you'll need to invoke <c>RegisterTracked()</c>
        /// and <c>UnregisterTracked()</c> manually.
        /// </param>
        public SingletonAttribute(bool manual = false) { }
    }

    /// <summary>
    /// Instructs the code generator to extend the class to track a list of active instances.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Use <see cref="Find.Instances{T}"/> to get access to the tracked instances. Works best with <c>foreach</c>.</item>
    /// <item>Install <see href="https://github.com/Cysharp/ZLinq">ZLinq</see> to query components with a GC-free, LINQ-like syntax.</item>
    /// <item>The source generator emits additional code that registers the class in <c>OnEnable</c>/<c>OnDisable</c>.</item>
    /// <item>(Actually, it uses the mostly-unknown <c>OnEnableINTERNAL</c>/<c>OnDisableINTERNAL</c>,
    /// which prevents conflicts with the rest of your code.)</item>
    /// <item>Working with the job system? Enable <c>trackTransforms</c> to automatically add the
    /// GameObject's transform to a <see cref="UnityEngine.Jobs.TransformAccessArray"/>.</item>
    /// </list>
    /// </remarks>
    /// <example><code>
    /// [Track]
    /// class MyComponent { }
    /// ...
    /// foreach (var instance in MyComponent.Instances)
    ///     Debug.Log(instance.name);
    /// </code></example>
    [AttributeUsage(Class | Interface)]
    public sealed class TrackAttribute : Attribute
    {
        /// <param name="transformAccessArray">
        /// Enables the automatic tracking of object transforms in a <see cref="UnityEngine.Jobs.TransformAccessArray"/>.
        /// </param>
        /// <param name="transformInitialCapacity">
        /// The starting size of the internal array used to track transform instances.
        /// </param>
        /// <param name="transformDesiredJobCount">
        /// A hint that tells the Job System how many parallel batches it should try to create when
        /// the <see cref="UnityEngine.Jobs.TransformAccessArray"/> is consumed by <see cref="UnityEngine.Jobs.IJobParallelForTransform"/>.
        /// (-1 = selected automatically)
        /// </param>
        /// <param name="manual">
        /// Setting this to <c>true</c> disables automatic tracking - you'll need to invoke <c>RegisterTracked()</c>
        /// and <c>UnregisterTracked()</c> manually.
        /// </param>
        public TrackAttribute(
            bool instanceIdArray = false,
            bool transformAccessArray = false,
            int transformInitialCapacity = 64,
            int transformDesiredJobCount = -1,
            bool manual = false
        ) { }
    }

#if MEDICINE_V3_MIGRATION
    [MeansImplicitUse, UsedImplicitly(WithMembers)]
    public static class Inject
    {
        [MeansImplicitUse, AttributeUsage(Property), EditorBrowsable(Never)]
        public sealed class FromChildren : Attribute
        {
            public bool Optional { get; set; }
            public bool IncludeInactive { get; set; }

            [MeansImplicitUse, AttributeUsage(Property), EditorBrowsable(Never)]
            public sealed class Lazy : Attribute
            {
                public bool IncludeInactive { get; set; }
            }
        }

        [MeansImplicitUse, AttributeUsage(Property), EditorBrowsable(Never)]
        public sealed class FromParents : Attribute
        {
            public bool Optional { get; set; }
            public bool IncludeInactive { get; set; }

            [MeansImplicitUse]
            [AttributeUsage(Property)]
            [EditorBrowsable(Never)]
            public sealed class Lazy : Attribute
            {
                public bool IncludeInactive { get; set; }
            }
        }

        [MeansImplicitUse, AttributeUsage(Property), EditorBrowsable(Never)]
        public sealed class Lazy : Attribute { }

        [MeansImplicitUse, AttributeUsage(Property), EditorBrowsable(Never)]
        public sealed class Single : Attribute { }

        [MeansImplicitUse, AttributeUsage(Property), EditorBrowsable(Never)]
        public sealed class All : Attribute { }
    }
#endif
}