using Medicine;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TestUtility
{
    public static void DestroyAllGameObjects()
    {
        var testRunner = GameObject.Find("Code-based tests runner");

        using (var gameObjects = SceneManager.GetActiveScene().GetRootGameObjectsPooledList())
            foreach (var gameObject in gameObjects)
                if (gameObject != testRunner)
                    Object.DestroyImmediate(gameObject);
    }
}