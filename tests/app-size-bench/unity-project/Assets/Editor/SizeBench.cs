// Headless driver for the IL2CPP app-size matrix
// (docs/guides/il2cpp-size-protocol.md §4).
//
// This file used to exist only as a fenced code block in
// docs/reports/il2cpp-size-report.md §6, which meant CI could not run the
// matrix at all. It is now the canonical copy; the report's appendix is
// history.
//
// Parameters arrive either as command-line arguments (what CI uses —
// game-ci/unity-builder forwards `customParameters` to the editor but does
// not forward arbitrary environment variables into the container) or as
// environment variables (what a local run uses):
//
//   -sbOutput <path>    / SB_OUTPUT     APK path; relative paths resolve
//                                       against the project folder, so the
//                                       artifact always lands inside the
//                                       workspace CI later reads.
//   -sbDefines <a;b>    / SB_DEFINES    scripting define symbols
//   -sbValidate <mode>  / SB_VALIDATE   bench | game | probe | none
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SizeBench
{
    public static void ValidateAndBuild()
    {
        try
        {
            Validate();
            Build();
        }
        catch (Exception e)
        {
            Debug.LogError("SB_FAIL " + e);
            EditorApplication.Exit(1);
        }
    }

    static string Param(string flag, string envName, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag) return args[i + 1];
        }
        return Environment.GetEnvironmentVariable(envName) ?? fallback;
    }

    // Relative output paths resolve against the project folder (Assets/..),
    // which is inside the workspace no matter what the editor's working
    // directory happens to be.
    static string ResolveOutput()
    {
        var output = Param("-sbOutput", "SB_OUTPUT", null);
        if (string.IsNullOrEmpty(output)) throw new Exception("no output path (-sbOutput / SB_OUTPUT)");
        if (!Path.IsPathRooted(output))
            output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        return output;
    }

    static void Validate()
    {
        var mode = Param("-sbValidate", "SB_VALIDATE", "none");
        if (mode == "none") return;
        var asm = Assembly.Load("Assembly-CSharp");
        object result;
        switch (mode)
        {
            case "bench":
                result = asm.GetType("BenchEntry").GetMethod("Run").Invoke(null, new object[] { 7 });
                break;
            case "game":
                result = asm.GetType("DummyGame.GameWork").GetMethod("RunAll").Invoke(null, new object[] { 7 });
                break;
            case "probe":
                result = asm.GetType("Program").GetMethod("Run").Invoke(null, null);
                break;
            default:
                throw new Exception("unknown validate mode " + mode);
        }
        var block = "SB_VALIDATE_BEGIN\n" + result + "\nSB_VALIDATE_END";
        Debug.Log(block);
        // Also on disk, next to the APK: the editor log is streamed to the
        // container's stdout and is awkward to capture from a build action,
        // while a file beside the artifact is not.
        File.WriteAllText(ResolveOutput() + ".validate.txt", block);
    }

    static void Build()
    {
        var output = ResolveOutput();
        var defines = Param("-sbDefines", "SB_DEFINES", "");
        var nbt = NamedBuildTarget.Android;
        // A default project identifier can contain characters Android
        // rejects; the matrix compares sizes, so the id only has to be
        // valid and identical across rows.
        PlayerSettings.SetApplicationIdentifier(nbt, "com.sqlitehost.sizebench");
        PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(nbt, ManagedStrippingLevel.High);
        PlayerSettings.SetApiCompatibilityLevel(nbt, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetIl2CppCodeGeneration(nbt, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetIl2CppCompilerConfiguration(nbt, Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetScriptingDefineSymbols(nbt, defines);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.stripEngineCode = true;
        EditorUserBuildSettings.buildAppBundle = false;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Runner");
        var runnerType = Assembly.Load("Assembly-CSharp").GetType("Runner");
        if (runnerType == null) throw new Exception("Runner type not found");
        go.AddComponent(runnerType);
        EditorSceneManager.SaveScene(scene, "Assets/Main.unity");

        var report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Main.unity" }, output, BuildTarget.Android, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception("build result " + report.summary.result);
        Debug.Log("SB_BUILD_OK " + output);
        EditorApplication.Exit(0);
    }
}
