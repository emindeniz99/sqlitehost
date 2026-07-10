// Boots the package sample's SmokeBehaviour at runtime — no scene wiring
// needed. Uses reflection on purpose: the "Generated Sample" sample only
// exists in this project after it has been imported via the Package
// Manager, and this project must compile with ZERO errors even before
// that import (the Unity 2021 spike gate). Unity-2021-safe C#.

using System;
using UnityEngine;

namespace SqliteHostSpike
{
    public static class SmokeRunner
    {
        private const string SmokeBehaviourTypeName =
            "SqliteHost.Sample.SmokeBehaviour, SqliteHost.Sample";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Type behaviourType = Type.GetType(SmokeBehaviourTypeName);
            if (behaviourType == null)
            {
                Debug.LogWarning("[SqliteHost] SmokeBehaviour not found. Import the "
                    + "'Generated Sample' sample from the SqliteHost Runtime package "
                    + "(Window > Package Manager > SqliteHost Runtime > Samples) and "
                    + "enter Play mode again.");
                return;
            }

            var host = new GameObject("SqliteHostSmoke");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent(behaviourType);
            Debug.Log("[SqliteHost] SmokeBehaviour attached; watch the Console for the SMOKE result.");
        }
    }
}
