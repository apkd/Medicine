# Me*di*cine
### Code-driven component injection toolkit for [Unity](https://unity.com/).

Sick and tired of assigning references between components by hand (and losing them when something goes wrong with Unity serialization)? Having a migraine from all the `GetComponent` calls sprinkled all over your codebase?

***Medicine*** is a collection of attributes that give you a code-driven, performance-oriented way to automatically hook up references between your components. Additionally, it comes with a toolbox of optimized versions of many standard component-related operations.

> **Warning!** This library is experimental. Please [let me know](https://github.com/apkd/medicine/issues) about any issues you encounter.

## How to install
Compatibility: ***Unity 2019.3 or newer***

Open "Add package from git URL" in the Unity Package Manager and paste the repository URL:
##### `https://github.com/apkd/Medicine.git`

## Features/examples
#### Write cleaner components

```csharp
class Vehicle : MonoBehaviour
{
    // find components attached to the same GameObject
    [Inject]
    Rigidbody rigidbody { get; }

    // find components in child GameObjects (including self)
    [Inject.FromChildren]
    WheelCollider[] colliders { get; }

    // works well with interfaces and base/abstract types
    [Inject]
    IVehiclePilot pilot { get; }

    void OnEnable()
    {
        Debug.Log($"I have a rigidbody: {rigidbody}");
        Debug.Log($"And a couple of wheels: {colliders.Length}");
        Debug.Log($"And a pilot of type: {pilot.GetType()}");
    }
}
```

#### Create robust singleton objects, easily track active object instances
```csharp
class Player : MonoBehaviour
{
    // get reference to singleton objects (marked with the [Register.Single] attribute)
    [Inject.Single]
    LevelManager levelManager { get; } // getter always returns active instance
    
    // get array of all active instances of given script type (marked with the [Register.All] attribute)
    [Inject.All]
    Enemy[] enemies { get; } // getter always returns current set of active objects
    
    void OnEnable()
    {
        Debug.Log($"There's the level manager: {levelManager}");
        Debug.Log($"I'm sensing some enemies lurking nearby: {enemies.Length}");

        foreach (var enemy in enemies)
            Debug.Log($"Here's one: {enemy.name}");
    }
    
    // cleanly access all (active and enabled) scripts implementing some interface
    [Inject.All]
    IPickup[] pickups { get; }
    
    void Update()
    {
        foreach (var pickup in pickups)
            if (pickup.IsInRange(transform.position))
                pickup.Activate(this);
    }
}

// indicates that we want to to be able to inject all active instances of this script using [Inject.All].
// this array is always up-to-date
// (even if you create/destroy registered object instances at runtime!)
[Register.All]
class Enemy : MonoBehaviour { /* implementation goes here */ }

// indicates that there will only ever be one instance of this script active in the game at any given time.
// properties injected with [Inject.Single] are always up-to-date
// (even if you replace your singleton instance at runtime!)
[Register.Single]
class LevelManager : MonoBehaviour { /* implementation goes here */ }

// allows us to obtain all (custom) scripts that implement this interface.
// make sure the scripts are also registered using [Register.All].
// this also works for [Register.Single] and standard inheritance (abstract/derived classes).
[Register.All]
interface IPickup
{ 
    bool IsInRange(Vector3 position);
    void Activate(Player player);
}
```

#### More insight, less errors
```csharp
class Gun : MonoBehaviour
{
    // thanks to the fact that you're declaring your dependencies ahead of use,
    // your code automatically becomes more readable and maintainable
    [Inject.Single]
    BulletManager bulletManager { get; }

    // script will immediately log an error if the magazine is missing.
    // this indicates that the prefab is critically misconfigured and allows you to notice this early.
    // since the reference is immutable, we don't need to manually check this later in code.
    [Inject]
    Magazine magazine { get; } // immutable property! this is the recommended default because it
                               // promises that we're always referring to the same object
    
    // script will immediately log an error if there are no child Renderers.
    // this indicates to the developer that the prefab is critically misconfigured.
    [Inject.FromChildren]
    Renderer[] renderers { get; }

    // this component is optional. if we don't put at least one DamageModifier component
    // on the GameObject, the reference will be null (you should handle that!)
    [Inject(Optional = true)]
    DamageModifier modifier { get; }

    // no error will be thrown if the collection is empty because of (Optional = true)
    // (an injected array will never be null)
    [Inject.FromChildren(Optional = true)]
    GunMod[] mods { get; }
}
```

#### I still don't get it
```csharp
// before
class MyUglyUnreadableBugRiddenMessComponent : MonoBehaviour
{
    Rigidbody rigidbody;
    MeshRenderer[] meshRenderers;

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
        if (rigidbody == null)
            Debug.LogError($"No Rigidbody component attached to {name}!", this);

        meshRenderers = GetComponentsInChildren<MeshRenderer>();
        if (meshRenderers.Length == 0)
            Debug.LogError($"No child MeshRenderer components attached to {name}!", this);
    }
}
```
```csharp
// after
class MyElegantSelfDocumentingAutoDebuggingPromotionEarningGDCWorthyComponent : MonoBehaviour
{
    [Inject]
    Rigidbody rigidbody { get; }

    [Inject.FromChildren]
    MeshRenderer[] meshRenderers { get; }
}
```

#### Prototyping tricks
```csharp
class Goose : MonoBehaviour
{
    // easily make references public
    [Inject.FromChildren]
    public GooseNeck Neck { get; } // immutable!

    // simply add a setter if you're planning to pluck some
    [Inject.FromChildren]
    public GooseFeather[] Feathers { get; private set; } // not mutable from outside
}
```
```csharp
class UsabilityTricks : MonoBehaviour
{
    // easily access child transforms.
    // order of injected components is deterministic and depends on place in hierarchy.
    // (see: https://forum.unity.com/threads/getcomponentsinchildren.4582/#post-33983)
    [Inject.FromChildren]
    Transform[] childTransforms { get; }

    // components on inactive child GameObjects are omitted by default
    // (see: https://docs.unity3d.com/ScriptReference/Component.GetComponentsInChildren.html)
    [Inject.FromChildren(IncludeInactive = true)]
    Collider[] colliders { get; }

    // easily get a reference to a singleton ScriptableObject
    // (you first need to add it to preloaded assets in player settings)
    [Inject.Single]
    MyGameConfiguration gameConfig { get; }
}
```
```csharp
class PerformanceTricks : MonoBehaviour
{
    // cache transform reference to avoid extern call overhead
    [Inject]
    new Transform transform { get; }

    // easily refer to the main camera in your scene without additional work
    // (resolves Camera.main and returns a cached reference until the camera is destroyed)
    [Inject.Single]
    Camera camera { get; }
}
```
```csharp
class LazyTricks : MonoBehaviour
{
    // when you use "Lazy", the injection result is no longer stored in a local field in Awake().
    // instead, a non-alloc version of GetComponents is used to obtain a temp array of *current* components.
    // useful, but be careful to use the getter sparingly and never store the array anywhere
    // because it will become invalid after next call
    [Inject.FromChildren.Lazy]
    Renderer[] currentRenderers { get; }

    void ShowActiveRenderers() // always up to date
        => Debug.Log($"Number of currently active renderers: {currentRenderers.Length}");

    // because we aren't caching the components in Awake, the code won't break even when
    // we modify the transform hierarchy. hell, it even works in edit mode
    [Inject.FromParents.Lazy]
    Transform[] parentTransforms { get; }

    void WhereAmI() // always up to date
        => Debug.Log(string.Join(" -> ", parentTransforms.Select(x => x.name)));
}
```
```csharp
// high performance update manager in <20 lines of code
sealed class UpdateManager : MonoBehaviour
{
    [Inject.All]
    TickableBase[] tickables { get; }

    void Update()
    {
        float dt = Time.deltaTime;

        foreach (var tickable in tickables)
            tickable.Tick(dt);
    }
}
[Register.All]
abstract class TickableBase : MonoBehaviour 
{ 
    public abstract void Tick(float dt); 
}
```

## API reference

### [Inject]
**Automatically initializes the property in Awake() with a component (or an array of components) from the current GameObject.**

This internally uses `GetComponent<T>/GetComponents<T>` - it finds the first component of matching type.

Options:
* ***Optional*** - Set this to true to allow the component to be missing. By default, injection will log an error if the component is missing (or if the array of components is empty).
* ***IncludeInactive*** - not available because Unity doesn't have an equivalent `GetComponent`/`GetComponents` overload (injection always behaves as if `IncludeInactive` were true).

Allowed access modifiers:
* `{ get; }`
* `{ get; private set; }`
* `{ get; set; }`

Examples:
```csharp
[Inject] // single (first one found) component on current GameObject
Collider collider { get; } 

[Inject] // all components on current GameObject
Collider[] allColliders { get; }
```

### [Inject.FromChildren]
**Automatically initializes the property in Awake() with a component (or an array of components) from the current GameObject and/or child GameObjects.**

This internally uses `GetComponentInChildren<T>/GetComponentsInChildren<T>` - it searches the GameObject's child hierarchy recursively to locate components.

Options:
* ***Optional*** - Set this to true to allow the component to be missing. By default, injection will log an error if the component is missing (or if the array of components is empty).
* ***IncludeInactive*** - includes components that are disabled (or placed on inactive GameObjects). Keep in mind that the root GameObject is [always searched regardless](https://docs.unity3d.com/ScriptReference/Component.GetComponentsInChildren.html).

Examples:
```csharp
[Inject.FromChildren] // single (first one found) component on current GameObject or child GameObjects
Collider collider { get; } 

[Inject.FromChildren] // all components on current GameObject or child GameObjects
Collider[] allColliders { get; } 

[Inject.FromChildren(IncludeInactive = true)] // all components, including inactive child GameObjects
Collider[] allCollidersIncludingInactive { get; }
```

### [Inject.FromParents]
**Automatically initializes the property in Awake() with a component (or an array of components) from the current GameObject and/or child GameObjects.**

This internally uses `GetComponentInChildren<T>/GetComponentsInChildren<T>` - it searches the GameObject's child hierarchy recursively to locate components.

Options:
* ***Optional*** - Set this to true to allow the component to be missing. By default, injection will log an error if the component is missing (or if the array of components is empty).
* ***IncludeInactive*** - (included for API symmetry with `GetComponentsInParent`, but won't do much since injection happens in Awake when parents are necessarily already active)

Examples:
```csharp
[Inject.FromParents] // single (first one found) component on current GameObject or parent GameObjects
Collider collider { get; } 

[Inject.FromParents] // all components on current GameObject or parent GameObjects
Collider[] allColliders { get; } 
```

### [Inject.Single]
**Never write a singleton again! Makes the property return the currently registered singleton instance.**
In order for the object to register itself as a singleton, the type needs to be marked with `[Register.Single]`.
Can be used on static properties.

> Protip: you can inject the main camera, too! Simply tag your camera property with `[Inject.Single]` and it will automatically return a cached reference to `Camera.Main`.

Examples:
```csharp
[Register.Single] // ScriptableObject-based singleton.
                  // you need to put it in the "preloaded assets" in Project Settings 
class BulletConfig : ScriptableObject
{
    public float BulletVelocity;
}

[Register.Single] // MonoBehaviour-based singleton.
                  // you should put it in the scene or spawn it however you like
class BulletSpawner : MonoBehaviour
{
    // our BulletConfig instance is automatically available here
    [Inject.Single]
    BulletConfig bulletConfig { get; }

    public GameObject bulletPrefab;

    public void SpawnBullet(Vector3 position, Quaternion rotation, Vector3 direction)
        => Instantiate(bulletPrefab, position, rotation).GetComponent<Rigidbody>().velocity 
            = direction.normalized * bulletConfig.BulletVelocity;
}

class Gun : MonoBehaviour
{
    // our BulletSpawner instance is automatically available here
    [Inject.Single]
    BulletSpawner bulletSpawner { get; }

    // resolves Camera.main and returns a cached reference until the camera is destroyed
    [Inject.Single]
    Camera camera { get; }

    void Fire()
        => bulletSpawner.SpawnBullet(transform.position, transform.rotation, transform.forward);
}
```

### [Inject.All]
**Never lose track of your component's instances again! Makes the property return the set of currently active objects of this type.**

In order for the object to register itself in the active objects collection, the type needs to be marked with `[Register.All]`.
Can be used on static properties.

Examples:
```csharp
class BulletUpdater : MonoBehaviour
{
    // this property lets you access all active bullets in the game
    [Inject.All]
    public Bullet[] allBullets { get; }

    void Update()
    {
        foreach (var bullet in allBullets)
            /* do something with bullet */
    }
}

[Register.All] // allows this class to be injected using [Inject.All]
class Bullet : MonoBehaviour { }

```

### [Register.Single]
**Registers the type as a singleton that can be injected using `[Inject.Single]`.**
This property will log an error if there is no registered singleton instance of this type, or if multiple objects try to register themselves as the active instance. You can change the registered instance at runtime by disabling/deleting the object

**Supported types:**

* ***MonoBehaviour***:
    The object will register/unregister itself as singleton in OnEnable/OnDisable. (If needed, you can use script execution order to make sure that the singleton registers itself before it is used)
* ***ScriptableObject***:
    Adds a GUI to the object header that allows you to select the active ScriptableObject singleton instance.
    This instance is automatically added to preloaded assets (as the only instance of the type).
* ***Interface***:
    Allows you to use `[Inject.Single]` with this interface type. Make sure the types you're injecting are also registered using `[Register.Single]`.

### [Register.All]
**Tracks all active instances of the type so that they can be injected using `[Inject.All]`.**
This creates a static collection of active instances of this type. The objects will automatically register/unregister themselves in OnEnable/OnDisable. This means you can assume are instances returned by the property are non-null.

**Supported types:**

* ***MonoBehaviour***:
    The object will automatically register/unregister itself in OnEnable/OnDisable.
* ***ScriptableObject***:
    The object will automatically register/unregister itself in OnEnable/OnDisable.
    In practice, this returns all of the loaded instances of the ScriptableObject in your project.
    You might want to add them to preloaded assets (in Player Settings) to ensure they're available in build.
* ***Interface***:
    Allows you to use `[Inject.All]` with this interface type. Make sure that the actual MonoBehaviour/ScriptableObject
    types you're injecting are also registered using `[Register.All]`.

### [Inject.Lazy], [Inject.FromChildren.Lazy], [Inject.FromParents.Lazy]
Equivalent to regular `[Inject]`, but returns the current value every time the property is accessed (no caching, no null/empty checks).

> **WARNING: The Inject.Lazy APIs use reuse the same static memory buffer to avoid allocating arrays. Improper handling of the result array *will* cause your app to crash.** Make sure you know what you're doing if you're planning to use these APIs. (Never store the temporary array reference outside of the local scope, do not use multiple temporary arrays at the same time)

### Medicine.NonAlloc
**Collection of useful functions for high-performance Unity programming.**

* `T[] FindObjectsOfType<T>(bool includeInactive = false)`

    Faster version of `Object.FindObjectsOfType<T>` (skips unnecessary array copying).

* `T[] FindObjectsOfTypeAll<T>()`

    Faster version of `Resources.FindObjectsOfTypeAll<T>` (skips unnecessary array copying).

* `T[] LoadAll<T>(string path)`

    Faster version of `Resources.LoadAll<T>` (skips unnecessary array copying).

* `T[] GameObject.GetComponentsNonAlloc<T>()`

    Extension method. Non-allocating version of `GameObject.GetComponents<T>`. This re-uses the same static memory buffer to store the component array, so make sure you do not store the array reference anywhere.

* `T[] GameObject.GetComponentsInChildrenNonAlloc<T>(bool includeInactive = false)`

    Extension method. Non-allocating version of `GameObject.GetComponentsInChildren<T>`. This re-uses the same static memory buffer to store the component array, so make sure you do not store the array reference anywhere.

* `T[] GameObject.GetComponentsInParentNonAlloc<T>(bool includeInactive = false)`

    Extension method. Non-allocating version of `GameObject.GetComponentsInParent<T>`. This re-uses the same static memory buffer to store the component array, so make sure you do not store the array reference anywhere.

* `T[] GameObject.GetComponentsInParentNonAlloc<T>(bool includeInactive = false)`

    Extension method. Non-allocating version of `GameObject.GetComponentsInParent<T>`. This re-uses the same static memory buffer to store the component array, so make sure you do not store the array reference anywhere.

* `T[] GetArray<T>(int length, bool clear = true) where T : class`

    Returns a temporary array of given length. This re-uses the same static memory buffer, so make sure you do not store the array reference anywhere.

* `List<T> GetList<T>(bool clear = true) where T : class`

    Returns a temporary generic list of given length. This re-uses the same static memory buffer, so make sure you do not store the list reference anywhere.

> **WARNING: Improper handling of the temporary array *will* cause your app to crash.** Make sure you know what you're doing if you're planning to use these APIs. (Never store the temporary array reference outside of the local scope, do not use multiple temporary arrays at the same time)

## FAQ

##### How does this work?
The library uses an `ILPostProcessor` (based on [Mono.Cecil](https://github.com/Unity-Technologies/cecil)) to modify compiled assemblies. This allows it to emit additional method calls and error checks based on the attributes you placed in your code. Here's a [before and after](https://gist.github.com/apkd/406df729fa2d8ba78a50a01d4d4b4468) comparison of how the post-processing works.
This approach is similar to how UNET used to work and how the `Entities.ForEach`/`Job.WithCode` ECS syntax is currently implemented.

##### Known issues
- If you're hiding the `Awake` method of your base class, make sure you always call it in the child class (`base.Awake()`) for injection to work. If you have no `Awake` method in the base class, you can define an empty one, or call `this.Reinject();` instead.

##### Why plain arrays instead of IEnumerable/IReadOnlyArray/ImmutableList/CoolCustomCollection?
Arrays get special treatment by the CLR that optimizes `foreach` iteration and `array[index]` accesses into basically direct memory accesses. This starts to matter on hot code paths, and means you can use `foreach` without losing out on performance.

##### Why properties instead of fields?
- More visibility options (eg. `{ get; private set; }`)
- Ability to easily replace the getter method with custom code (for example, case of singletons we can get the instance from `RuntimeHelpers.Singleton<T>` which automatically includes additional error checking)
