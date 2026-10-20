using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostParty.LevelWhitebox.Editor
{
    // One-shot migration, gated by an explicit local request file.
    // Updates existing authored objects; does not regenerate the floor plan.
    [InitializeOnLoad]
    public static class ResizeDimensions
    {
        const string Root="Assets/LevelWhitebox";
        const string Request="Logs/resize-dimensions.request";
        const float Factor=2f/3f;
        static ResizeDimensions() { EditorApplication.delayCall+=TryRun; }
        static void TryRun()
        {
            if(!File.Exists(Request))return;
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            { EditorApplication.delayCall+=TryRun;return; }
            Run();
        }
        public static void Run()
        {
            if(!File.Exists(Request))return;
            // Consume before mutation to prevent re-scaling on subsequent domain reloads.
            File.Move(Request,Request+".running");
            try
            {
                foreach(string guid in AssetDatabase.FindAssets("t:Mesh",new[]{Root+"/Meshes"}))
                {
                    string path=AssetDatabase.GUIDToAssetPath(guid);
                    if(!Path.GetFileName(path).StartsWith("Level01_FirstFloor_"))continue;
                    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    var vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i++)vertices[i].y*=Factor;
                    mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
                }
                foreach(string id in new[]{"Level01_FirstFloor","Level02_SecondFloor"})
                {
                    string prefab=Root+"/Prefabs/"+id+"_Structure.prefab";
                    var contents=PrefabUtility.LoadPrefabContents(prefab);
                    Resize(contents.transform);
                    PrefabUtility.SaveAsPrefabAsset(contents,prefab);
                    PrefabUtility.UnloadPrefabContents(contents);
                    string path=Root+"/Scenes/"+id+".unity";
                    var scene=SceneManager.GetSceneByPath(path);
                    bool alreadyOpen=scene.IsValid()&&scene.isLoaded;
                    if(!alreadyOpen)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    var structure=scene.GetRootGameObjects().Single(g=>g.name==id+"_Structure");
                    Resize(structure.transform);
                    foreach(var collider in structure.GetComponentsInChildren<MeshCollider>())
                    { var m=collider.sharedMesh;collider.sharedMesh=null;collider.sharedMesh=m; }
                    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                    Validate(structure.transform,id.Contains("Second"));
                    // Render only this scene's structure, avoiding the other loaded floor.
                    var hidden=SceneManager.GetActiveScene().GetRootGameObjects()
                        .Where(g=>g.scene!=scene&&g.activeSelf).ToArray();
                    foreach(var g in hidden)g.SetActive(false);
                    Render(scene,id);
                    foreach(var g in hidden)g.SetActive(true);
                    if(!alreadyOpen)EditorSceneManager.CloseScene(scene,true);
                }
                AssetDatabase.SaveAssets();
                File.Delete(Request+".running");
                File.WriteAllText("Logs/resize-dimensions-result.txt","SUCCESS: walls=2m; rails=0.8m; pit=-0.3m; both scenes and prefabs updated.");
                Debug.Log("RESIZE_DIMENSIONS_OK");
            }
            catch(Exception e)
            {
                File.WriteAllText("Logs/resize-dimensions-result.txt",e.ToString());Debug.LogException(e);
            }
        }
        static void Resize(Transform root)
        {
            foreach(Transform group in root)
            {
                if(group.name=="02_BlackPlanLines_Walls_3m_80mm"||group.name=="03_BluePlanLines_Railing_12m")
                {
                    foreach(Transform child in group)
                    {
                        var p=child.localPosition;p.y*=Factor;child.localPosition=p;
                        var s=child.localScale;s.y*=Factor;child.localScale=s;
                    }
                    group.name=group.name.Replace("Walls_3m","Walls_2m").Replace("Railing_12m","Railing_08m");
                }
                if(group.name.Contains("Depth045"))group.name=group.name.Replace("Depth045","Depth030");
            }
        }
        static void Validate(Transform root,bool upper)
        {
            var walls=root.Find("02_BlackPlanLines_Walls_2m_80mm");
            foreach(Transform t in walls)
                if(Mathf.Abs(t.localScale.y-2)>.001f||Mathf.Abs(t.localScale.z-.08f)>.001f)throw new Exception("Wall dimension mismatch");
            if(upper)
            {
                foreach(Transform t in root.Find("03_BluePlanLines_Railing_08m"))
                    if(t.name.StartsWith("RailPost")&&Mathf.Abs(t.localScale.y-.8f)>.001f)throw new Exception("Rail dimension mismatch");
            }
            else
            {
                var bottom=root.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="DanceFloor_Bottom");
                if(Mathf.Abs(bottom.sharedMesh.bounds.max.y+.3f)>.001f)throw new Exception("Pit depth mismatch");
            }
        }
        static void Render(Scene scene,string id)
        {
            var camera=scene.GetRootGameObjects().Select(g=>g.GetComponent<Camera>()).First(c=>c);
            var rt=new RenderTexture(1600,1200,24);var old=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(1600,1200,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,1600,1200),0,0);tex.Apply();
            File.WriteAllBytes("Logs/Whitebox/"+id+"_2m.png",tex.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=old;
            UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
