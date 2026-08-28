// Headless driver for the IL2CPP app-size matrix
// (docs/guides/il2cpp-size-protocol.md §4 for Android, §7 for iOS).
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
//   -sbOutput <path>    / SB_OUTPUT     build output; relative paths resolve
//                                       against the project folder, so the
//                                       artifact always lands inside the
//                                       workspace CI later reads. Android
//                                       wants an .apk FILE, iOS a DIRECTORY
//                                       (Unity emits an Xcode project, not a
//                                       binary).
//   -sbDefines <a;b>    / SB_DEFINES    scripting define symbols
//   -sbValidate <mode>  / SB_VALIDATE   bench | game | probe | none
//   -sbTarget <t>       / SB_TARGET     android | ios (case-insensitive),
//                                       default android
//
// -sbTarget defaults to android on purpose: the Android matrix is the one
// that has actually run green, and a defaulted flag keeps its behaviour
// identical to before iOS existed. Both branches pin the same knobs so a
// row means the same thing on either platform; the iOS branch adds the
// iOS-only ones. What it can NEVER add is comparability of absolute bytes
// between the two — see §7 of the protocol.
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
        // Also on disk, next to the build output: the editor log is streamed
        // to the container's stdout and is awkward to capture from a build
        // action, while a file beside the artifact is not.
        File.WriteAllText(ResolveOutput() + ".validate.txt", block);
    }

    static void Build()
    {
        var output = ResolveOutput();
        var defines = Param("-sbDefines", "SB_DEFINES", "");
        // Matched case-insensitively, and only after lowering: the
        // unity-builder step in .github/workflows/ios-size-bench.yml spells
        // the same platform `iOS`, so anyone normalising the two spellings —
        // the natural instinct, iOS being the correct casing everywhere else
        // — would otherwise get "unknown target iOS" and a dead matrix. The
        // default is still android, and the error still echoes the spelling
        // that was actually passed.
        var target = Param("-sbTarget", "SB_TARGET", "android");

        BuildTarget buildTarget;
        switch (target.ToLowerInvariant())
        {
            case "android":
                buildTarget = BuildTarget.Android;
                ConfigureAndroid(defines);
                break;
            case "ios":
                buildTarget = BuildTarget.iOS;
                ConfigureIOS(defines);
                break;
            default:
                throw new Exception("unknown target " + target + " (-sbTarget / SB_TARGET): android | ios");
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Runner");
        var runnerType = Assembly.Load("Assembly-CSharp").GetType("Runner");
        if (runnerType == null) throw new Exception("Runner type not found");
        go.AddComponent(runnerType);
        EditorSceneManager.SaveScene(scene, "Assets/Main.unity");

        var report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Main.unity" }, output, buildTarget, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception("build result " + report.summary.result);
        Debug.Log("SB_BUILD_OK " + output);
        EditorApplication.Exit(0);
    }

    static void ConfigureAndroid(string defines)
    {
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
    }

    // Mirrors ConfigureAndroid's managed-side knobs against
    // NamedBuildTarget.iOS — a row only means the same thing on both
    // platforms if the managed side is configured the same way, and managed
    // stripping in particular is per-NamedBuildTarget, so leaving one out
    // would silently compare different builds.
    //
    // Two Android knobs deliberately have no counterpart here, both because
    // a Unity iOS build only emits an Xcode project rather than the shipped
    // artifact. The architecture: ConfigureAndroid pins ARM64 because the
    // .apk it produces is the artifact, whereas the architecture the Xcode
    // project is compiled for is pinned on the xcodebuild line (ARCHS=arm64,
    // ONLY_ACTIVE_ARCH=NO) in .github/workflows/ios-size-bench.yml, which
    // wins over whatever Unity wrote into the pbxproj. And the package
    // format: buildAppBundle chooses .apk over .aab, a choice iOS has no
    // analogue for, since there is one output shape and it is a project.
    //
    // NOTE FOR WHOEVER SEES THIS FAIL FIRST: no Unity editor exists on the
    // machine where this branch was written, so every spelling below was
    // checked against the 2022.3 scripting reference rather than compiled.
    // The doc URL sits next to anything that is not a direct mirror of the
    // Android line above it.
    static void ConfigureIOS(string defines)
    {
        var nbt = NamedBuildTarget.iOS;
        PlayerSettings.SetApplicationIdentifier(nbt, "com.sqlitehost.sizebench");
        PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(nbt, ManagedStrippingLevel.High);
        PlayerSettings.SetApiCompatibilityLevel(nbt, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetIl2CppCodeGeneration(nbt, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetIl2CppCompilerConfiguration(nbt, Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetScriptingDefineSymbols(nbt, defines);
        PlayerSettings.stripEngineCode = true;

        // Device, not simulator: a simulator slice is x86_64/arm64-sim code
        // built against a different SDK and is not the thing that ships.
        // docs.unity3d.com/2022.3/Documentation/ScriptReference/iOSSdkVersion.html
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        // The deployment target decides which SDK availability branches the
        // compiler keeps, so it is a size input. Pinned rather than left at
        // whatever the editor defaults to, so a Unity patch cannot move it
        // under a month-over-month comparison.
        // docs.unity3d.com/2022.3/Documentation/ScriptReference/PlayerSettings.iOS-targetOSVersionString.html
        PlayerSettings.iOS.targetOSVersionString = "13.0";
        // FastButNoExceptions strips managed exception support and would
        // make the iOS rows measure a different language than the Android
        // ones. SlowAndSafe is the default; pinned because it is a size
        // knob that only exists on iOS.
        // docs.unity3d.com/2022.3/Documentation/ScriptReference/ScriptCallOptimizationLevel.html
        PlayerSettings.iOS.scriptCallOptimization = ScriptCallOptimizationLevel.SlowAndSafe;
        // CI has no Apple account. Automatic signing would write a team id
        // and a provisioning style into the generated pbxproj; the macOS
        // job builds with CODE_SIGNING_ALLOWED=NO and wants neither.
        // docs.unity3d.com/2022.3/Documentation/ScriptReference/PlayerSettings.iOS-appleEnableAutomaticSigning.html
        PlayerSettings.iOS.appleEnableAutomaticSigning = false;
        PlayerSettings.iOS.appleDeveloperTeamID = "";
        // Selects the configuration the generated SCHEME runs. Per Unity's
        // page for this member that is all it does — it does not decide
        // which configurations the project has, and it is not what selects
        // what CI builds: the `-configuration Release` on the xcodebuild
        // line does that. Pinned so a scheme opened by hand lands on the
        // same configuration the workflow measures.
        // docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorUserBuildSettings-iOSXcodeBuildConfig.html
        EditorUserBuildSettings.iOSXcodeBuildConfig = XcodeBuildConfig.Release;
        // Load-bearing for the Linux->macOS handoff: symlinked sources point
        // into the editor installation, which does not exist on the machine
        // that runs xcodebuild. False is the default; pinning it means the
        // handoff does not depend on a default staying put.
        // docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorUserBuildSettings-symlinkSources.html
        EditorUserBuildSettings.symlinkSources = false;
    }
}
