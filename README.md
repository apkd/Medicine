<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://github.com/user-attachments/assets/be12b8f7-3c44-4484-9ead-338b6d71cee7">
  <source media="(prefers-color-scheme: light)" srcset="https://github.com/user-attachments/assets/b04fd3d0-a666-4af8-b202-38b7951525eb">
  <img alt="medicine logo" src="https://github.com/user-attachments/assets/b04fd3d0-a666-4af8-b202-38b7951525eb">
</picture>

[![Unity package badge](https://img.shields.io/badge/Unity%20Package-2C3439?style=flat&logo=unity&logoColor=white)](https://github.com/apkd/Medicine/releases/tag/latest)
![GitHub License](https://img.shields.io/github/license/apkd/medicine?style=flat&label=License&labelColor=2C3439)
[![Test status badge](https://github.com/apkd/Medicine/actions/workflows/test.yml/badge.svg?branch=master&event=push)](https://github.com/apkd/Medicine/actions/workflows/test.yml)
![GitHub commit activity](https://img.shields.io/github/commit-activity/y/apkd/Medicine?authorFilter=apkd&label=Commits&labelColor=2C3439)
![GitHub last commit](https://img.shields.io/github/last-commit/apkd/Medicine?labelColor=2C3439)


Medicine is a package that uses Roslyn source generators and efficient runtime helpers to remove common Unity component boilerplate.

- Cache/validate component references by writing assignments inside an `[Inject]` method.
- Implement singletons in a single line with `[Singleton]`.
- Track active instances with `[Track]` and access them simply with `T.Instances`.
- Easily write burstable Job System code by attaching data and `TransformAccessArray`s to components.
- Allocation-free component enumeration utilities, convenient pooled lists, etc.
- IDE analyzers and quick fixes for identifying mistakes and easily applying fixes and optimizations.
- Emits highly optimized code, but with no performance penalty for features you aren't using.

<picture>
 <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=apkd/medicine&type=date&theme=dark" />
 <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/svg?repos=apkd/medicine&type=date" />
 <img alt="Star History Chart" src="https://api.star-history.com/svg?repos=apkd/medicine&type=date" width="512" />
</picture>

> ##### Table of contents:
> - [**Installation**](#installation)
> - [**Quick start**](#quick-start)
>     - [`[Inject]`: generate cached properties](#1-inject-generate-cached-properties--debug-checks)
>     - [`[Singleton]` one active instance](#2-singleton-locate-the-single-active-instance)
>     - [`[Track]`: list of active instances](#3-track-locate-all-active-instances)
> - [**Advanced tracking features**](#advanced-tracking-features)
>     - [`IUnmanagedData<TData>`: per-instance unmanaged arrays](#iunmanageddatatdata-per-instance-unmanaged-arrays)
>     - [`TransformAccessArray` tracking](#transformaccessarray-tracking)
>     - [`IInstanceIndex`: data indexing + O(1) unregister](#iinstanceindex-data-indexing--o1-unregister)
>     - [`IFindByID<TId>`: map instances by ID](#ifindbyidtid-map-instances-by-id)
> - [**Other utilities**](#other-utilities)
>     - [Allocation-free component enumeration](#allocation-free-component-enumeration)
>     - [Convenience APIs for finding objects by type](#apis-for-finding-objects-by-type)
>     - [Pooled lists](#pooled-lists)
>     - [Lazy init structs](#lazy-init)
>     - [Extra attributes](#some-extra-attributes)
>     - [Unity constants generation](#unity-constants-generation)
> - [**Configuration**](#configuration)

Installation
============
Compatibility: ***Unity 2022.3.12f1 or newer***

### UPM
- Window ⟶ Package Manager ⟶ `+` ⟶ *Install package from git URL*
- Paste this URL (the `release` branch points at the latest commit that passed all tests)
```md
https://github.com/apkd/Medicine.git#release
```

### UPM via `manifest.json`
Add to your project's `Packages/manifest.json`:
```json
"dependencies": {
  "pl.apkd.medicine": "https://github.com/apkd/Medicine.git#release"
```

Quick start
===========
## 1) `[Inject]`: generate cached properties + debug checks
Mark a method with `[Inject]`, make the type `partial`, and write assignments:

```csharp
[Inject] // convenient to place on Awake, but can be used on any other method
void Awake()
{
    // simply write some assignments - backing fields and
    // missing component checks will be generated for you
    Colliders = GetComponentsInChildren<Collider>();
    Rigidbody = GetComponent<Rigidbody>().Optional(); // .Optional() suppresses the null check
    MainCamera = Camera.main;
}
```

Medicine generates the backing properties based on your assignments, with (debug-only) null checks and nice error messages.

<img alt="[Inject] attribute usage example gif" src="https://github.com/user-attachments/assets/c2013881-2f19-45e6-bec0-cafbef35cd5c" width="512" />

Notes:
- In release builds, the safety checks in the generated code are stripped away for optimal performance.
- `.Optional()` only has meaning inside an `[Inject]` assignment (it suppresses the generated null check).

## 2) `[Singleton]`: locate the single active instance

A super convenient singleton pattern for MonoBehaviours and ScriptableObjects, implemented by simply tagging the class with `[Singleton]`.

How it works:
- The generator adds internal registration in `OnEnable`/`OnDisable`.[^0]
- In the editor (edit mode), `Find.Singleton<T>()` can fall back to a cached `FindObjectsByType` scan for tooling friendliness.
- Works with interfaces (tag both the interface and the implementing classes with `[Singleton]` and obtain the instance via `Find.Singleton<YourInterfaceType>()`).

<details>
  <summary><em>Usage example</em></summary>

```csharp
[Singleton]
partial class GameController : MonoBehaviour { }

// access the current singleton instance anywhere:
var instance = GameController.Instance;          // generated easy accessor
var instance = Find.Singleton<GameController>(); // alternative helper useful for interfaces, generics, etc
                                                 // (both work exactly the same)
```
</details>

## 3) `[Track]`: locate all active instances

A powerful and easy way to maintain a list of active/enabled instances. Works for MonoBehaviours and ScriptableObjects. Simply tag the class with `[Track]`.

- The generator adds internal registration in `OnEnable`/`OnDisable`.[^0]
- Access the active instances using `T.Instances` or `Find.Instances<T>()`.
- Fast and efficient under the hood.
- In edit mode, falls back to slow `FindObjectsOfType` (for editor tooling compatibility).
- Works with interfaces (tag both the interface and the implementing classes with `[Track]` and find the instances via `Find.Instances<YourInterfaceType>()`).

<details>
  <summary><em>Usage example</em></summary>

```csharp
[Track]
partial class Enemy : MonoBehaviour { }

foreach (var enemy in Enemy.Instances) // generated easy accessor
{
    // iterate over active (enabled) instances
}

foreach (var enemy in Find.Instances<Enemy>()) // alternative helper useful for interfaces, generics, etc
                                               // (both work exactly the same)
{
    // iterate over active (enabled) instances
}
```

Important: don't enable/disable tracked objects while enumerating `Enemy.Instances`.
If you must mutate during iteration, use a snapshot:

```csharp
foreach (var enemy in Enemy.Instances.WithCopy)
{
    // safe to enable/disable while iterating
}
```
</details>

Advanced tracking features
==========================
## `IUnmanagedData<TData>`: per-instance unmanaged arrays
If a tracked type implements `IUnmanagedData<TData>`, Medicine maintains a `NativeList<TData>` aligned with instance order.

This is useful for scheduling jobs where you want a packed unmanaged array that mirrors your tracked instances. (The tracked instance and its "attached" data are stored at the same index.) You can add as many data arrays as you want.

<details>
  <summary><em>Usage example</em></summary>

```csharp
// declare your data structure:
struct EnemyData
{
    public float Health;
    public bool IsAlive;
}

// implement IUnmanagedData:
[Track]
sealed partial class Enemy : MonoBehaviour, IUnmanagedData<EnemyData>
{
    [SerializeField] float initialHealth;

    // optional initialization callback
    // important: this method must never throw! guard for exceptions carefully if necessary
    // (executes in OnEnable, along with tracked object registration)
    void IUnmanagedData<EnemyData>.Initialize(out EnemyData data)
        => data = new() { Health = initialHealth, IsAlive = true };
}

// now you can write a job that targets the unmanaged data of each tracked instance:
struct EnemyDeathJob : IJobParallelFor
{
    public NativeArray<EnemyData> Data;

    void IJobParallelFor.Execute(int index)
    {
        var data = Data[index];
        data.IsAlive = data.Health > 0;
        Data[index] = data;
    }
}

// get the data array and schedule your job:
static JobHandle ScheduleEnemyDeathJob()
{
    var enemyData = Enemy.Unmanaged.DataArray; // convenient static accessor for the NativeArray<EnemyData>
                                               // (name generated based on struct name)
    var job = new EnemyDeathJob { DataArray = enemyData };
    return job.Schedule(Enemy.Unmanaged.DataArray.Length, 16);
}
```
</details>

## `TransformAccessArray` tracking
`[Track(transformAccessArray: true)]` will also keep a `UnityEngine.Jobs.TransformAccessArray` in sync. This lets you schedule jobs that access the transforms of tracked instances.


<details>
  <summary><em>Usage example</em></summary>

Here's some example code that procedurally animates tracked objects by setting their transform positions – Burst-compiled and parallelized using the Job System!

```csharp
struct TrackedObjectData
{
    public float Speed;
    public float3 InitialPosition;
}

// define the script for the objects we're animating
[Track(transformAccessArray: true)] // the important part
sealed partial class TrackedObject : MonoBehaviour, IUnmanagedData<TrackedObjectData>
{
    [SerializeField] float speed;

    // initialize our procedural animation data
    void IUnmanagedData<TrackedObjectData>.Initialize(out TrackedObjectData data)
        => data = new() { speed = speed, InitialPosition = transform.position };
}

// this is the script that schedules the job (we only need one instance in the scene)
sealed class Mover : MonoBehaviour
{
    JobHandle jobHandle;

    void Update()
    {
        // complete the job from the previous frame (always already completed in practice)
        jobHandle.Complete();

        // grab our data array and the transform access array via static accessors
        var instanceData = TrackedObject.Unmanaged.TrackedObjectDataArray;
        var transformAccessArray = TrackedObject.TransformAccessArray;
        
        // now we can schedule a job that animates the objects based on their data
        jobHandle = new MoveJob
            {
                Time = Time.time,
                InstanceData = instanceData,
            }
            .Schedule(transformAccessArray);
    }

    // simple job to procedurally wiggle the transforms
    // see: https://docs.unity3d.com/Documentation/ScriptReference/Jobs.IJobParallelForTransform.html
    [BurstCompile]
    struct MoveJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<TrackedObjectData> InstanceData;
        public float Time;
    
        void IJobParallelForTransform.Execute(int index, TransformAccess transform)
        {
            var data = InstanceData[index];
            var position = data.InitialPosition;
            position += math.sin(Time * data.Speed + index * 0.1f) * 2;
            transform.position = position;
        }
    }
}
```

The resulting animation looks like this:

> ![Result animation](https://github.com/user-attachments/assets/f7b26676-a823-4feb-ae73-a4cad1e1273d)
</details>

## `IInstanceIndex`: data indexing + O(1) unregister
If a tracked type implements `IInstanceIndex`, Medicine keeps an up-to-date `InstanceIndex` for addressing per-instance data. This effectively allows an instance to "know its own index" in the list of active instances.

> Warning: The index might change over time. As instances are enabled/disabled, they will be swapped around in the internal storage arrays. The generated `InstanceIndex` property always returns the up-to-date value.

You can use the `InstanceIndex` to index into the unmanaged data arrays, etc. Additionally, for each `IUnmanagedData` implementation, instance properties are emitted to easily access the data belonging to the current instance.

Internally, `IInstanceIndex` additionally allows for a small optimization: It makes unregistration an O(1) operation (no need to scan the list to find the instance index, since we have it stored). Slightly faster when dealing with large numbers of instances that are frequently enabled/disabled.

> There is a [`[MedicineSettings]` option](#configuration) that automatically enables `InstanceIndex` tracking for all types.

<details>
  <summary><em>Usage example</em></summary>

```csharp
struct VelocityData { public float Velocity; }

[Track]
sealed partial class MyScript : MonoBehaviour, IInstanceIndex, IUnmanagedData<VelocityData>
{
    void Update()
    {
        Debug.Log($"My tracked instance index is: {InstanceIndex}");
        Debug.Log($"My velocity is: {LocalVelocityData.Velocity}"); // LocalVelocityData is a generated accessor
    }
}
```
</details>

## `IFindByID<TId>`: map instances by ID
If a tracked type implements `IFindByID<TId>`, Medicine maintains a dictionary for lookup by ID, and generates a static `FindByID` method. The `TId` can be any type that implements `IEquatable<TId>`, and you can index by multiple IDs.

Effectively, this allows you to find instances by an ID of your choosing.

<details>
  <summary><em>Usage example</em></summary>

```csharp
[Track]
sealed partial class Item : ScriptableObject, IFindByID<int>
{
    [SerializeField] int id;
    int IFindByID<int>.ID => id; // needs to return a constant value
} 

// usage:
Item.FindByID(id: 123);
```
</details>

Other utilities
===============================
## Allocation-free component enumeration

These utilities use pooled lists internally, letting you conveniently iterate over components without allocating.

```csharp
foreach (var collider in gameObject.EnumerateComponents<Collider>())
```

Available APIs:
* `EnumerateComponents<T>()`
* `EnumerateComponentsInParents<T>(bool includeInactive)`
* `EnumerateComponentsInChildren<T>(bool includeInactive)`
* `EnumerateComponentsInScene<T>(bool includeInactive)`

## Pooled lists

This `List<T>` pool implementation comes with special optimizations for reference types.
While it is used internally for many Medicine APIs, you can also use it directly if you want.

```csharp
using var handle = PooledList.Get<GameObject>(out var list);
```

## Lazy init
Similar to `System.Lazy<T>`, but struct-based and with a nice `Lazy.From(...)` factory method.

```csharp
LazyRef<GameObject> myPrefab // for reference types
    = Lazy.From(() => Resources.Load<GameObject>("MyPrefab"));

LazyVal<int> numActive // for value types
    = Lazy.From(() => GameObject.FindObjectsOfType<GameObject>().Length);
```

## APIs for finding objects by type
These wrap Unity APIs but avoid an extra internal array copy. They still allocate and are slow for gameplay code.
```csharp
Find.ObjectsByType<GameObject>(includeInactive: true);
Find.ObjectsByTypeAll<ScriptableObject>();
```
Prefer `[Track]` whenever possible.

## Unity constants generation

Add this in an assembly to generate a handy `Medicine.Constants` class with tags/layers extracted from `TagManager.asset`.

```csharp
[assembly: Medicine.GenerateUnityConstants]
```

The benefit here - on top of being way faster than dealing with strings - is that when you change tags/layers in Unity, your code will break in places that need to be updated instead of failing silently. Of course, you also get IDE autocompletion and all that.

The generated class is `partial`, so you can extend it with your own constants.

<details>
  <summary>Emitted code preview (simplified)</summary>

```csharp
namespace Medicine;

public static partial class Constants
{
    public enum Tag : uint
    {
        @SomeTag = 20000u,
        @AnotherTag = 20001u,
        ...
    }

    public enum Layer : uint
    {
        @Default = 00,
        @TransparentFX = 01,
        @Ignore_Raycast = 02,
        ...
    }

    [System.Flags]
    public enum LayerMask : uint
    {
        None = 0,
        All = 0xffffffff,
        @Default = 1u << 00,
        @TransparentFX = 1u << 01,
        ...
    }

    public static partial class ConstantsExtensions
    {
        public static UnityEngine.TagHandle GetHandle(this Constants.Tag tag)
            => UnsafeUtility.As<Constants.Tag, UnityEngine.TagHandle>(ref tag);

        public static bool CompareTag(this GameObject gameObject, Constants.Tag tag)
            => gameObject.CompareTag(tag.GetHandle());

        public static bool CompareTag(this Component component, Constants.Tag tag)
            => component.CompareTag(tag.GetHandle());
    }
}
```
</details>

Configuration
=============
You can configure defaults at the assembly level:
```csharp
[assembly: MedicineSettings(
    makePublic: true,                   // make properties generated by [Inject] public by default
    
    alwaysTrackInstanceIndices: false,  // equivalent to always adding IInstanceIndex to tracked types
    
    debug: MedicineDebugMode.Automatic  // safety checks, logs, etc. 
                                        // (default: enabled in editor, stripped in release builds)
                                        // the default value is usually good, but you can
                                        // override it if you want to review the generated code
)]
```

Build defines
-------------
- `MEDICINE_NO_FUNSAFE`: disables the "shared pooled list across reference types" optimization in `PooledList`.
- `MEDICINE_EDITMODE_ALWAYS_REFRESH`: forces edit-mode instance/singleton refresh on every access (instead of "once per frame unless invalid").

Optional dependencies
=====================
- [ZLinq](https://github.com/Cysharp/ZLinq) (recommended): enables `AsValueEnumerable()` on Medicine enumerables for GC-free LINQ-like queries.
- [PolySharp](https://github.com/Sergio0694/PolySharp): enables modern C# features in Unity; Medicine can generate fallbacks when PolySharp isn't present.
    - Unlocks C# features such as: [`{ get; init; }`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/init), [`[CallerArgumentExpression]`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-10.0/caller-argument-expression), [`[InterpolatedStringHandler]`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/interpolated#compilation-of-interpolated-strings)
- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity): convenient way to install ZLinq/PolySharp as NuGet packages.

[^0]: In reality, Medicine uses OnEnableINTERNAL and OnDisableINTERNAL for registration. These little-known callbacks let you write your own OnEnable method without conflicts or bothering with inheritance.