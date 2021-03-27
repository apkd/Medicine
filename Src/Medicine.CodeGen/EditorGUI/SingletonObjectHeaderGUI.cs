using System;
using Medicine;
using UnityEditor;
using UnityEngine;
using static System.Reflection.BindingFlags;
using Object = UnityEngine.Object;

[InitializeOnLoad]
static class SingletonObjectHeaderGUI
{
    static readonly Type[] typesWithInjectSingleAttribute;

    static readonly GUIContent label = new GUIContent(
        text: "Set as current active singleton instance",
        tooltip: "This adds the ScriptableObject to Preloaded Assets (in Player Settings), which makes it resolvable using [Inject.Single]."
    );

    static SingletonObjectHeaderGUI()
    {
        var types = TypeCache.GetTypesWithAttribute<Register.Single>();
        typesWithInjectSingleAttribute = new Type[types.Count];
        types.CopyTo(typesWithInjectSingleAttribute, 0);

        Editor.finishedDefaultHeaderGUI += DrawSingletonGUI;
    }

    static void DrawSingletonGUI(Editor editor)
    {
        if (!(editor.target is ScriptableObject))
            return;

        if (editor.targets.Length > 1)
            return;

        var targetObject = editor.target;
        var type = targetObject.GetType();

        bool HasInjectSingleAttr()
        {
            foreach (var candidate in typesWithInjectSingleAttribute)
                if (type == candidate)
                    return true;

            return false;
        }

        if (!HasInjectSingleAttr())
            return;

        var preloadedAssets = PlayerSettings.GetPreloadedAssets();

        bool isAdded = false;

        foreach (var asset in preloadedAssets)
            if (asset == targetObject)
                isAdded = true;

        var controlRect = EditorGUILayout.GetControlRect(
            hasLabel: false,
            height: 14,
            options: Array.Empty<GUILayoutOption>()
        );

        var oldColor = GUI.color;
        var color = oldColor;
        color.a = 0.8f;
        GUI.color = color;

        bool shouldBeAdded = EditorGUI.ToggleLeft(
            position: controlRect,
            label: label,
            value: isAdded
        );

        GUI.color = oldColor;

        if (shouldBeAdded == isAdded)
            return;

        var preloadedAssetsList = NonAlloc.GetList<Object>();

        // remove all objects of target object's type
        // (to avoid multiple instances of the same singleton type being registered)
        foreach (var asset in preloadedAssets)
            if (!asset || asset.GetType() != type)
                preloadedAssetsList.Add(asset);

        if (shouldBeAdded)
            preloadedAssetsList.Add(targetObject);

        PlayerSettings.SetPreloadedAssets(preloadedAssetsList.ToArray());

        if (shouldBeAdded && Application.isPlaying)
        {
            // get generic instance of the singleton helper type
            var helper = typeof(RuntimeHelpers.Singleton<>).MakeGenericType(type);
            
            // get current singleton instance
            var currentInstance = helper.GetField("instance", Static | NonPublic).GetValue(null);

            // unregister current singleton instance
            if (currentInstance as Object)
                helper.GetMethod("UnregisterInstance", Static | Public).Invoke(null, new[] { currentInstance });

            // replace with new singleton instance
            helper.GetMethod("RegisterInstance", Static | Public).Invoke(null, new object[] { targetObject });
        }
    }
}
