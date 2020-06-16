using System;
using System.Reflection;
using UnityEngine;

namespace Medicine
{
    public static class TestExtensions
    {
        public static T CallAwake<T>(this T component) where T : MonoBehaviour
        {
            component
                .GetType()
                .GetMethod("Awake", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, Array.Empty<object>());

            return component;
        }

        public static GameObject WithComponent<T>(this GameObject gameObject) where T : Component
        {
            gameObject.AddComponent<T>();
            return gameObject;
        }

        public static GameObject WithParent(this GameObject gameObject, Action<GameObject> configure)
        {
            var parent = new GameObject();
            gameObject.transform.parent = parent.transform;
            configure(parent);
            return gameObject;
        }

        public static GameObject WithChild(this GameObject gameObject, Action<GameObject> configure)
        {
            var parent = new GameObject();
            parent.transform.parent = gameObject.transform;
            configure(parent);
            return gameObject;
        }
    }
}
