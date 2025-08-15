using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Medicine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using static UnityEditor.EnterPlayModeOptions;
using Object = UnityEngine.Object;

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

public static class CI
{
    sealed class TestRunnerLambdaCallbacks : ICallbacks
    {
        public Action<ITestAdaptor> RunStarted { get; init; }
        public Action<ITestResultAdaptor> RunFinished { get; init; }
        public Action<ITestAdaptor> TestStarted { get; init; }
        public Action<ITestResultAdaptor> TestFinished { get; init; }

        void ICallbacks.RunStarted(ITestAdaptor testsToRun)
            => RunStarted?.Invoke(testsToRun);

        void ICallbacks.RunFinished(ITestResultAdaptor result)
            => RunFinished?.Invoke(result);

        void ICallbacks.TestStarted(ITestAdaptor test)
            => TestStarted?.Invoke(test);

        void ICallbacks.TestFinished(ITestResultAdaptor result)
            => TestFinished?.Invoke(result);
    }

    [UsedImplicitly]
    [SuppressMessage("ReSharper", "AsyncVoidMethod")]
    [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
    public static async void RunTests()
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = DisableDomainReload | DisableSceneReload;

        var testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
        TestRunnerLambdaCallbacks callbacks = null!;
        int failCount = 0;

        try
        {
            static void WriteTestResults(ITestResultAdaptor results, string filename)
            {
#if UNITY_6000_0_OR_NEWER
                File.Copy(
                    $"{Application.persistentDataPath}/TestResults.xml",
                    $"test-results/{filename}"
                );
#else
                var resultsWriterType = Type.GetType("UnityEditor.TestTools.TestRunner.Api.ResultsWriter,UnityEditor.TestRunner")!;
                object resultsWriter = Activator.CreateInstance(resultsWriterType, true);
                resultsWriter.GetType()
                    .GetMethod("WriteResultToFile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    ?.Invoke(resultsWriter, new object[] { results, $"test-results/{filename}" });
#endif
            }

            Debug.Log("Running edit mode tests...");
            {
                bool runFinished = false;

                callbacks = new()
                {
                    TestStarted = static test => Console.WriteLine($"Running test: {test.Name}"),
                    RunFinished = results =>
                    {
                        try
                        {
                            failCount += results.FailCount;
                            WriteTestResults(results, "Edit mode tests.xml");
                        }
                        finally
                        {
                            runFinished = true;
                            testRunnerApi.UnregisterCallbacks(callbacks);
                        }
                    },
                };

                testRunnerApi.RegisterCallbacks(callbacks);
                testRunnerApi.Execute(new(new Filter { testMode = TestMode.EditMode }));

                while (!runFinished)
                    await Task.Delay(100);
            }

            Debug.Log("Running play mode tests...");
            {
                bool runFinished = false;

                callbacks = new()
                {
                    TestStarted = static test => Console.WriteLine($"Running test: {test.Name}"),
                    RunFinished = results =>
                    {
                        try
                        {
                            failCount += results.FailCount;
                            WriteTestResults(results, "Play mode tests.xml");
                        }
                        finally
                        {
                            runFinished = true;
                            testRunnerApi.UnregisterCallbacks(callbacks);
                        }
                    },
                };

                testRunnerApi.RegisterCallbacks(callbacks);
                testRunnerApi.Execute(new(new Filter { testMode = TestMode.PlayMode }));

                while (!runFinished)
                    await Task.Delay(100);
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            EditorApplication.Exit(failCount);
        }
    }
}