#if !HAS_UNITY_PHYSICSMODULE
public abstract class Collider : UnityEngine.MonoBehaviour { }

public sealed class BoxCollider : Collider { }

public sealed class SphereCollider : Collider { }

public sealed class Rigidbody : Collider { }
#endif