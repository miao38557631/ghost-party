using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Controlled Play Mode presentation snapshots. Full input acceptance is
/// covered separately by GhostGamepadPlayChecks; these snapshots never award hits.</summary>
public static class CurtainDesignerUIReview
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static GhostPartyPrototype prototype;
    static CurtainGameplayUI view;
    static Camera camera;
    static Vector3 anchor;
    static int savedStep;

    public static string Begin()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode and possess the curtain first.");
        prototype = UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>();
        if (prototype == null) throw new InvalidOperationException("No curtain controller.");
        var type = prototype.GetType();
        if (type.GetField("mode", Flags).GetValue(prototype).ToString() != "Closeup")
            throw new InvalidOperationException("Possess the curtain before capturing the actual closeup camera.");
        savedStep = (int)type.GetField("curtainStep", Flags).GetValue(prototype);
        view = (CurtainGameplayUI)type.GetField("gameplayUI", Flags).GetValue(prototype);
        camera = (Camera)type.GetField("cam", Flags).GetValue(prototype);
        anchor = (Vector3)type.GetField("curtainHome", Flags).GetValue(prototype);
        prototype.enabled = false;
        Directory.CreateDirectory("Logs/UIIteration");
        return "Frozen actual closeup for presentation-only screenshots; gameplay checks run separately.";
    }

    public static string Frame(int step, float progress, bool held, bool pad, bool english, string name)
    {
        var type = prototype.GetType();
        type.GetField("curtainStep", Flags).SetValue(prototype, step);
        var cue = new CurtainGameplayUI.PlayerCue {
            kind = step < 2 ? CurtainGameplayUI.CueKind.Tap : step == 15 ? CurtainGameplayUI.CueKind.Hold : CurtainGameplayUI.CueKind.Direction,
            beatsToTarget = 0.5f, tolerance = 0.3f, motionProgress = progress,
            held = held, inputPressed = held, motionReady = true, armed = progress >= 1f,
            usingGamepad = pad, english = english,
            exitHint = english ? (pad ? "B to exit" : "Esc to exit") : (pad ? "B 退出" : "Esc 退出"),
            timing = english ? (step < 2 ? "Wait for the rings to meet" : step == 15 ? "Hold through the next beat" : !held ? "Hold LMB first" : progress >= 1f ? "Ready · keep holding for the beat" : pad ? "Push the stick with the arrow" : "Swipe with the arrow now")
                : step < 2 ? "等外圈合拢" : step == 15 ? "保持到下一拍" : !held ? "先按住左键" : progress >= 1f ? "到位了 · 按住等拍点" : pad ? "按箭头推动左摇杆" : "现在跟箭头滑动"
        };
        object[] args = { cue };
        type.GetMethod("FillDesignerGestureRoute", Flags).Invoke(prototype, args);
        cue = (CurtainGameplayUI.PlayerCue)args[0];
        cue.curtainVisual = prototype.CurtainVisual;
        if (cue.curtainVisual != null)
        {
            if (cue.kind == CurtainGameplayUI.CueKind.Direction || cue.kind == CurtainGameplayUI.CueKind.Hold)
                cue.curtainVisual.DriveGameplay(step, cue.gestureTarget, progress, held,
                    cue.kind == CurtainGameplayUI.CueKind.Hold, false, camera);
            else cue.curtainVisual.ReleaseGameplay();
        }
        view.ClearPlayerFeedback();
        view.PresentPlayerCue(true, cue, camera, anchor);
        var root = GameObject.Find("CurtainPlayerHUD").transform;
        var next = root.Find("NextDragTarget");
        var arrow = root.Find("DirectionArrow").GetComponent<Image>();
        bool drag = cue.kind == CurtainGameplayUI.CueKind.Direction && held;
        if (next.gameObject.activeSelf != (drag && cue.hasNextDirection && progress > 0.5f))
            throw new InvalidOperationException("Next-target visibility disagrees with the second-half rule.");
        if (drag && progress > 0.5f && !(arrow.color.a < 1f))
            throw new InvalidOperationException("Arrow did not fade in the second half.");
        if (drag && progress <= 0.5f && arrow.color.a != 1f)
            throw new InvalidOperationException("Arrow faded before half progress.");
        if (pad && root.Find("CurrentAction/MouseButton").gameObject.activeSelf)
            throw new InvalidOperationException("Mouse glyph leaked into gamepad prompts.");
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            if (graphic.raycastTarget) throw new InvalidOperationException("HUD captures pointer input: " + graphic.name);
        if ((int)type.GetField("curtainStep", Flags).GetValue(prototype) != step)
            throw new InvalidOperationException("Presentation advanced gameplay.");
        Canvas.ForceUpdateCanvases();
        ScreenCapture.CaptureScreenshot(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs/UIIteration/" + name + ".png"));
        return name + ": step=" + step + ", progress=" + progress + ", arrowAlpha=" + arrow.color.a
            + ", nextAlpha=" + next.GetComponent<CanvasGroup>().alpha + ", nextVisible=" + next.gameObject.activeSelf
            + ", ghost=" + root.Find("CurrentAction/GhostBody/Ghost").GetComponent<Image>().sprite.name
            + ", size=" + Screen.width + "x" + Screen.height;
    }

    // Uses production localization and presentation methods, not duplicated copy.
    public static string ProductionLanguageFrame(int step, bool english, string name)
    {
        var type = prototype.GetType();
        type.GetField("curtainStep", Flags).SetValue(prototype, step);
        type.GetField("curtainHeld", Flags).SetValue(prototype, step == 15);
        type.GetField("gestureArmed", Flags).SetValue(prototype, false);
        type.GetField("gestureRetryPending", Flags).SetValue(prototype, false);
        GhostLanguage.Set(english ? GhostPartyPrototype.Language.English : GhostPartyPrototype.Language.Chinese);
        // The review intentionally disables gameplay; refresh the presentation
        // explicitly because the normal Changed subscription is disabled too.
        type.GetMethod("OnLanguageChanged", Flags).Invoke(prototype, null);
        Canvas.ForceUpdateCanvases();
        var root = GameObject.Find("CurtainPlayerHUD").transform;
        var instruction = root.Find("Caption/Instruction").GetComponent<Text>();
        var timing = root.Find("Caption/Timing").GetComponent<Text>();
        ScreenCapture.CaptureScreenshot(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            "Logs/Localization/" + name + ".png"));
        return instruction.text + " | " + timing.text;
    }

    public static string CheckTransitions()
    {
        var root = GameObject.Find("CurtainPlayerHUD").transform;
        view.PresentPlayerCue(true, new CurtainGameplayUI.PlayerCue {
            kind = CurtainGameplayUI.CueKind.Automatic, instruction = "等客人走近", timing = "自动演出" }, camera, anchor);
        if (root.Find("CurrentDragTarget").gameObject.activeSelf || root.Find("NextDragTarget").gameObject.activeSelf
            || root.Find("DirectionArrow").gameObject.activeSelf)
            throw new InvalidOperationException("Automatic stages leak directional previews.");
        view.PresentPlayerCue(false, new CurtainGameplayUI.PlayerCue(), camera, anchor);
        if (root.gameObject.activeSelf) throw new InvalidOperationException("Leaving possession did not hide the HUD.");
        return "Automatic stages hide route previews; leaving possession hides the entire HUD.";
    }

    public static void Finish()
    {
        if (prototype == null) return;
        prototype.GetType().GetField("curtainStep", Flags).SetValue(prototype, savedStep);
        prototype.enabled = true;
        prototype.GetType().GetMethod("ReturnToExplore", Flags).Invoke(prototype, null);
        prototype = null;
    }
}
