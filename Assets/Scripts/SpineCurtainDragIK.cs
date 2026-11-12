using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SampleScene-only Spine curtain drag prototype. Attach this component to
/// "Curtain Drag IK Preview" with Curtain_SkeletonData; it creates its own
/// SkeletonAnimation child at runtime. It does not replace Level01's curtain.
///
/// Handoff for the rhythm/prank integration:
/// - LevelCurtainEncounter starts the existing GhostPartyPrototype sequence.
///   GhostPartyPrototype.HandleCurtainSequence owns gesture/beat judgement;
///   UpdatePrankVisuals still poses the old cube curtain. Replace that visual
///   path there, rather than adding a second rhythm judge in this component.
/// - This prototype alone reads Mouse.current and starts a drag only near the
///   middle hem tip. Route the gameplay's press/hold/release and pointer to
///   this solver when integrating, so one input stream drives both the pose
///   and beat judgement. Gamepad-driven targets are not implemented here.
/// - Pointer projection is camera-ray/curtain-plane based (local Spine XY).
///   The prank's gesture thresholds currently use screen-space mouse delta;
///   keep that distinction when connecting the two systems.
/// - Bone1/2/3 have separate bend limits, increasing down the curtain.
///   Bone4's origin is the pinned lower-chain pivot; Bone4..8 use original
///   lengths. A held tip follows the target
///   directly within reach; releaseSpring/releaseDamping return it to rest.
///   Projected gravity adds a small, delayed sag to the inner guide nodes and
///   assists the tip's fall after release. It never moves the held tip.
/// - The current export has one mesh. Drag_Ctrl/L_Ctrl/R_Ctrl have zero mesh
///   weights, so extreme folds can overlap black outlines and the visible
///   hem is not guaranteed to coincide with the solved M_Bone8 tip. This is
///   an art/rigging limitation still open after the SampleScene test.
/// - OnGUI's plus marker and F6 sweep are SampleScene debug aids only.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class SpineCurtainDragIK : MonoBehaviour
{
    [SerializeField, Tooltip("窗帘的 Spine 骨架资源；缺少它时样机无法显示。")]
    private SkeletonDataAsset skeletonDataAsset;
    [SerializeField, Tooltip("左侧骨骼链的名称前缀，对应 Spine 中的 L_Bone 系列。")]
    private string leftRoot = "L_Bone";
    [SerializeField, Tooltip("中间骨骼链的名称前缀，对应 Spine 中的 M_Bone 系列。")]
    private string middleRoot = "M_Bone";
    [SerializeField, Tooltip("右侧骨骼链的名称前缀，对应 Spine 中的 R_Bone 系列。")]
    private string rightRoot = "R_Bone";
    [SerializeField, Min(0.1f), Tooltip("鼠标按下时，距离窗帘下端抓取点多近才能抓住；单位为窗帘局部坐标。越大越容易抓。")]
    private float grabRadius = 0.48f;
    [SerializeField, Range(4, 64), Tooltip("每帧校正骨骼长度的次数。越高越不容易拉伸，但计算量也越大。")]
    private int solverIterations = 48;
    [SerializeField, Range(0f, 2f), Tooltip("拖动时向左或向右鼓起的倾向。越大，窗帘下半段越往侧面弯。")]
    private float bendHint = 0.65f;
    [SerializeField, Range(0f, 3f), Tooltip("拖动时中段向下弯成 U 形的倾向。越大，中段越往下垂。")]
    private float elbowDownBias = 2f;
    [SerializeField, Range(0.1f, 0.6f), Tooltip("左右折叠方向在中间切换的宽度。越大，换向越平缓；越小，换向越快。")]
    private float sideSwitchWidth = 0.3f;
    [SerializeField, Range(2f, 20f), Tooltip("松开鼠标后回到垂落位置的弹力。越大，回位越快。")]
    private float releaseSpring = 7f;
    [SerializeField, Range(0.1f, 1f), Tooltip("松手回位时的阻尼。越大，来回摆动越少、越快停稳。")]
    private float releaseDamping = 0.36f;
    [SerializeField, Range(0f, 20f), Tooltip("重力强度。增加后，下半段的下垂更明显，松手下落也稍快；设为 0 可关闭新增的重力效果。")]
    private float gravityAcceleration = 8f;
    [SerializeField, Range(0f, 0.5f), Tooltip("拖动或松手产生的余波最大强度。越大，尾部摆动越明显。")]
    private float rippleStrength = 0.28f;
    [SerializeField, Range(0.5f, 10f), Tooltip("余波每秒摆动的次数。越大，摆动越快。")]
    private float rippleFrequency = 3.5f;
    [SerializeField, Range(0.1f, 12f), Tooltip("余波消失的速度。越大，摆动越快停下。")]
    private float rippleDecay = 2f;
    [SerializeField, Range(0f, 15f), Tooltip("最上方 Bone1 的最大附加转角，单位为度。默认 0.5°，几乎不晃；设为 0 就固定。")]
    private float bone1BendDegrees = 0.5f;
    [SerializeField, Range(0f, 20f), Tooltip("Bone2 的最大附加转角，单位为度。建议比 Bone1 大，让弯曲从顶部逐渐增加。")]
    private float bone2BendDegrees = 4f;
    [SerializeField, Range(0f, 30f), Tooltip("Bone3 的最大附加转角，单位为度。建议比 Bone2 大；Bone4 以下由自由求解接续弯曲。")]
    private float bone3BendDegrees = 10f;

    private const int FirstMovingBone = 3; // Bone4 begins the unrestricted lower chain.
    private const int MovingBones = 5;
    private readonly Bone[,] chains = new Bone[3, 8];
    private readonly Vector2[] restNodes = new Vector2[MovingBones + 1];
    private readonly Vector2[] nodes = new Vector2[MovingBones + 1];
    private readonly float[] lengths = new float[MovingBones];
    private readonly float[] restWorldAngles = new float[MovingBones];
    private readonly float[] worldAngleDeltas = new float[MovingBones];
    private readonly float[] visualAngles = new float[MovingBones];
    private readonly Vector2[] guideNodes = new Vector2[MovingBones + 1];

    private SkeletonAnimation spineAnimation;
    private Skeleton skeleton;
    private Camera inputCamera;
    private Vector2 restHem;
    private Vector2 target;
    private Vector2 targetVelocity;
    private Vector2 pivot;
    private Vector2 elbowPole;
    private float maxReach;
    private float restUpperWorldAngle;
    private float rippleAmplitude;
    private float ripplePhase;
    private float gravitySag;
    private float gravitySagVelocity;
    private float poleSide = 1f;
    private int lastBendSide = 1;
    private bool dragging;
    private bool ready;
#if UNITY_EDITOR
    private bool autoSweep;
    private float autoSweepTime;
    private float autoSweepMaxError;
    private float autoSweepMaxTurn;
    private readonly float[] autoSweepPreviousAngles = new float[MovingBones];
#endif

    private void Start()
    {
        if (skeletonDataAsset == null)
        {
            Debug.LogError("Curtain IK needs Curtain_SkeletonData.", this);
            return;
        }

        var components = SkeletonAnimation.NewSkeletonAnimationGameObject(skeletonDataAsset);
        spineAnimation = components.skeletonAnimation;
        spineAnimation.gameObject.name = "Curtain Spine (drag IK)";
        spineAnimation.transform.SetParent(transform, false);

        skeleton = components.skeletonRenderer.Skeleton;
        string[] roots = { leftRoot, middleRoot, rightRoot };
        for (int column = 0; column < 3; column++)
        {
            for (int row = 0; row < 8; row++)
            {
                string name = roots[column] + (row == 0 ? "" : (row + 1).ToString());
                chains[column, row] = skeleton.FindBone(name);
                if (chains[column, row] != null) continue;

                Debug.LogError("Curtain IK cannot find Spine bone: " + name, this);
                Destroy(spineAnimation.gameObject);
                spineAnimation = null;
                return;
            }
        }

        skeleton.UpdateWorldTransform(Spine.Physics.None);
        for (int i = 0; i < MovingBones; i++)
        {
            Bone bone = chains[1, FirstMovingBone + i];
            restNodes[i] = new Vector2(bone.AppliedPose.WorldX, bone.AppliedPose.WorldY);
            restWorldAngles[i] = bone.AppliedPose.WorldRotationX;
        }

        Bone tipBone = chains[1, 7];
        tipBone.AppliedPose.LocalToWorld(tipBone.Data.Length, 0f, out float tipX, out float tipY);
        restNodes[MovingBones] = new Vector2(tipX, tipY);
        for (int i = 0; i < MovingBones; i++)
        {
            lengths[i] = Vector2.Distance(restNodes[i], restNodes[i + 1]);
            maxReach += lengths[i];
        }

        for (int i = 0; i <= MovingBones; i++) nodes[i] = restNodes[i];
        visualAngles[0] = restWorldAngles[0] * Mathf.Deg2Rad;
        for (int i = 1; i < MovingBones; i++)
        {
            visualAngles[i] = Mathf.DeltaAngle(restWorldAngles[i - 1], restWorldAngles[i]) * Mathf.Deg2Rad;
        }
        pivot = restNodes[0];
        elbowPole = (pivot + restNodes[MovingBones]) * 0.5f;
        restUpperWorldAngle = chains[1, FirstMovingBone - 1].AppliedPose.WorldRotationX;
        restHem = restNodes[MovingBones];
        target = restHem;
        inputCamera = Camera.main;
        ready = true;
        spineAnimation.UpdateLocal += ApplyIK;
    }

    private void OnDestroy()
    {
        if (spineAnimation != null) spineAnimation.UpdateLocal -= ApplyIK;
    }

    private void Update()
    {
        if (!ready || Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame && TryGetMouseOnCurtain(out Vector2 press))
        {
            if (Vector2.Distance(press, nodes[MovingBones]) <= grabRadius)
            {
                dragging = true;
            }
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            dragging = false;
            rippleAmplitude = Mathf.Max(rippleAmplitude,
                Mathf.Min(rippleStrength, targetVelocity.magnitude * 0.08f));
        }

        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        if (dt <= 0f) return;

#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
        {
            autoSweep = !autoSweep;
            autoSweepTime = 0f;
            autoSweepMaxError = 0f;
            autoSweepMaxTurn = 0f;
            dragging = autoSweep;
            targetVelocity = Vector2.zero;
            for (int i = 0; i < MovingBones; i++) autoSweepPreviousAngles[i] = visualAngles[i];
        }
        if (autoSweep)
        {
            autoSweepTime += dt;
            Vector2 previous = target;
            target = ClampToReach(pivot + new Vector2(
                Mathf.Sin(autoSweepTime * 4f) * maxReach * 0.7f,
                maxReach * 0.25f));
            targetVelocity = Vector2.ClampMagnitude((target - previous) / dt, 18f);
            UpdatePoleSide(dt);
            return;
        }
#endif

        Vector2 desired = dragging ? target : restHem;
        if (dragging && Mouse.current.leftButton.isPressed && TryGetMouseOnCurtain(out Vector2 mouse))
        {
            desired = ClampToReach(mouse);
        }

        Vector2 previousVelocity = targetVelocity;
        if (dragging)
        {
            // While held, the grab point is an input constraint, not a spring.
            // A spring would make a fast horizontal swipe trace a sagging arc.
            Vector2 previousTarget = target;
            target = desired;
            targetVelocity = Vector2.ClampMagnitude((target - previousTarget) / dt, 18f);
        }
        else
        {
            targetVelocity += ((desired - target) * (releaseSpring * releaseSpring)
                - targetVelocity * (2f * releaseDamping * releaseSpring)
                + GetPlanarGravity() * gravityAcceleration * 0.35f) * dt;
            targetVelocity = Vector2.ClampMagnitude(targetVelocity, 18f);
            target += targetVelocity * dt;
        }
        Vector2 constrained = ClampToReach(target);
        if (constrained != target)
        {
            Vector2 outward = (target - pivot).normalized;
            float outwardSpeed = Vector2.Dot(targetVelocity, outward);
            if (outwardSpeed > 0f) targetVelocity -= outward * outwardSpeed;
            target = constrained;
        }

        UpdatePoleSide(dt);

        float acceleration = (targetVelocity - previousVelocity).magnitude;
        rippleAmplitude = Mathf.Min(rippleStrength, rippleAmplitude + acceleration * dt * 0.025f);
        rippleAmplitude *= Mathf.Exp(-rippleDecay * dt);
        ripplePhase += Mathf.PI * 2f * rippleFrequency * dt;
    }

    private bool TryGetMouseOnCurtain(out Vector2 local)
    {
        // Integration seam: replace Mouse.current with the shared gameplay
        // pointer, but keep this ray/plane conversion for a rotated curtain.
        local = default;
        if (inputCamera == null) inputCamera = Camera.main;
        if (inputCamera == null) return false;

        Plane plane = new Plane(spineAnimation.transform.forward, spineAnimation.transform.position);
        Ray ray = inputCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!plane.Raycast(ray, out float distance)) return false;

        Vector3 point = spineAnimation.transform.InverseTransformPoint(ray.GetPoint(distance));
        local = new Vector2(point.x, point.y);
        return true;
    }

    private Vector2 ClampToReach(Vector2 desired)
    {
        Vector2 fromPivot = desired - pivot;
        return fromPivot.magnitude > maxReach
            ? pivot + fromPivot.normalized * maxReach : desired;
    }

    private Vector2 GetPlanarGravity()
    {
        // InverseTransformDirection projects world gravity onto the curtain's
        // own XY plane, so a rotated curtain still sags in the right direction.
        Vector3 down = spineAnimation.transform.InverseTransformDirection(Vector3.down);
        return new Vector2(down.x, down.y);
    }

    private void UpdatePoleSide(float dt)
    {
        // A continuous pole direction avoids a branch change at the centre.
        // Near the middle, small mouse noise only causes a small bend change.
        float width = Mathf.Max(0.01f, maxReach * sideSwitchWidth);
        float horizontal = target.x - restHem.x;
        float wanted = Mathf.Clamp(horizontal / width, -1f, 1f);
        if (Mathf.Abs(horizontal) > width * 0.1f)
            lastBendSide = horizontal > 0f ? 1 : -1;
        if (Mathf.Abs(horizontal) < width * 0.1f
            && Mathf.Abs(targetVelocity.x) < 0.05f)
            wanted = lastBendSide;
        poleSide = Mathf.MoveTowards(poleSide, wanted, 8f * dt);
    }

    private void ApplyIK(ISkeletonRenderer renderer)
    {
        // Keep the solver here: SkeletonAnimation invokes UpdateLocal after
        // applying animation locals, before its final world transform/mesh.
        if (!ready) return;
        ApplySmallUpperBend();
        // Refresh the actual Bone3 origin after the two long bones turn.
        // The renderer will run its normal world update again after this callback.
        skeleton.UpdateWorldTransform(Spine.Physics.None);
        Bone middlePivotBone = chains[1, FirstMovingBone];
        pivot = new Vector2(middlePivotBone.AppliedPose.WorldX, middlePivotBone.AppliedPose.WorldY);
        float upperWorldDelta = Mathf.DeltaAngle(restUpperWorldAngle,
            chains[1, FirstMovingBone - 1].AppliedPose.WorldRotationX);
        SolveLowerChainSmooth(ClampToReach(target));
#if UNITY_EDITOR
        if (autoSweep)
        {
            if (autoSweepTime > 1f)
                autoSweepMaxError = Mathf.Max(autoSweepMaxError,
                    Vector2.Distance(nodes[MovingBones], ClampToReach(target)));
            for (int i = 0; i < MovingBones; i++)
            {
                if (autoSweepTime > 1f)
                    autoSweepMaxTurn = Mathf.Max(autoSweepMaxTurn, Mathf.Abs(Mathf.DeltaAngle(
                        autoSweepPreviousAngles[i] * Mathf.Rad2Deg,
                        visualAngles[i] * Mathf.Rad2Deg)));
                autoSweepPreviousAngles[i] = visualAngles[i];
            }
        }
#endif

        for (int i = 0; i < MovingBones; i++)
        {
            Vector2 direction = nodes[i + 1] - nodes[i];
            float desiredAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            worldAngleDeltas[i] = Mathf.DeltaAngle(restWorldAngles[i], desiredAngle);
        }

        for (int i = 0; i < MovingBones; i++)
        {
            float localDelta = i == 0 ? Mathf.DeltaAngle(upperWorldDelta, worldAngleDeltas[i])
                : Mathf.DeltaAngle(worldAngleDeltas[i - 1], worldAngleDeltas[i]);
            for (int column = 0; column < 3; column++)
            {
                Bone bone = chains[column, FirstMovingBone + i];
                bone.Pose.Rotation = bone.Data.GetSetupPose().Rotation + localDelta;
            }
        }
    }

    private void ApplySmallUpperBend()
    {
        float horizontal = Mathf.Clamp((target.x - restHem.x) / maxReach, -1f, 1f);
        float lift = Mathf.Clamp01((target.y - restHem.y) / maxReach);
        float turn = Mathf.Clamp(horizontal + poleSide * lift * 0.18f, -1f, 1f);
        for (int column = 0; column < 3; column++)
        {
            for (int boneIndex = 0; boneIndex < FirstMovingBone; boneIndex++)
            {
                Bone bone = chains[column, boneIndex];
                float bendLimit = boneIndex == 0 ? bone1BendDegrees
                    : boneIndex == 1 ? bone2BendDegrees : bone3BendDegrees;
                bone.Pose.Rotation = bone.Data.GetSetupPose().Rotation
                    + turn * bendLimit;
            }
        }
    }

    private void SolveLowerChainSmooth(Vector2 goal)
    {
        // Keep the previous frame's nodes, pin the two ends, and project the
        // six original segment lengths. This has one continuous pose state:
        // no candidate U branch or final CCD correction can suddenly win.
        Vector2 span = goal - pivot;
        float distance = span.magnitude;
        float slack = Mathf.Max(0f, maxReach - distance);
        Vector2 planarGravity = GetPlanarGravity();
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        // A damped gravity displacement supplies weight without putting the
        // endpoint on a spring. Available chain slack caps the displacement.
        gravitySagVelocity += (gravityAcceleration * planarGravity.magnitude
            - gravitySag * 36f - gravitySagVelocity * 9.6f) * dt;
        gravitySag += gravitySagVelocity * dt;
        float sagLimit = slack * 0.4f;
        if (gravitySag > sagLimit)
        {
            gravitySag = sagLimit;
            if (gravitySagVelocity > 0f) gravitySagVelocity = 0f;
        }
        if (gravitySag < 0f)
        {
            gravitySag = 0f;
            if (gravitySagVelocity < 0f) gravitySagVelocity = 0f;
        }
        if (distance >= maxReach - 0.001f)
        {
            Vector2 direction = distance > 0.0001f ? span / distance : Vector2.down;
            nodes[0] = pivot;
            for (int i = 0; i < MovingBones; i++)
                nodes[i + 1] = nodes[i] + direction * lengths[i];
            RecordVisualAngles();
            return;
        }

        float crossing = 1f - Mathf.Abs(poleSide);
        Vector2 bulge = new Vector2(
            poleSide * slack * (0.6f + bendHint * 0.4f),
            -slack * elbowDownBias * (0.35f + crossing * 0.45f));
        float blend = 1f - Mathf.Exp(-14f * Mathf.Min(Time.deltaTime, 0.033f));
        float traveled = 0f;
        guideNodes[0] = pivot;
        guideNodes[MovingBones] = goal;
        for (int i = 1; i < MovingBones; i++)
        {
            traveled += lengths[i - 1];
            float t = traveled / maxReach;
            float wave = dragging ? 0f : rippleAmplitude * 0.12f
                * Mathf.Sin(ripplePhase - i * 0.65f);
            guideNodes[i] = Vector2.Lerp(pivot, goal, t)
                + Mathf.Sin(Mathf.PI * t) * bulge
                + Mathf.Sin(Mathf.PI * t) * planarGravity.normalized * gravitySag
                + new Vector2(wave, 0f);
            nodes[i] = Vector2.Lerp(nodes[i], guideNodes[i], blend);
        }

        for (int iteration = 0; iteration < solverIterations; iteration++)
        {
            nodes[MovingBones] = goal;
            for (int i = MovingBones - 1; i >= 0; i--)
            {
                Vector2 delta = nodes[i] - nodes[i + 1];
                if (delta.sqrMagnitude < 0.000001f)
                    delta = guideNodes[i] - guideNodes[i + 1];
                nodes[i] = nodes[i + 1] + delta.normalized * lengths[i];
            }

            nodes[0] = pivot;
            for (int i = 0; i < MovingBones; i++)
            {
                Vector2 delta = nodes[i + 1] - nodes[i];
                if (delta.sqrMagnitude < 0.000001f)
                    delta = guideNodes[i + 1] - guideNodes[i];
                nodes[i + 1] = nodes[i] + delta.normalized * lengths[i];
            }
            if ((nodes[MovingBones] - goal).sqrMagnitude < 0.000025f) break;
        }

        RecordVisualAngles();
        elbowPole = nodes[3];
    }

    private void RecordVisualAngles()
    {
        float previousDirection = 0f;
        for (int i = 0; i < MovingBones; i++)
        {
            Vector2 segment = nodes[i + 1] - nodes[i];
            float direction = Mathf.Atan2(segment.y, segment.x);
            visualAngles[i] = i == 0 ? direction
                : Mathf.DeltaAngle(previousDirection * Mathf.Rad2Deg,
                    direction * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            previousDirection = direction;
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !ready || inputCamera == null) return;

        Rect panel = new Rect(Screen.width - 266, 16, 250, 86);
        GUI.Box(panel, "Curtain drag IK");
        GUI.Label(new Rect(panel.x + 12, panel.y + 24, 225, 20),
            "Drag the + at the lower middle");
        GUI.Label(new Rect(panel.x + 12, panel.y + 44, 225, 20),
            "Pivot: Bone4 | graded upper bend");
#if UNITY_EDITOR
        GUI.Label(new Rect(panel.x + 12, panel.y + 64, 225, 20),
            autoSweep ? $"F6 sweep: error {autoSweepMaxError:F3}, turn {autoSweepMaxTurn:F1}"
                : "F6: automatic left/right sweep");
        if (autoSweep)
        {
            GUI.color = Color.magenta;
            for (int i = 0; i <= MovingBones; i++)
            {
                Vector3 world = spineAnimation.transform.TransformPoint(nodes[i]);
                Vector3 pixel = inputCamera.WorldToScreenPoint(world);
                if (pixel.z > 0f)
                    GUI.Box(new Rect(pixel.x - 3f, Screen.height - pixel.y - 3f, 6f, 6f), "");
            }
            GUI.color = Color.white;
        }
#endif

        Vector3 hemWorld = spineAnimation.transform.TransformPoint(new Vector3(nodes[MovingBones].x, nodes[MovingBones].y, 0f));
        Vector3 screen = inputCamera.WorldToScreenPoint(hemWorld);
        if (screen.z <= 0f) return;
        Color previousColor = GUI.color;
        GUI.color = dragging ? Color.cyan : Color.yellow;
        GUI.Box(new Rect(screen.x - 10f, Screen.height - screen.y - 10f, 20f, 20f), "+");
        GUI.color = previousColor;
    }

    private void OnDrawGizmosSelected()
    {
        if (!ready || spineAnimation == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(spineAnimation.transform.TransformPoint(pivot), maxReach);
        Gizmos.color = Color.yellow;
        for (int i = 0; i < MovingBones; i++)
        {
            Vector3 a = spineAnimation.transform.TransformPoint(nodes[i]);
            Vector3 b = spineAnimation.transform.TransformPoint(nodes[i + 1]);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(a, 0.025f);
        }
        Gizmos.DrawSphere(spineAnimation.transform.TransformPoint(nodes[MovingBones]), 0.04f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(spineAnimation.transform.TransformPoint(elbowPole), 0.06f);
    }
}
