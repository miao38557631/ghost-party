using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GhostParty.LevelWhitebox.Editor
{
    public static class WhiteboxBuilder
    {
        const string Root="Assets/LevelWhitebox";
        const float Height=2f, Thickness=.08f, Storey=2.2f, SunkenDepth=.3f;
        static Material wall,floor,edge;
        static Transform structure;
        static int meshNumber;
        // Coordinates traced from Frame20 and the aligned overlay Frame19.
        // Common axes preserve the exact relative position of the two storeys.
        static Vector2 P(float x,float y) { return V((x-359)/54.7f,(249-y)/54.5f); }
        static Vector2 U(float x,float y) { return P(x+116,y-4); }
        static Vector2[] Trace(bool upper,params float[] c)
        {
            var a=new Vector2[c.Length/2];
            for(int i=0;i<a.Length;i++) a[i]=upper?U(c[i*2],c[i*2+1]):P(c[i*2],c[i*2+1]);
            return a;
        }
        static readonly Vector2[] Ground=Trace(false,72,176,172,176,172,130,255,130,299,74,578,74,646,168,646,240,615,240,615,354,550,354,550,397,245,397,245,425,172,425,172,360,72,300);
        static readonly Vector2[] Dance=Trace(false,263,192,483,192,483,337,263,337);
        static readonly Vector2[] Upper=Trace(true,56,254,56,134,141,134,151,124,166,114,184,108,184,78,277,78,277,38,412,38,412,78,530,78,530,244,499,244,499,329,430,329,430,310,395,310,395,329,391,329,391,254,369,254,369,159,220,159,199,161,181,167,165,177,152,189,143,200,143,254);
        static readonly Vector2[] Rail=Trace(true,143,200,152,189,165,177,181,167,199,161,220,159,369,159,369,254,391,254,391,329);

        [MenuItem("Ghost Party/Level Whitebox/Generate two structural scenes")]
        public static void Build()
        {
            if(!Application.isBatchMode && (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() ||
                !EditorUtility.DisplayDialog("Replace whiteboxes","Replaces the two generated scenes/prefabs using the 2026-10-21 plans.","Replace","Cancel"))) return;
            foreach(string sub in new[]{"Scenes","Prefabs","Materials","Meshes","Documentation"}) Directory.CreateDirectory(Root+"/"+sub);
            AssetDatabase.Refresh();
            wall=Mat("Wall",new Color(.88f,.89f,.91f));
            floor=Mat("Floor",new Color(.51f,.55f,.60f));
            edge=Mat("StructureEdge",new Color(.7f,.73f,.78f));
            BuildFloor(false); BuildFloor(true);
            AssetDatabase.SaveAssets();
            Validate();
            RenderAlignment();
            EditorSceneManager.OpenScene(Root+"/Scenes/Level01_FirstFloor.unity");
            Debug.Log("WHITEBOX_BUILD_OK revision 2026-10-21");
        }
        static void PolyWalls(string name,Vector2[] pts,Transform parent,float h=Height)
        {
            for(int i=0;i<pts.Length-1;i++) Wall(name+"_"+i,pts[i],pts[i+1],parent,h);
        }
        static void BuildFloor(bool upper)
        {
            meshNumber=0;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            string id=upper?"Level02_SecondFloor":"Level01_FirstFloor";
            structure=new GameObject(id+"_Structure").transform;
            var slabs=Group("01_Slabs");
            Slab(upper?"Upper_ConcaveSlab_OpenAtrium":"Ground_WithSunkenCutout",upper?Upper:Ground,
                 upper?new Vector2[0][]:new[]{Dance},0,upper?.12f:.1f,slabs,id);
            var walls=Group("02_BlackPlanLines_Walls_2m_80mm");
            if(!upper)
            {
                // Only black strokes in Frame20 are walls. No inferred colour-boundary partitions.
                PolyWalls("External",Trace(false,172,278,172,425,245,425,245,397,550,397,550,354,615,354,615,240,646,240,646,168,578,74,299,74,255,130,172,130,172,176,72,176,72,300,172,360),walls);
                PolyWalls("Dining_East",Trace(false,528,74,528,146),walls);
                PolyWalls("Bathroom_North",Trace(false,548,240,615,240),walls);
                PolyWalls("Vent_East",Trace(false,218,130,218,176),walls);
                PolyWalls("Vent_South",Trace(false,172,176,218,176),walls);
                var dance=Group("03_SunkenDanceFloor_Depth030_ThreeSteps");
                // Three 10cm descending perimeter terraces within the 4m footprint.
                for(int i=0;i<3;i++)
                {
                    float inset=i*.13f;
                    Vector2 min=P(263,337)+Vector2.one*inset;
                    Vector2 max=P(483,192)-Vector2.one*inset;
                    var outer=Rect(min,max);
                    var inner=Rect(min+Vector2.one*.13f,max-Vector2.one*.13f);
                    Slab("DescendingRing_"+(i+1),outer,new[]{inner},-(i+1)*.1f,.1f,dance,id);
                }
                var mn=P(263,337)+Vector2.one*.39f;
                var mx=P(483,192)-Vector2.one*.39f;
                Slab("DanceFloor_Bottom",Rect(mn,mx),new Vector2[0][],-SunkenDepth,.08f,dance,id);
                Marker("StairConnection_LShape",P(510,360),0);
                Marker("VentConnection_075m",P(195,152),0);
                Marker("Dining_Open",P(450,110),0);
                Marker("Kitchen_Open",P(310,125),0);
            }
            else
            {
                PolyWalls("Bedroom1",Trace(true,143,254,56,254,56,134,141,134),walls);
                PolyWalls("Bedroom1_EastShort",Trace(true,143,200,143,254),walls);
                PolyWalls("Gallery_North",Trace(true,141,134,151,124,166,114,184,108,184,78,277,78,277,38,412,38,412,118),walls);
                PolyWalls("Pool_Exterior",Trace(true,412,78,530,78,530,244,430,244),walls);
                PolyWalls("Bath_Exterior",Trace(true,499,244,499,329,430,329),walls);
                var rails=Group("03_BluePlanLines_Railing_08m");
                for(int i=0;i<Rail.Length-1;i++)
                {
                    var a=Rail[i]; var b=Rail[i+1];
                    Wall("RailTop_"+i,a,b,rails,.04f,.05f,.76f);
                    Wall("RailMid_"+i,a,b,rails,.02666667f,.04f,.4f);
                    int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/.6f));
                    for(int j=0;j<count;j++)
                    {
                        var v=Vector2.Lerp(a,b,j/(float)count);
                        Box("RailPost_"+i+"_"+j,new Vector3(v.x,.4f,v.y),new Vector3(.05f,.8f,.05f),wall,rails);
                    }
                }
                var end=Rail[Rail.Length-1];
                Box("RailPost_End",new Vector3(end.x,.4f,end.y),new Vector3(.05f,.8f,.05f),wall,rails);
                Marker("StairOpening_Reserved",U(412,320),0);
                Marker("Bedroom1",U(100,190),0); Marker("Bedroom2",U(340,80),0);
                Marker("PoolZone_NoBasinSpecified",U(470,170),0); Marker("Bath_OpenWest",U(465,285),0);
            }
            // Independent scenes both use Y=0. Offset second prefab by 2.2m when combining.
            var prefab=Root+"/Prefabs/"+id+"_Structure.prefab";
            PrefabUtility.SaveAsPrefabAsset(structure.gameObject,prefab);
            CreateView();
            EditorSceneManager.SaveScene(scene,Root+"/Scenes/"+id+".unity");
            RenderPreview(id,false); RenderPreview(id+"_Top",true);
        }
        static Vector2[] Rect(Vector2 min,Vector2 max) { return new[]{min,V(min.x,max.y),max,V(max.x,min.y)}; }
        static void Marker(string name,Vector2 at,float y)
        {
            var t=new GameObject("Anchor_"+name).transform;t.SetParent(structure,false);t.localPosition=new Vector3(at.x,y,at.y);
        }
        static void CreateView()
        {
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.7f,.73f,.78f);
            var sun=new GameObject("Preview_Light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=.9f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(60,-30,0);
            var cam=new GameObject("Preview_Camera").AddComponent<Camera>();cam.tag="MainCamera";
            cam.orthographic=true;cam.orthographicSize=6.7f;cam.nearClipPlane=.05f;cam.farClipPlane=100;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.18f,.21f,.25f);
            cam.transform.position=new Vector3(-1,18,-8);cam.transform.LookAt(new Vector3(0,.3f,0));
            cam.gameObject.AddComponent<AudioListener>();cam.gameObject.AddComponent<WhiteboxCamera>().speed=3;
        }
        static void RenderPreview(string id,bool top)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            var c=Camera.main;var pos=c.transform.position;var rot=c.transform.rotation;
            if(top)c.transform.SetPositionAndRotation(new Vector3(0,25,0),Quaternion.Euler(90,0,0));
            var rt=new RenderTexture(1600,1200,24);c.targetTexture=rt;c.Render();
            var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1600,1200,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,1600,1200),0,0);tex.Apply();
            Directory.CreateDirectory("Logs/Whitebox");File.WriteAllBytes("Logs/Whitebox/"+id+".png",tex.EncodeToPNG());
            c.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            c.transform.SetPositionAndRotation(pos,rot);
        }
        static void RenderAlignment()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var a=PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Level01_FirstFloor_Structure.prefab")) as GameObject;
            var b=PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Level02_SecondFloor_Structure.prefab")) as GameObject;
            b.transform.position=Vector3.up*Storey;
            CreateView();Camera.main.orthographicSize=8;Camera.main.transform.position=new Vector3(-11,15,-15);Camera.main.transform.LookAt(new Vector3(0,2,0));
            RenderPreview("StoreyAlignment",false);
        }
        [MenuItem("Ghost Party/Level Whitebox/Open first floor")]
        public static void OpenFirstFloor()
        {
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(Root+"/Scenes/Level01_FirstFloor.unity");
            EditorApplication.delayCall+=()=>{var v=EditorWindow.GetWindow<SceneView>();v.LookAt(Vector3.zero,Quaternion.Euler(65,0,0),8,true);v.Focus();};
        }
        static Material Mat(string name,Color color)
        {
            string path=Root+"/Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) { m=new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m,path); }
            m.color=color; m.SetFloat("_Glossiness",.08f); EditorUtility.SetDirty(m); return m;
        }
        static Transform Group(string name) { var t=new GameObject(name).transform; t.SetParent(structure,false); return t; }
        static void Zone(string name,float x,float z)
        {
            var t=new GameObject("Zone_"+name).transform; t.SetParent(structure,false); t.localPosition=new Vector3(x,.05f,z);
        }
        static GameObject Box(string name,Vector3 position,Vector3 scale,Material material,Transform parent)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(parent,false);
            go.transform.localPosition=position; go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material; return go;
        }
        static void Wall(string name,Vector2 a,Vector2 b,Transform parent,float h=Height,float t=Thickness,float y=0)
        {
            if(Vector2.Distance(a,b)<.01f) return;
            var mid=(a+b)/2; var d=b-a;
            var go=Box(name,new Vector3(mid.x,y+h/2,mid.y),new Vector3(d.magnitude,h,t),wall,parent);
            go.transform.localRotation=Quaternion.Euler(0,-Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,0);
        }
        static void Opening(string name,Vector2 a,Vector2 b,float width,float bottom,float top,Transform parent,float center=.5f)
        {
            float length=Vector2.Distance(a,b); var dir=(b-a)/length;
            var p=a+dir*(length*center-width/2); var q=p+dir*width;
            Wall(name+"_JambA",a,p,parent); Wall(name+"_JambB",q,b,parent);
            if(bottom>0) Wall(name+"_Sill",p,q,parent,bottom);
            Wall(name+"_Lintel",p,q,parent,Height-top,Thickness,top);
        }
        static Vector2 V(float x,float y) { return new Vector2(x,y); }
        static Vector2[] Points(params float[] values)
        {
            var p=new Vector2[values.Length/2]; for(int i=0;i<p.Length;i++) p[i]=V(values[i*2],values[i*2+1]); return p;
        }
        struct Crossing { public Vector2 a,b; public float X(float z) { return a.x+(b.x-a.x)*(z-a.y)/(b.y-a.y); } }
        // Scanline trapezoids triangulate the outer outline AND actual holes without hidden floor quads.
        static void Slab(string name,Vector2[] outside,Vector2[][] holes,float y,float depth,Transform parent,string id)
        {
            var loops=new List<Vector2[]> {outside}; loops.AddRange(holes);
            var cuts=loops.SelectMany(p=>p.Select(v=>v.y)).Distinct().OrderBy(v=>v).ToArray();
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            Action<Vector3,Vector3,Vector3,Vector3> quad=(a,b,c,d)=> { int n=vertices.Count; vertices.AddRange(new[]{a,b,c,d}); triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3}); };
            for(int s=0;s<cuts.Length-1;s++)
            {
                float lo=cuts[s],hi=cuts[s+1],mid=(lo+hi)/2;
                var crossings=new List<Crossing>();
                foreach(var loop in loops) for(int i=0;i<loop.Length;i++)
                {
                    Vector2 a=loop[i],b=loop[(i+1)%loop.Length];
                    if((a.y<mid&&b.y>mid)||(b.y<mid&&a.y>mid)) crossings.Add(new Crossing{a=a,b=b});
                }
                crossings=crossings.OrderBy(c=>c.X(mid)).ToList();
                if(crossings.Count%2!=0) throw new Exception("Invalid slab polygon: "+name);
                for(int i=0;i<crossings.Count;i+=2)
                {
                    var l=crossings[i]; var r=crossings[i+1];
                    quad(new Vector3(l.X(lo),y,lo),new Vector3(l.X(hi),y,hi),new Vector3(r.X(hi),y,hi),new Vector3(r.X(lo),y,lo));
                    quad(new Vector3(l.X(lo),y-depth,lo),new Vector3(r.X(lo),y-depth,lo),new Vector3(r.X(hi),y-depth,hi),new Vector3(l.X(hi),y-depth,hi));
                }
            }
            foreach(var loop in loops) for(int i=0;i<loop.Length;i++)
            {
                var a=loop[i]; var b=loop[(i+1)%loop.Length];
                if(loop==outside) quad(new Vector3(a.x,y,a.y),new Vector3(a.x,y-depth,a.y),new Vector3(b.x,y-depth,b.y),new Vector3(b.x,y,b.y));
                else quad(new Vector3(b.x,y,b.y),new Vector3(b.x,y-depth,b.y),new Vector3(a.x,y-depth,a.y),new Vector3(a.x,y,a.y));
            }
            var mesh=new Mesh {name=id+"_"+name}; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path=Root+"/Meshes/"+id+"_"+(meshNumber++)+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing) { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing; } else AssetDatabase.CreateAsset(mesh,path);
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider)); go.transform.SetParent(parent,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=floor; go.GetComponent<MeshCollider>().sharedMesh=mesh;
        }


        [MenuItem("Ghost Party/Level Whitebox/Validate saved scenes")]
        public static void Validate()
        {
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            foreach(string id in new[]{"Level01_FirstFloor","Level02_SecondFloor"})
            {
                var scene=EditorSceneManager.OpenScene(Root+"/Scenes/"+id+".unity");
                var ts=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
                if(ts.Any(t=>t.GetComponent<GhostPartyPrototype>() || GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))throw new Exception("Script isolation failure");
                var wg=ts.Single(t=>t.name=="02_BlackPlanLines_Walls_2m_80mm");
                foreach(Transform w in wg) if(Mathf.Abs(w.localScale.y-2)>.001f||Mathf.Abs(w.localScale.z-.08f)>.001f)throw new Exception("Wall dimensions incorrect");
                Physics.SyncTransforms();
                RaycastHit hit;
                bool has=Physics.Raycast(new Vector3(0,1,-.2f),Vector3.down,out hit,3);
                bool upper=id.Contains("Second");
                if(upper&&has)throw new Exception("Atrium is not empty");
                if(!upper&&(!has||Mathf.Abs(hit.point.y+SunkenDepth)>.001f))throw new Exception("Sunken floor depth incorrect");
                if(upper)
                {
                    var posts=ts.Where(t=>t.name.StartsWith("RailPost")).ToArray();
                    if(posts.Length==0||posts.Any(t=>Mathf.Abs(t.localScale.y-.8f)>.001f))throw new Exception("Railing height incorrect");
                }
                Debug.Log("WHITEBOX_VALIDATED "+id+" walls="+wg.childCount+" atriumEmpty="+!has+" sunkenDepth="+(!upper?-hit.point.y:0));
            }
        }
    }
}
