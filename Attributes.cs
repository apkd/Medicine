using System;
using JetBrains.Annotations;
using UnityEngine;
using static System.AttributeTargets;
using static JetBrains.Annotations.ImplicitUseTargetFlags;

// ReSharper disable MemberHidesStaticFromOuterClass
namespace Medicine
{
    /// <summary>
    /// Interface used to virtually access the generated [Inject] attribute initialization methods on any MonoBehaviour.
    /// See: <see cref="RuntimeHelpers.Reinject"/>.
    /// </summary>
    public interface IMedicineInjection
    {
        /// <summary> Calls the generated initialization method for given component type. </summary>
        void Inject();
    }

    /// <summary>
    /// Automatically initializes the property in Awake() with a component (or an array of components)
    /// from the current GameObject using <see cref="GameObject.GetComponent{T}"/>.
    /// </summary>
    /// <remarks> Available options: Optional. </remarks>
    [MeansImplicitUse]
    [UsedImplicitly(WithMembers)]
    [AttributeUsage(Property)]
    public sealed class Inject : Attribute
    {
        /// <summary> Set this to true to allow the component to be missing. </summary>
        /// <remarks> By default, [Inject] will log an error if the component is missing (or if the array of components is empty). </remarks>
        public bool Optional { get; set; } = false;

        /// <summary>
        /// Automatically calls <see cref="GameObject.GetComponent{T}()"/> every time the property is accessed.
        /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
        /// </summary>
        /// <remarks> A non-allocating variant of the method is used to limit GC allocation. </remarks>
        [MeansImplicitUse]
        [UsedImplicitly(WithMembers)]
        [AttributeUsage(Property)]
        public sealed class Lazy : Attribute { }

        /// <summary>
        /// Automatically initializes the property in Awake() with a component (or an array of components)
        /// from the current GameObject and/or child GameObjects using <see cref="GameObject.GetComponentInChildren{T}()"/>.
        /// </summary>
        /// <remarks> Available options: Optional, IncludeInactive. </remarks>
        [MeansImplicitUse]
        [UsedImplicitly(WithMembers)]
        [AttributeUsage(Property)]
        public sealed class FromChildren : Attribute
        {
            /// <summary> Set this to true to allow the component to be missing. </summary>
            /// <remarks> By default, [Inject.FromChildren] will log an error if the component is missing (or if the array of components is empty). </remarks>
            public bool Optional { get; set; } = false;

            /// <summary> Set this to true to include components on inactive child GameObjects. Components on the root GameObject are always included. </summary>
            /// <remarks> Keep in mind that this has nothing to do with whether the component is enabled - disabled components are included if the GameObject is active. </remarks>
            public bool IncludeInactive { get; set; } = false;

            /// <summary>
            /// Automatically calls <see cref="GameObject.GetComponentInChildren{T}"/> every time the property is accessed.
            /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
            /// </summary>
            /// <remarks> A non-allocating variant of the method is used to limit GC allocation. </remarks>
            [MeansImplicitUse]
            [UsedImplicitly(WithMembers)]
            [AttributeUsage(Property)]
            public sealed class Lazy : Attribute
            {
                /// <summary> Set this to true to include components on inactive child GameObjects. Components on the root GameObject are always included. </summary>
                /// <remarks> Keep in mind that this has nothing to do with whether the component is enabled - disabled components are included if the GameObject is active. </remarks>
                public bool IncludeInactive { get; set; } = false;
            }
        }

        /// <summary>
        /// Automatically initializes the property in Awake() with a component (or an array of components)
        /// from the current GameObject using <see cref="GameObject.GetComponentInParent{T}()"/>.
        /// </summary>
        /// <remarks> Available options: Optional, IncludeInactive. </remarks>
        [MeansImplicitUse]
        [UsedImplicitly(WithMembers)]
        [AttributeUsage(Property)]
        public sealed class FromParents : Attribute
        {
            /// <summary> Set this to true to allow the component to be missing. </summary>
            /// <remarks> By default, [Inject.FromParents] will log an error if the component is missing (or if the array of components is empty). </remarks>
            public bool Optional { get; set; } = false;

