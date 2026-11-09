using Spine.Unity;
using UnityEngine;
using UnityEngine.AI;

/// <summary>NPC movement on the baked first-floor NavMesh. Roams when no scripted destination is active.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[DefaultExecutionOrder(100)]
public sealed class NpcNavMover : MonoBehaviour
{
    [Header("Destination")]
    [SerializeField] Transform followTarget;
    [SerializeField] Transform[] patrolPoints;
    [SerializeField] bool loopPatrol = true;
    [SerializeField, Min(0f)] float pauseAtPoint = 0.5f;
    [SerializeField, Min(0.05f)] float arrivalDistance = 0.25f;
    [SerializeField, Min(0.05f)] float repathInterval = 0.25f;

    [Header("Random Roaming")]
    [SerializeField] bool randomRoam = true;
    [SerializeField, Min(1f)] float roamRadius = 18f;
    [SerializeField, Min(0f)] float minimumWalkDistance = 3f;
    [SerializeField, Min(0f)] float minimumPause = 0.5f;
    [SerializeField, Min(0f)] float maximumPause = 1.5f;

    [Header("Animation")]
    [SerializeField] string idleAnimation = "idle";
    [SerializeField] string walkAnimation = "walk";
    [SerializeField, Min(0f)] float walkSpeedThreshold = 0.08f;

    NavMeshAgent agent;
    PrankNpcActor actor;
    SkeletonAnimation spine;
    Spine.TrackEntry animationTrack;
    int syncedClockRevision = -1;
    int patrolIndex;
    float pauseUntil;
    float nextRepathAt;
    Vector3 lastDestination;
    Vector3 commandedDestination;
    bool hasDestination;
    bool walking;
    bool warnedOffMesh;
    bool paused;
    bool roamingToPoint;
    Vector3 roamDestination;
    float roamStartedAt;
    NavMeshPath candidatePath;
    Transform artwork;
    float artworkScaleX;
    Camera viewCamera;

