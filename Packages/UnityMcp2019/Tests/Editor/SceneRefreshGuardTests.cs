#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp2019.Tests
{
    public sealed class SceneRefreshGuardTests
    {
        private string folder;
        private Scene scene;
        private Scene second;
        private Scene previous;
        private bool savedEmptyScene;

        /// <summary>Creates an isolated test scene without closing any existing scenes.</summary>
        [SetUp]
        public void SetUp()
        {
            Mcp2019SceneRefreshGuard.Initialize();
            previous = SceneManager.GetActiveScene();
            folder = "Assets/McpSceneRefreshTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            savedEmptyScene = false;
            second = default(Scene);
            if (string.IsNullOrEmpty(previous.path))
            {
                if (!Application.isBatchMode && (previous.isDirty || previous.rootCount != 0))
                    Assert.Ignore("Save the untitled scene before running scene integration tests.");
                // Unity 2019 forbids additive scene creation alongside an untitled scene.
                Assert.IsTrue(EditorSceneManager.SaveScene(previous, folder + "/Previous.unity"));
                savedEmptyScene = true;
            }
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            new GameObject("disk_original");
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, folder + "/Test.unity"));
        }

        /// <summary>Removes only this test's temporary scene and assets, restoring the active scene.</summary>
        [TearDown]
        public void TearDown()
        {
            Scene current = SceneManager.GetSceneByPath(folder + "/Test.unity");
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (current.IsValid()) EditorSceneManager.CloseScene(current, true);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (second.IsValid() && second.isLoaded) EditorSceneManager.CloseScene(second, true);
            if (savedEmptyScene) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(folder);
        }

        /// <summary>Reloads external changes from disk while retaining the additive scene setup.</summary>
        [Test]
        public void CleanExternalSceneIsReloadedAndOtherScenesSurvive()
        {
            int count = SceneManager.sceneCount; // Reload must not close other loaded scenes.
            ChangeDiskName();
            Assert.IsTrue(Mcp2019SceneRefreshGuard.GetStatus().ExternalSceneChanges);
            Mcp2019SceneRefreshGuard.Prepare();
            Scene reloaded = SceneManager.GetSceneByPath(folder + "/Test.unity");
            Assert.AreEqual("disk_external", reloaded.GetRootGameObjects()[0].name);
            Assert.AreEqual(count, SceneManager.sceneCount);
            Assert.IsTrue(previous.IsValid());
            Assert.IsFalse(Mcp2019SceneRefreshGuard.GetStatus().ExternalSceneChanges);
            Assert.IsFalse(reloaded.isDirty);
        }

        /// <summary>Preserves both conflicting versions and reports the exact affected scene.</summary>
        [Test]
        public void DirtyExternalSceneBlocksBeforeAnyReload()
        {
            scene.GetRootGameObjects()[0].name = "unsaved_work";
            EditorSceneManager.MarkSceneDirty(scene);
            ChangeDiskName();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => Mcp2019SceneRefreshGuard.Prepare());
            StringAssert.Contains("scene_reload_required", error.Message);
            StringAssert.Contains(folder + "/Test.unity", error.Message);
            Assert.AreEqual("unsaved_work", scene.GetRootGameObjects()[0].name);
            StringAssert.Contains("disk_external", File.ReadAllText(scene.path));
            Assert.IsTrue(scene.isDirty);
        }

        /// <summary>Does not reload an unchanged scene just because it contains unsaved edits.</summary>
        [Test]
        public void DirtySceneWithoutDiskChangeDoesNotBlockCodeImport()
        {
            GameObject root = scene.GetRootGameObjects()[0];
            int id = root.GetInstanceID(); // The in-memory object and its Undo history must survive.
            root.name = "unsaved_work";
            EditorSceneManager.MarkSceneDirty(scene);
            Mcp2019SceneRefreshGuard.Prepare();
            Assert.AreEqual(id, scene.GetRootGameObjects()[0].GetInstanceID());
            Assert.AreEqual("unsaved_work", root.name);
            Assert.IsTrue(scene.isDirty);
        }

        /// <summary>A timestamp-only file update must not reload or reset object identities.</summary>
        [Test]
        public void IdenticalFileContentsDoNotTriggerReload()
        {
            int id = scene.GetRootGameObjects()[0].GetInstanceID();
            File.SetLastWriteTimeUtc(scene.path, DateTime.UtcNow.AddSeconds(2));
            Mcp2019SceneRefreshGuard.Prepare();
            Assert.AreEqual(id, scene.GetRootGameObjects()[0].GetInstanceID());
        }

        /// <summary>A conflict in an additive scene blocks the whole preflight before a clean scene reloads.</summary>
        [Test]
        public void ChecksAllChangedScenesBeforeReloadingAny()
        {
            second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(second);
            GameObject other = new GameObject("other_disk_original");
            Assert.IsTrue(EditorSceneManager.SaveScene(second, folder + "/Other.unity"));
            other.name = "other_unsaved";
            EditorSceneManager.MarkSceneDirty(second);
            File.WriteAllText(second.path, File.ReadAllText(second.path).Replace("other_disk_original", "other_disk_external"));
            ChangeDiskName();
            int id = scene.GetRootGameObjects()[0].GetInstanceID();
            Assert.Throws<InvalidOperationException>(() => Mcp2019SceneRefreshGuard.Prepare());
            Assert.AreEqual(id, scene.GetRootGameObjects()[0].GetInstanceID());
            Assert.AreEqual("disk_original", scene.GetRootGameObjects()[0].name);
            Assert.AreEqual("other_unsaved", other.name);
            Assert.IsTrue(second.isDirty);
        }

        /// <summary>Changing or removing a disk scene must never silently close the loaded scene.</summary>
        [Test]
        public void MissingSceneFileBlocksReload()
        {
            File.Delete(scene.path);
            Assert.Throws<InvalidOperationException>(() => Mcp2019SceneRefreshGuard.Prepare());
            Assert.AreEqual("disk_original", scene.GetRootGameObjects()[0].name);
        }

        /// <summary>Simulates an external text editor modifying the saved scene asset.</summary>
        private void ChangeDiskName()
        {
            string path = folder + "/Test.unity";
            File.WriteAllText(path, File.ReadAllText(path).Replace("disk_original", "disk_external"));
        }
    }
}
#endif
