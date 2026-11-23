using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>Plays the opening while loading the playable tutorial.</summary>
[RequireComponent(typeof(VideoPlayer))]
public sealed class GhostPartyOpening : MonoBehaviour
{
    public const string ScenePath = "Assets/Scenes/OpeningScene.unity";
    const string TutorialPath = "Assets/TutorialScene.unity";
    const float PreparationTimeout = 8f;

    [SerializeField] RawImage picture;

    VideoPlayer video;
    AsyncOperation tutorialLoad;
    GhostPartyOpeningFade fade;
    bool handingOff;

    IEnumerator Start()
    {
        tutorialLoad = SceneManager.LoadSceneAsync(TutorialPath);
        tutorialLoad.allowSceneActivation = false;

        video = GetComponent<VideoPlayer>();
        if (video.clip == null || picture == null)
        {
            PlaybackFailed(video, "The video clip or its display is missing.");
            yield break;
        }

        video.loopPointReached += PlaybackEnded;
        video.errorReceived += PlaybackFailed;
        video.Prepare();

        float deadline = Time.realtimeSinceStartup + PreparationTimeout;
        while (!handingOff && !video.isPrepared && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (handingOff) yield break;
        if (!video.isPrepared)
        {
            PlaybackFailed(video, "Video preparation timed out.");
            yield break;
        }

        video.Play();
    }

    void LateUpdate()
    {
        if (video == null || picture == null || handingOff) return;

        if (!picture.enabled && video.frame >= 0 && video.texture != null)
        {
            picture.texture = video.texture;
            picture.enabled = true;
        }

        if (!picture.enabled || !video.isPlaying) return;
        float remaining = (float)(video.length - video.time);
        if (remaining > GhostPartyOpeningFade.CoverSeconds) return;

        if (fade == null) fade = GhostPartyOpeningFade.Create();
        fade.Opacity = Mathf.SmoothStep(0f, 1f, 1f - remaining / GhostPartyOpeningFade.CoverSeconds);
    }

    void OnDestroy()
    {
        if (fade != null && !handingOff) Destroy(fade.gameObject);
        if (video == null) return;
        video.loopPointReached -= PlaybackEnded;
        video.errorReceived -= PlaybackFailed;
    }

    void PlaybackEnded(VideoPlayer player)
    {
        if (handingOff) return;
        handingOff = true;
        if (fade == null) fade = GhostPartyOpeningFade.Create();
        fade.RevealAfter(tutorialLoad);
    }

    void PlaybackFailed(VideoPlayer player, string message)
    {
        if (handingOff) return;
        handingOff = true;
        Debug.LogWarning("Opening video could not play: " + message, this);
        if (fade != null) fade.RevealAfter(tutorialLoad);
        else tutorialLoad.allowSceneActivation = true;
    }
}