    public NavMeshAgent Agent { get { return agent != null ? agent : (agent = GetComponent<NavMeshAgent>()); } }
    public bool IsMoving { get { return Agent.isOnNavMesh && Agent.velocity.sqrMagnitude > walkSpeedThreshold * walkSpeedThreshold; } }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        candidatePath = new NavMeshPath();
        agent.updateRotation = false; // Spine artwork faces the camera independently of path direction.
        actor = GetComponent<PrankNpcActor>();
        viewCamera = Camera.main;
    }

    void Start()
    {
        if (!agent.enabled) AttachToNavMesh();
    }

    void Update()
    {
        if (!agent.enabled) return;
        if (!agent.isOnNavMesh)
        {
            if (!warnedOffMesh)
            {
                Debug.LogWarning(name + " is not on the NPC NavMesh. Re-bake the floor or move this NPC onto it.", this);
                warnedOffMesh = true;
            }
            SetWalking(false);
            return;
        }
        warnedOffMesh = false;

        if (paused)
        {
            if (!agent.isStopped) agent.isStopped = true;
            SetWalking(false);
            return;
        }

        if (followTarget != null)
        {
            RequestDestination(followTarget.position);
        }
        else if (hasDestination)
        {
            RequestDestination(commandedDestination);
            if (Arrived(commandedDestination)) { hasDestination = false; agent.isStopped = true; }
        }
        else if (patrolPoints != null && patrolPoints.Length > 0)
        {
            UpdatePatrol();
        }
        else if (randomRoam)
        {
            UpdateRoaming();
        }
        else if (!agent.isStopped)
        {
            agent.isStopped = true;
        }

        Vector3 velocity = agent.velocity;
        bool isWalking = !agent.isStopped && velocity.sqrMagnitude > walkSpeedThreshold * walkSpeedThreshold;
        SetWalking(isWalking);
        if (isWalking) FaceMovement(velocity);
    }

    void UpdateRoaming()
    {
        if (roamingToPoint)
        {
            RequestDestination(roamDestination);
            if (Arrived(roamDestination) ||
                (Time.time > roamStartedAt + 1f && !agent.pathPending &&
                 agent.pathStatus != NavMeshPathStatus.PathComplete))
            {
                roamingToPoint = false;
                agent.isStopped = true;
                pauseUntil = Time.time + Random.Range(minimumPause, Mathf.Max(minimumPause, maximumPause));
            }
            return;
        }
        if (Time.time < pauseUntil) { agent.isStopped = true; return; }
        if (!PickRoamDestination(out roamDestination))
        {
            pauseUntil = Time.time + 1f;
            agent.isStopped = true;
            return;
        }
        roamingToPoint = true;
        roamStartedAt = Time.time;
        nextRepathAt = 0f;
        RequestDestination(roamDestination);
    }

    bool PickRoamDestination(out Vector3 destination)
    {
        destination = transform.position;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * roamRadius;
            if (offset.sqrMagnitude < minimumWalkDistance * minimumWalkDistance) continue;
            Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 2f, agent.areaMask)) continue;
            Vector3 travel = hit.position - transform.position;
            travel.y = 0f;
            if (travel.sqrMagnitude < minimumWalkDistance * minimumWalkDistance) continue;
            if (!NavMesh.CalculatePath(agent.nextPosition, hit.position, agent.areaMask, candidatePath) ||
                candidatePath.status != NavMeshPathStatus.PathComplete) continue;
            destination = hit.position;
            return true;
        }
        return false;
    }

    void UpdatePatrol()
    {
        if (patrolIndex >= patrolPoints.Length) { agent.isStopped = true; return; }
        if (Time.time < pauseUntil) { agent.isStopped = true; return; }
        Transform point = patrolPoints[patrolIndex];
        if (point == null) { AdvancePatrol(); return; }
        RequestDestination(point.position);
        if (Arrived(point.position))
        {
            agent.isStopped = true;
            pauseUntil = Time.time + pauseAtPoint;
            AdvancePatrol();
        }
    }

    void AdvancePatrol()
    {
        patrolIndex++;
        if (loopPatrol && patrolIndex >= patrolPoints.Length) patrolIndex = 0;
    }

    bool Arrived(Vector3 destination)
    {
        if (agent.pathPending) return false;
        if (agent.hasPath)
            return agent.remainingDistance <= Mathf.Max(arrivalDistance, agent.stoppingDistance);
        Vector3 offset = destination - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= arrivalDistance * arrivalDistance;
    }

    void RequestDestination(Vector3 worldPosition)
    {
        if ((worldPosition - lastDestination).sqrMagnitude < 0.01f &&
            (agent.pathPending || agent.hasPath || Arrived(worldPosition)))
        {
            if (agent.isStopped && agent.hasPath) agent.isStopped = false;
            return;
        }
        if (Time.time < nextRepathAt) return;
        if (agent.SetDestination(worldPosition))
        {
            agent.isStopped = false;
            lastDestination = worldPosition;
            nextRepathAt = Time.time + repathInterval;
        }
    }

    /// <summary>One-off destination. FollowTarget in the Inspector takes priority when assigned.</summary>
    public void MoveTo(Vector3 worldPosition)
    {
        paused = false;
        roamingToPoint = false;
        commandedDestination = worldPosition;
        hasDestination = true;
        nextRepathAt = 0f;
    }

    public void Stop()
    {
        paused = true;
        roamingToPoint = false;
        hasDestination = false;
        followTarget = null;
        if (Agent.isOnNavMesh) { Agent.ResetPath(); Agent.isStopped = true; }
        SetWalking(false);
    }

    public void Resume() { paused = false; }

    public void SetRandomRoam(bool value) { randomRoam = value; }
    public void SetRoamRadius(float value) { roamRadius = Mathf.Max(1f, value); }

    /// <summary>Lets a scripted encounter move this transform directly without fighting the agent.</summary>
    public void SuspendNavigation()
    {
        Stop();
        Agent.enabled = false;
    }

    public void ResumeNavigation()
    {
        AttachToNavMesh();
        paused = false;
        roamingToPoint = false;
        pauseUntil = Time.time + Random.Range(minimumPause, Mathf.Max(minimumPause, maximumPause));
    }

    bool AttachToNavMesh()
    {
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(transform.position, out hit, 4f, Agent.areaMask)) return false;
        if (Agent.enabled) Agent.enabled = false;
        transform.position = hit.position + Vector3.up * Agent.baseOffset;
        Agent.enabled = true;
        return Agent.isOnNavMesh;
    }

    void FaceMovement(Vector3 velocity)
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return;
        float screenDirection = Vector3.Dot(velocity, viewCamera.transform.right);
        if (Mathf.Abs(screenDirection) < 0.03f) return;
        bool facingLeft = screenDirection < 0f;
        if (actor != null) { actor.SetFacingLeft(facingLeft); return; }
        if (spine == null) spine = GetComponentInChildren<SkeletonAnimation>();
        if (spine == null) return;
        if (artwork != spine.transform)
        {
            artwork = spine.transform;
            artworkScaleX = Mathf.Abs(artwork.localScale.x);
        }
        Vector3 scale = artwork.localScale;
        scale.x = artworkScaleX * (facingLeft ? -1f : 1f);
        artwork.localScale = scale;
    }

    void SetWalking(bool value)
    {
        if (walking == value) return;
        walking = value;
        if (actor != null) { actor.SetWalking(value); return; }
        if (spine == null) spine = GetComponentInChildren<SkeletonAnimation>();
        if (spine == null || !spine.IsValid) return;
        string animationName = value ? walkAnimation : idleAnimation;
        if (spine.Skeleton.Data.FindAnimation(animationName) != null)
        {
            animationTrack = spine.AnimationState.SetAnimation(0, animationName, true);
            syncedClockRevision = -1;
            AlignAnimationToBeat();
        }
    }

    void LateUpdate()
    {
        if (actor == null && animationTrack != null &&
            syncedClockRevision != RhythmAccent.ClockRevision) AlignAnimationToBeat();
    }

    void AlignAnimationToBeat()
    {
        if (animationTrack == null ||
            !RhythmAccent.TryGetLoopTime(animationTrack.Animation.Duration, out float trackTime)) return;
        animationTrack.TrackTime = trackTime;
        syncedClockRevision = RhythmAccent.ClockRevision;
    }
}
