#nullable enable
#define MEDICINE_V3_MIGRATION
using System;
using System.ComponentModel;
using JetBrains.Annotations;
using static System.AttributeTargets;
using static System.ComponentModel.EditorBrowsableState;
using static JetBrains.Annotations.ImplicitUseTargetFlags;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Medicine.Tests")]

// ReSharper disable MemberHidesStaticFromOuterClass
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable GrammarMistakeInComment

namespace Medicine
{
    /// <summary>
    /// Configures settings for the Medicine framework at the assembly level.
    /// </summary>
    [AttributeUsage(Assembly)]
    public sealed class MedicineSettingsAttribute : Attribute
    {
        /// <param name="makePublic">
        /// Configures whether properties generated by the <see cref="InjectAttribute"/> are public by default.
        /// </param>
        /// <param name="debug">
        /// Certain debug features in the generated code, such as null checks, are stripped from release builds.
        /// By default, they are only available in debug builds and in the editor.
        /// This parameter lets you override this and force debugging to be always on or off.
        /// </param>
        /// <param name="alwaysTrackInstanceIndices">
        /// If set to <c>true</c>, the source generator will automatically implement the <see cref="IInstanceIndex"/>
        /// interface on all classes tracked by <see cref="TrackAttribute"/>. This saves you from having to
        /// add the interfaces yourself if you want to put them on all tracked types anyway.
        /// </param>
        public MedicineSettingsAttribute(
            bool makePublic = true,
            bool alwaysTrackInstanceIndices = false,
            MedicineDebugMode debug = MedicineDebugMode.Automatic
        ) { }
    }

    [EditorBrowsable(Never)]
    public enum MedicineDebugMode
    {
        Automatic = 0,
        ForceEnabled = 1,
        ForceDisabled = 2,
    }

    /// <summary>
    /// The `[Inject]` attribute can be placed on any method to generate
    /// properties for any assignments in that method.
    /// </summary>
    /// <remarks>
    /// To use this feature, simply mark a method with <c>[Inject]</c> and write some
    /// assignments like in the example below.
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

    /// <summary>
    /// Can be used to ensure that a mutable struct is not stored in a `readonly` field.
    /// This would prevent the struct's value from being updated.
    /// </summary>
    [AttributeUsage(Struct)]
    public sealed class DisallowReadonlyAttribute : Attribute { }

    /// <summary>
    /// Tags the assembly for generation of a Constants class, containing various
    /// project constants extracted from the TagManager.asset file.
    /// </summary>
    [AttributeUsage(Assembly)]
    public sealed class GenerateUnityConstantsAttribute : Attribute { }

#if MODULE_ZLINQ
    [AttributeUsage(Method | Property)]
    public sealed class WrapValueEnumerableAttribute : Attribute { }
#endif

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