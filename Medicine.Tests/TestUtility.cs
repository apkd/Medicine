#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using static UnityEditor.EnterPlayModeOptions;
#endif
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Medicine;
using UnityEngine;
using UnityEngine.SceneManagement;
using static System.Runtime.CompilerServices.MethodImplOptions;
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

    [MethodImpl(AggressiveInlining)]
    public static bool IsNull(this object obj)
        => ReferenceEquals(obj, null);

    [MethodImpl(AggressiveInlining)]
    public static bool IsNotNull(this object obj)
        => !ReferenceEquals(obj, null);

    [MethodImpl(AggressiveInlining)]
    public static bool IsAlive(this object obj)
        => obj is Object unityObject
            ? Medicine.Internal.Utility.IsNativeObjectAlive(unityObject)
            : obj.IsNotNull();

    [MethodImpl(AggressiveInlining)]
    public static bool IsDead(this object obj)
        => obj is Object unityObject
            ? Medicine.Internal.Utility.IsNativeObjectDead(unityObject)
            : obj.IsNull();
}

#if UNITY_EDITOR
public static class CI
{
    const string TestResultsDirectory = "test-results";
    const string EditModeResultsFilename = "results-editmode.xml";
    const string PlayModeResultsFilename = "results-playmode.xml";
    const string CombinedResultsFilename = "report.txt";
    const string InitTestSceneSearchPattern = "InitTestScene*.unity";

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
                Directory.CreateDirectory(TestResultsDirectory);
                var outputPath = Path.Combine(TestResultsDirectory, filename);

#if UNITY_6000_0_OR_NEWER
                File.Copy($"{Application.persistentDataPath}/TestResults.xml", outputPath, overwrite: true);
#else
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                var resultsWriterType = Type.GetType("UnityEditor.TestTools.TestRunner.Api.ResultsWriter,UnityEditor.TestRunner")!;
                object resultsWriter = Activator.CreateInstance(resultsWriterType, true);
                resultsWriter.GetType()
                    .GetMethod("WriteResultToFile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    ?.Invoke(resultsWriter, new object[] { results, outputPath });
#endif
            }

            Debug.Log("Running edit mode tests...");
            {
                bool runFinished = false;

                callbacks = new()
                {
                    TestStarted = static test => Console.WriteLine($"[{test.Name}] Running test"),
                    TestFinished = static result =>
                    {
                        Console.WriteLine($"[{result.Test.Name}] Result: {result.ResultState}");
                        if (result.TestStatus is TestStatus.Failed)
                        {
                            Console.WriteLine($"[{result.Test.Name}] Message: {result.Message}");
                            Console.WriteLine(result.StackTrace);
                        }
                    },
                    RunFinished = results =>
                    {
                        try
                        {
                            failCount += results.FailCount;
                            WriteTestResults(results, EditModeResultsFilename);
                        }
                        catch (Exception ex)
                        {
                            failCount += 1;
                            Debug.LogException(ex);
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
                    TestStarted = static test => Console.WriteLine($"[{test.Name}] Running test"),
                    TestFinished = static result =>
                    {
                        Console.WriteLine($"[{result.Test.Name}] Result: {result.ResultState}");
                        if (result.TestStatus is TestStatus.Failed)
                        {
                            Console.WriteLine($"[{result.Test.Name}] Message: {result.Message}");
                            Console.WriteLine(result.StackTrace);
                        }
                    },
                    RunFinished = results =>
                    {
                        try
                        {
                            failCount += results.FailCount;
                            WriteTestResults(results, PlayModeResultsFilename);
                        }
                        catch (Exception ex)
                        {
                            failCount += 1;
                            Debug.LogException(ex);
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

            var editModeReportPath = Path.Combine(TestResultsDirectory, EditModeResultsFilename);
            var playModeReportPath = Path.Combine(TestResultsDirectory, PlayModeResultsFilename);
            var combinedReportPath = Path.Combine(TestResultsDirectory, CombinedResultsFilename);

            try
            {
                var editModeReport = TestReportParser.ParseTestRunReport(editModeReportPath, "EditMode");
                var playModeReport = TestReportParser.ParseTestRunReport(playModeReportPath, "PlayMode");
                string combinedReport = TestReportParser.BuildCombinedReport(editModeReport, playModeReport);
                await File.WriteAllTextAsync(combinedReportPath, combinedReport);
                Console.WriteLine(combinedReport);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

        }
        catch (Exception ex)
        {
            failCount += 1;
            Debug.LogException(ex);
        }
        finally
        {
            CleanupInitTestScenes();
            EditorApplication.Exit(failCount);
        }
    }

    static void CleanupInitTestScenes()
    {
        try
        {
            foreach (var scenePath in Directory.EnumerateFiles("Assets", InitTestSceneSearchPattern, SearchOption.TopDirectoryOnly))
                AssetDatabase.DeleteAsset(scenePath.Replace('\\', '/'));
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
#endif