            /// <summary> Set this to true to include components on inactive parent GameObjects. Components on the root GameObject are always included. </summary>
            /// <remarks>
            /// Note that this option is only included for API symmetry as it makes very little difference when we're injecting from parents
            /// (because injection is executed in Awake when parent GameObjects are pretty much guaranteed to be active).
            /// Keep in mind that this has nothing to do with whether the component is enabled - disabled components are included if the GameObject is active.
            /// </remarks>
#if UNITY_2020_1_OR_NEWER // this wasn't actually supported in GetComponentsInParent before 2020.1
            public bool IncludeInactive { get; set; } = false;
#endif

            /// <summary>
            /// Automatically calls <see cref="GameObject.GetComponentInParent{T}"/> every time the property is accessed.
            /// WARNING: Make sure you use the result array directly and not store any references to it, as they may become invalid on next call.
            /// </summary>
            /// <remarks> A non-allocating variant of the method is used to limit GC allocation. </remarks>
            [MeansImplicitUse]
            [UsedImplicitly(WithMembers)]
            [AttributeUsage(Property)]
            public sealed class Lazy : Attribute
            {
                /// <summary> Set this to true to include components on inactive parent GameObjects. Components on the root GameObject are always included. </summary>
                /// <remarks> Keep in mind that this has nothing to do with whether the component is enabled - disabled components are included if the GameObject is active. </remarks>
                public bool IncludeInactive { get; set; } = false;
            }
        }

        /// <summary>
        /// Makes the property return the currently registered singleton instance.
        /// </summary>
        /// <remarks>
        /// In order for the object to register itself as a singleton, the type needs to be marked with [Register.Single].
        /// Can be used on static properties.
        /// </remarks>
        [MeansImplicitUse]
        [UsedImplicitly(WithMembers)]
        [AttributeUsage(Property)]
        public sealed class Single : Attribute { }

        /// <summary>
        /// Makes the property return the set of currently active objects of this type.
        /// </summary>
        /// <remarks>
        /// In order for the object to register itself in the active objects collection, the type needs to be marked with [Register.All].
        /// Can be used on static properties.
        /// </remarks>
        [MeansImplicitUse]
        [UsedImplicitly(WithMembers)]
        [AttributeUsage(Property)]
        public sealed class All : Attribute { }
    }

    /// <summary>
    /// This attribute class is a container for [Register.Single] and [Register.All] and should not be used directly.
    /// </summary>
    [UsedImplicitly]
    [AttributeUsage(Module)] // could have used a static class here, but that breaks IntelliSense when starting to type [Register.*]
    public sealed class Register : Attribute
    {
        /// <summary>
        /// Registers the type as a singleton that can be injected using [Inject.Single].
        /// </summary>
        /// <remarks>
        /// This property will log an error if there is no registered instance of this singleton, or if multiple objects
        /// try to register themselves as the active instance. You can change the registered instance at runtime.
        /// </remarks>
        [MeansImplicitUse]
        [UsedImplicitly(WithMembers)]
        [AttributeUsage(Class)]
        public sealed class Single : Attribute
        {
            // Supported types:
            // ---------------
            // * MonoBehaviour:
            //     The object will register/unregister itself as singleton in OnEnable/OnDisable.
            // * ScriptableObject:
            //     Adds a GUI to the object header that allows you to select the active ScriptableObject singleton instance.
            //     This instance is automatically added to preloaded assets (as the only instance of the type).
            // * Interface:
            //     Allows you to use [Inject.Single] with this interface type. Make sure the types you're injecting are also
            //     registered using [Register.Single].
        }

        /// <summary>
        /// Tracks all active instances of the type so that they can be injected using [Inject.All].
        /// </summary>
        /// <remarks>
        /// This creates a static collection of active instances of this type. The objects will automatically register/unregister
        /// themselves in OnEnable/OnDisable. This means you can assume are instances returned by the property are non-null.
        /// </remarks>
        [MeansImplicitUse]
        [UsedImplicitly(WithMembers)]
        [AttributeUsage(Class)]
        public sealed class All : Attribute
        {
            // Supported types:
            // ---------------
            // * MonoBehaviour:
            //     The object will automatically register/unregister itself in OnEnable/OnDisable.
            // * ScriptableObject:
            //     The object will automatically register/unregister itself in OnEnable/OnDisable.
            //     In practice, this returns all of the loaded instances of the ScriptableObject in your project.
            //     You might want to add them to preloaded assets (in Player Settings) to ensure they're available in build.
            // * Interface:
            //     Allows you to use [Inject.All] with this interface type. Make sure that the actual MonoBehaviour/ScriptableObject
            //     types you're injecting are also registered using [Register.All].
        }
    }
}
