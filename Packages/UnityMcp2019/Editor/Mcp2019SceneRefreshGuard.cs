#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrefabStageUtility = UnityEditor.SceneManagement.PrefabStageUtility;

namespace UnityMcp2019
{
    /// <summary>Preserves unsaved Editor work when MCP imports code or refreshes assets.</summary>
    internal static class Mcp2019SceneRefreshGuard
    {
        // SessionState survives script domain reloads, but never a new Editor session.
        private const string StateKey = "UnityMcp2019.SceneRefreshGuard";
        private static readonly MethodInfo ReloadMethod = typeof(EditorSceneManager).GetMethod(
            "ReloadScene", BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(Scene) }, null);
        private static GuardState state;
        internal static bool RefreshPending;

        /// <summary>Starts scene tracking before the bridge accepts script-edit requests.</summary>
        internal static void Initialize()
        {
            if (state != null) return;
            string saved = SessionState.GetString(StateKey, string.Empty);
            state = string.IsNullOrEmpty(saved) ? new GuardState() :
                JsonUtility.FromJson<GuardState>(saved) ?? new GuardState();
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            AssemblyReloadEvents.beforeAssemblyReload += Persist;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (Find(scene) == null) Remember(scene);
            }
        }

        /// <summary>Returns all loaded-scene and Prefab Stage conflicts without saving or reloading.</summary>
        internal static RefreshState GetStatus()
        {
            Initialize();
            List<SceneStatus> scenes = new List<SceneStatus>();
            bool changes = false;
            bool canReload = ReloadMethod != null; // Only changed, readable, clean scenes qualify.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                SceneBaseline baseline = Find(scene);
                // Compare contents, not timestamps: touching an unchanged scene must not destroy Undo.
                bool changed = baseline != null && !string.IsNullOrEmpty(scene.path) &&
                    baseline.Hash != Fingerprint(scene.path);
                changes |= changed;
                if (changed && (scene.isDirty || !File.Exists(scene.path))) canReload = false;
                scenes.Add(new SceneStatus { Path = scene.path, Handle = scene.handle,
                    IsDirty = scene.isDirty, ChangedOnDisk = changed });
            }
            var prefab = PrefabStageUtility.GetCurrentPrefabStage();
            return new RefreshState { Scenes = scenes.ToArray(), ExternalSceneChanges = changes,
                CanAutoReload = changes && canReload && prefab == null,
                PrefabStagePath = prefab == null ? string.Empty : prefab.prefabAssetPath,
                PrefabStageIsDirty = prefab != null && prefab.scene.isDirty,
                RefreshPending = RefreshPending, LastError = state.LastError };
        }

        /// <summary>Checks every affected scene before reloading any; never discards dirty scenes.</summary>
        internal static RefreshState Prepare()
        {
            Initialize();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("editor_busy: Wait until compilation, import and Play Mode finish.");
            RefreshState snapshot = GetStatus();
            foreach (SceneStatus item in snapshot.Scenes)
            {
                if (!item.ChangedOnDisk) continue;
                if (item.IsDirty || !File.Exists(item.Path) || ReloadMethod == null ||
                    !string.IsNullOrEmpty(snapshot.PrefabStagePath))
                    throw new InvalidOperationException("scene_reload_required: " + item.Path +
                        " changed on disk. Unsaved work is preserved. Resolve the scene/Prefab Stage " +
                        "in Unity before retrying; do not save over the external file or discard changes automatically.");
            }
            foreach (SceneStatus item in snapshot.Scenes)
            {
                if (!item.ChangedOnDisk) continue;
                Scene scene = SceneManager.GetSceneByPath(item.Path);
                // Recheck immediately before the synchronous reload; another callback may have edited it.
                if (!scene.IsValid() || scene.handle != item.Handle || scene.isDirty)
                    throw new InvalidOperationException("scene_reload_required: Scene state changed before reload: " + item.Path);
                if (!(bool)ReloadMethod.Invoke(null, new object[] { scene }))
                    throw new InvalidOperationException("scene_reload_failed: " + item.Path);
                Remember(SceneManager.GetSceneByPath(item.Path));
                Debug.Log("[MCP 2019] Reloaded externally changed clean scene: " + item.Path);
            }
            state.LastError = string.Empty;
            Persist();
            return GetStatus();
        }

        /// <summary>Records deferred import errors so clients do not mistake scheduling for completion.</summary>
        internal static void RecordFailure(Exception error)
        {
            Initialize();
            state.LastError = error.GetBaseException().Message;
            Persist();
            Debug.LogWarning("[MCP 2019] Asset refresh blocked: " + state.LastError);
        }

        /// <summary>Establishes the disk baseline only after Unity opens a scene.</summary>
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) { Remember(scene); }

        /// <summary>Accepts a deliberate Editor save as the new scene baseline.</summary>
        private static void OnSceneSaved(Scene scene) { Remember(scene); }

        /// <summary>Removes closed scenes so later requests cannot reload stale handles.</summary>
        private static void OnSceneClosed(Scene scene)
        {
            state.Scenes.RemoveAll(item => item.Handle == scene.handle);
            Persist();
        }

        /// <summary>Tracks a loaded scene's disk contents; excludes transient Prefab preview scenes.</summary>
        private static void Remember(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path) ||
                EditorSceneManager.IsPreviewScene(scene)) return;
            state.Scenes.RemoveAll(item => item.Handle == scene.handle || item.Path == scene.path);
            state.Scenes.Add(new SceneBaseline { Handle = scene.handle, Path = scene.path,
                Hash = Fingerprint(scene.path) });
            Persist();
        }

        /// <summary>Matches both handle and path to distinguish reopened scenes across reloads.</summary>
        private static SceneBaseline Find(Scene scene)
        {
            return state.Scenes.Find(item => item.Handle == scene.handle && item.Path == scene.path);
        }

        /// <summary>Hashes scene contents; inaccessible files are treated as changed, never safe to overwrite.</summary>
        private static string Fingerprint(string path)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                    return Convert.ToBase64String(sha.ComputeHash(stream));
            }
            catch (IOException) { return "unavailable"; }
            catch (UnauthorizedAccessException) { return "unavailable"; }
        }

        /// <summary>Retains baselines and the last import failure through script recompilation.</summary>
        private static void Persist() { SessionState.SetString(StateKey, JsonUtility.ToJson(state)); }

        [Serializable]
        private sealed class GuardState
        {
            public List<SceneBaseline> Scenes = new List<SceneBaseline>();
            public string LastError = string.Empty;
        }
        [Serializable]
        private sealed class SceneBaseline
        {
            public int Handle;
            public string Path;
            public string Hash;
        }
        [Serializable]
        internal sealed class SceneStatus
        {
            public int Handle;
            public string Path;
            public bool IsDirty;
            public bool ChangedOnDisk;
        }
        [Serializable]
        internal sealed class RefreshState
        {
            public SceneStatus[] Scenes;
            public bool ExternalSceneChanges;
            public bool CanAutoReload;
            public string PrefabStagePath;
            public bool PrefabStageIsDirty;
            public bool RefreshPending;
            public string LastError;
        }
    }
}
#endif
