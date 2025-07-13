# Medicine ***v3***

### ***Medicine*** is a collection of tools for working with components in Unity.

- ***Gain prototyping superpowers.*** Easily find, cache and validate components your script depends on.
- ***Skip the boilerplate.*** Implement singletons and track active components with one line of code.
- ***Have your cake, and eat it too.*** Emits highly optimized code, with no performance penalty for features you aren't using.

Features
--------

##### Cache and validate component references
- Write a simple assignment, and Medicine will automatically generate backing fields and null checks for your components.
- Simply tag a method with `[Inject]` and make the class `partial`:

```cs
partial class MyComponent : MonoBehaviour
{
    // nothing here!

    [Inject]
    void Awake()
    {
        // simply write some assignments - backing fields and
        // missing component checks will be generated for you
        Colliders = GetComponentsInChildren<Collider>();
        Rigidbody = GetComponent<Rigidbody>().Optional();
        MainCamera = Camera.main;
    }
}
```

##### Easily write and access singletons

- Simply tag a component with the `[Singleton]` attribute
- Access your singleton from anywhere with `T.Instance` or `Find.Singleton<T>()`
- Also works for `ScriptableObject`

```cs
[Singleton]
class GameController : MonoBehaviour { }
```
```
var mySingleton = GameController.Instance;
```
##### Easily track and iterate over all instances of a component
- Put `[Track]` on your component and you're done!
- Access the active instances using `T.Instances` or `Find.Instances<T>()`
- Fast and efficient under the hood
- Also works for `ScriptableObject`

```cs
[Track]
class Enemy : MonoBehaviour { } 
```
```
foreach (var instance in Enemy.Instances)
{
    // iterate over enabled instances
}
```

##### Optimized utilities for finding objects
- Non-alloc equivalents of the `GetComponents...<T>` family of functions.
- Non-alloc enumerator for finding all components of type in a scene.
- Slightly faster variants of common functions, such as `FindObjectsByType<T>`.

```csharp
foreach (var collider in this.EnumerateComponentsInChildren<Collider>())
{
    // (do something with the child colliders)
}

foreach (var rigidbody in gameObject.scene.EnumerateComponentsInScene<Rigidbody>())
{
    // (do something with each rigidbody in the scene)
}

// this version of FindObjectsByType omits an array allocation/copy,
// but it still alocates an array and is pretty slow.
// (you should use [Track] instead whenever possible)
GameObject[] allObjects = Find.ObjectsByType<GameObject>();
```

## How to install
Compatibility: ***Unity 2019.3 or newer***

Open "Add package from git URL" in the Unity Package Manager and paste the repository URL:
##### `https://github.com/apkd/Medicine.git`

Optional dependencies:
- [ZLinq](https://github.com/Cysharp/ZLinq)
    - For querying components with efficient, GC-free LINQ-like queries - highly recommended!
    - Medicine data structures implement `AsValueEnumerable()`, allowing you to easily query components with ZLinq.
- [PolySharp](https://github.com/Sergio0694/PolySharp)
    - Enables the use of various modern C# features in Unity projects.
- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)
    - Easiest way to install the above two packages.
