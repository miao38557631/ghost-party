using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GhostParty.LevelWhitebox.Editor
{
    [InitializeOnLoad]
    public static class WhiteboxPlayValidation
    {
        const string Key="Whitebox.PlayValidation";
        static int frames;
        static readonly string[] Scenes={"Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity","Assets/LevelWhitebox/Scenes/Level02_SecondFloor.unity","Assets/Scenes/SampleScene.unity"};
        static WhiteboxPlayValidation() { EditorApplication.playModeStateChanged+=State; }
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this regression check in batch mode.");
            SessionState.SetBool(Key,true); SessionState.SetInt(Key+"Index",0);
            OpenNext();
        }
        static void OpenNext()
        {
            int i=SessionState.GetInt(Key+"Index",0);
            if(i>=Scenes.Length)
            {
                SessionState.SetBool(Key,false);
                EditorSceneManager.OpenScene(Scenes[0]);
                Debug.Log("WHITEBOX_PLAY_VALIDATION_OK: both whiteboxes isolated; original prototype still bootstraps.");
                EditorApplication.Exit(0); return;
            }
            EditorSceneManager.OpenScene(Scenes[i]);
            EditorApplication.EnterPlaymode();
        }
        static void State(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Key,false)) return;
            if(state==PlayModeStateChange.EnteredPlayMode) { frames=0; EditorApplication.update+=Check; }
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetInt(Key+"Index",SessionState.GetInt(Key+"Index",0)+1);
                EditorApplication.delayCall+=OpenNext;
            }
        }
        static void Check()
        {
            if(++frames<30) return;
            EditorApplication.update-=Check;
            int i=SessionState.GetInt(Key+"Index",0);
            bool prototype=UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>()!=null;
            if(prototype!=(i==2))
            {
                Debug.LogError("WHITEBOX_PLAY_VALIDATION_FAILED: "+Scenes[i]);
                SessionState.SetBool(Key,false); EditorApplication.Exit(1); return;
            }
            Debug.Log("PLAY_CHECK_OK: "+Scenes[i]+" prototype="+prototype);
            EditorApplication.ExitPlaymode();
        }
    }
}
