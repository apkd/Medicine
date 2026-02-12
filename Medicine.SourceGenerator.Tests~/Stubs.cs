static class Stubs
{
    public const string Core
        = """
          using System;

          namespace UnityEngine
          {
              public class Object
              {
                  public static T[] FindObjectsOfType<T>() where T : Object
                      => Array.Empty<T>();

                  public static T? FindObjectOfType<T>() where T : Object
                      => default;

                  public static T? FindFirstObjectByType<T>() where T : Object
                      => default;

                  public static T? FindAnyObjectByType<T>() where T : Object
                      => default;
              }

              public class Component : Object
              {
                  public T? GetComponent<T>() where T : Component
                      => default;

                  public T[] GetComponents<T>() where T : Component
                      => Array.Empty<T>();

                  public T? GetComponentInChildren<T>() where T : Component
                      => default;

                  public T[] GetComponentsInChildren<T>() where T : Component
                      => Array.Empty<T>();

                  public T? GetComponentInParent<T>() where T : Component
                      => default;

                  public T[] GetComponentsInParent<T>() where T : Component
                      => Array.Empty<T>();

                  public bool CompareTag(TagHandle tag)
                      => false;
              }

              public class Transform : Component { }
              public class Rigidbody : Component { }
              public readonly struct TagHandle { }

              public class GameObject : Object
              {
                  public bool CompareTag(TagHandle tag)
                      => false;
              }

              public class MonoBehaviour : Component { }

              public class ScriptableObject : Object
              {
                  public static T CreateInstance<T>() where T : ScriptableObject, new()
                      => new T();
              }
          }

          namespace Medicine
          {
              [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
              public sealed class InjectAttribute : Attribute { }

              [AttributeUsage(AttributeTargets.Struct)]
              public sealed class UnionHeaderAttribute : Attribute { }

              [AttributeUsage(AttributeTargets.Struct)]
              public sealed class UnionAttribute : Attribute
              {
                  public UnionAttribute(int id) { }
              }

              [AttributeUsage(AttributeTargets.Struct)]
              public sealed class DisallowReadonlyAttribute : Attribute { }

              public sealed class SingletonAttribute : Attribute
              {
                  [Flags]
                  public enum Strategy : uint
                  {
                      Replace = 0,
                      KeepExisting = 1 << 0,
                      ThrowException = 1 << 1,
                      LogWarning = 1 << 2,
                      LogError = 1 << 3,
                      Destroy = 1 << 4,
                      AutoInstantiate = 1 << 5,
                  }

                  public SingletonAttribute(Strategy strategy = Strategy.Replace) { }
              }

              public sealed class TrackAttribute : Attribute
              {
                  public TrackAttribute(
                      SingletonAttribute.Strategy strategy = SingletonAttribute.Strategy.Replace,
                      bool instanceIdArray = false,
                      bool transformAccessArray = false,
                      int transformInitialCapacity = 64,
                      int transformDesiredJobCount = -1,
                      bool cacheEnabledState = false,
                      bool manual = false
                  ) { }
              }

              [AttributeUsage(AttributeTargets.Class)]
              public sealed class UnmanagedAccessAttribute : Attribute
              {
                  public UnmanagedAccessAttribute(params string[] memberNames) { }

                  public UnmanagedAccessAttribute(
                      bool safetyChecks,
                      bool includePublic = true,
                      bool includePrivate = true,
                      params string[] memberNames
                  ) { }
              }

              [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
              public sealed class WrapValueEnumerableAttribute : Attribute { }

              [AttributeUsage(AttributeTargets.Assembly)]
              public sealed class GenerateUnityConstantsAttribute : Attribute { }

              public interface IInstanceIndex
              {
                  int InstanceIndex { get; set; }
              }

              public interface IUnmanagedData<T> { }
              public interface IFindByID<T> { }
              public interface IFindByAssetID<T> { }

              public readonly struct TrackedInstances<T> { }
              public readonly struct LazyRef<T> where T : class { }
              public readonly struct LazyVal<T> where T : struct { }

              public static class Lazy
              {
                  public static LazyRef<T> From<T>(Func<T> init) where T : class
                      => default;

                  public static LazyVal<T> From<T>(in Func<T> init) where T : struct
                      => default;
              }

              public static class Find
              {
                  public static T? Singleton<T>()
                      => default;

                  public static TrackedInstances<T> Instances<T>()
                      => default;

                  public static T[] ObjectsByType<T>(bool includeInactive = false)
                      => Array.Empty<T>();
              }

              public static class MedicineExtensions
              {
                  public static T Optional<T>(this T value)
                      => value;
              }
          }

          namespace ZLinq
          {
              public interface IValueEnumerable<TEnumerator, TElement> { }
          }

          namespace Unity.Collections.LowLevel.Unsafe
          {
              public static class UnsafeUtility
              {
                  public static TTo As<TFrom, TTo>(ref TFrom value)
                      => default!;
              }
          }
          """;
}
