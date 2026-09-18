using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Plans a NavMesh path to a configurable ring around a target, then translates
/// path corners into the vr/wr/vd commands already consumed by X02newAgent.
/// </summary>
public sealed class X02TargetNavigationController : MonoBehaviour
{
    enum FinalDockPhase
    {
        FaceStopPoint,
        DriveToStopPoint,
        BrakeAfterTranslationPulse,
        BrakeAtStopPoint,
        AlignFinalFacing,
        Verify
    }

    [Header("Robot")]
    public X02newAgent agent;
    public Transform robotBody;

    [Header("Target approach")]
    [Min(0.1f)] public float stopDistance = 0.30f;
    [Tooltip("Optional lateral shift of the final stop point. The current grasp test uses no lateral shift.")]
    [Min(0f)] public float stopLeftOffset = 0f;
    [Min(0.005f)] public float arrivalTolerance = 0.09f;
    [Min(0.005f)] public float finalLateralTolerance = 0.04f;
    [Min(0.1f)] public float finalBrakingDistance = 0.80f;
    [Min(0.05f)] public float alignmentStartDistance = 0.35f;
    [Min(0.05f)] public float handoffStopLeadDistance = 0.80f;
    [Min(0.01f)] public float handoffPhysicalStopSpeed = 0.04f;
    [Range(1f, 30f)] public float maxHandoffTiltDegrees = 12f;
    [Min(0f)] public float stopSettlingSeconds = 0.40f;
    [Min(0.1f)] public float facingToleranceDegrees = 5f;
    [Min(0.1f)] public float translationFacingToleranceDegrees = 8f;
    [Min(0f)] public float facingStableSeconds = 0.30f;
    [Range(0.05f, 1f)] public float maxFinalTurnSpeed = 0.42f;
    [Range(0.01f, 0.5f)] public float minFinalTurnSpeed = 0.15f;
    [Range(0.05f, 0.5f)] public float maxFaceStopTurnSpeed = 0.28f;
    [Range(0.03f, 0.3f)] public float maxFinalTranslationSpeed = 0.15f;
    [Range(0f, 0.25f)] public float minFinalTranslationSpeed = 0f;
    [Min(0.1f)] public float finalPositionGain = 0.9f;
    [Min(0f)] public float finalVelocityDamping = 0.65f;
    [Min(0.02f)] public float finalVelocityFilterSeconds = 0.15f;
    [Min(0.01f)] public float dockingCaptureTolerance = 0.20f;
    [Range(5f, 90f)] public float dockingForwardCutoffDegrees = 25f;
    [Min(0.01f)] public float dockingEffectiveDeceleration = 0.08f;
    [Min(0f)] public float dockingBrakingBuffer = 0.15f;
    [Min(0.01f)] public float dockingBrakeAcceleration = 0.60f;
    [Min(0.05f)] public float dockingBrakeResponseSeconds = 0.35f;
    [Min(0.05f)] public float dockingBrakeStableSeconds = 0.25f;
    [Range(0.05f, 1f)] public float terminalForwardPulseCommand = 0.15f;
    [Range(0.05f, 0.8f)] public float terminalLateralPulseCommand = 0.15f;
    [Min(0.05f)] public float terminalPulseMinSeconds = 0.20f;
    [Min(0.1f)] public float terminalPulseMaxSeconds = 0.55f;
    [Min(0.05f)] public float terminalPulseSettleSeconds = 0.10f;
    [Min(0.05f)] public float terminalPulseContinueError = 0.10f;
    [Range(0, 3)] public int terminalSupportPhaseToleranceSteps = 1;
    [Range(1f, 30f)] public float dockingPauseTiltDegrees = 6f;
    [Min(0.1f)] public float dockingTelemetryInterval = 0.50f;
    [Min(0.01f)] public float physicalStopLinearSpeed = 0.08f;
    [Min(0.01f)] public float physicalStopAngularSpeed = 0.15f;
    [Tooltip("Maximum NavMesh sampling offset around the pre-alignment point.")]
    [Min(0.005f)] public float navMeshSampleRadius = 0.15f;
    [Tooltip("Maximum correction used to move the green stop marker onto robot-safe NavMesh.")]
    [Min(0.01f)] public float finalStopNavMeshSampleRadius = 0.20f;
    [Min(0.005f)] public float preAlignmentReachTolerance = 0.015f;
    [Range(0f, 90f)] public float handoffMaxDeviationDegrees = 45f;
    [Range(1f, 45f)] public float candidateAngleStep = 15f;
    [Range(0f, 180f)] public float maxCandidateAngle = 45f;

    [Header("Path following")]
    [Min(0.05f)] public float lookAheadDistance = 0.08f;
    [Min(0.01f)] public float pathTolerance = 0.015f;
    [Min(0.02f)] public float correctionOnlyDistance = 0.03f;
    [Min(0.1f)] public float lateralCorrectionGain = 3.5f;
    [Range(0.05f, 0.8f)] public float maxLateralSpeed = 0.35f;
    [Min(0.05f)] public float cornerSlowDistance = 0.30f;
    [Tooltip("Start the short smoothing chord this far before an approximately 90-degree corner.")]
    [Min(0.03f)] public float rightAngleTurnLeadDistance = 0.10f;
    [Range(30f, 90f)] public float rightAngleTurnMinimumDegrees = 70f;
    [Range(90f, 150f)] public float rightAngleTurnMaximumDegrees = 110f;
    [Range(1f, 90f)] public float rotateOnlyAboveDegrees = 10f;
    [Range(0.05f, 0.8f)] public float maxForwardSpeed = 0.40f;
    [Range(0.05f, 1.5f)] public float maxTurnSpeed = 1.0f;
    [Min(0.01f)] public float forwardAcceleration = 0.45f;
    [Min(0.01f)] public float forwardDeceleration = 0.45f;
    [Min(0.01f)] public float turnAcceleration = 0.5f;

    [Header("Play-start retreat before first turn")]
    [Min(0.05f)] public float playStartRetreatDistance = 0.20f;
    [Range(0.4f, 1.0f)] public float playStartRetreatSpeed = 0.40f;
    [Min(0.05f)] public float playStartRetreatStableSeconds = 0.15f;

    [Header("Post-grasp return")]
    [Min(0.1f)] public float postGraspRetreatDistance = 0.50f;
    [Range(0.4f, 1.0f)] public float postGraspRetreatSpeed = 0.40f;
    [Tooltip("Command change per 0.01-second physics step. 0.01 exactly matches holding S in X02newAgent.")]
    [Min(0.001f)] public float postGraspRetreatCommandStep = 0.01f;
    [Min(0.05f)] public float postGraspRetreatSlowdownDistance = 0.20f;
    [Range(0.4f, 1.0f)] public float postGraspRetreatMinimumSpeed = 0.40f;
    [Min(0.5f)] public float postGraspRetreatProgressTimeout = 1.50f;
    [Min(0.005f)] public float postGraspRetreatTolerance = 0.02f;
    [Range(1f, 15f)] public float postGraspRetreatHeadingTolerance = 5f;
    [Min(0.1f)] public float postGraspRetreatStableSeconds = 0.15f;
    [Min(0.01f)] public float postGraspRetreatStopLinearSpeed = 0.08f;
    [Min(0.01f)] public float postGraspRetreatStopAngularSpeed = 0.15f;
    [Range(1f, 10f)] public float postGraspRetreatMaxStableTilt = 3f;
    [Range(0.05f, 0.8f)] public float postGraspReturnMaxForwardSpeed = 0.40f;
    [Range(0.05f, 0.8f)] public float postGraspReturnMaxLateralSpeed = 0.35f;
    [Range(0.1f, 1.5f)] public float postGraspReturnMinPureTurnSpeed = 0.60f;
    [Range(0.1f, 1.5f)] public float postGraspReturnMaxTurnSpeed = 1.0f;
    [Range(0f, 0.2f)] public float postGraspReturnTurnBackwardBias = 0f;
    [Min(0f)] public float postGraspReturnTurnPreloadSeconds = 0f;
    [Range(0f, 0.2f)] public float postGraspReturnMaxDynamicTurnBackward = 0.10f;
    [Min(0f)] public float postGraspReturnTurnVelocityCompensationGain = 0.50f;
    [Min(0f)] public float postGraspReturnTurnDriftCompensationGain = 0.60f;
    [Range(0f, 0.15f)] public float postGraspReturnMotionBackwardBias = 0f;
    [Range(0.05f, 0.5f)] public float postGraspReturnLinearAcceleration = 0.45f;
    [Range(0.05f, 0.5f)] public float postGraspReturnLinearDeceleration = 0.45f;
    [Range(0.05f, 0.5f)] public float postGraspReturnTurnAcceleration = 0.50f;

    [Header("Path visualization")]
    public bool showPlannedPath = false;
    public bool keepPathAfterArrival = true;
    [Min(0.005f)] public float pathLineWidth = 0.025f;
    [Min(0f)] public float pathHeightOffset = 0.03f;
    public Color pathColor = new Color(0f, 1f, 1f, 1f);
    public bool showStopMarker = false;
    [Min(0.03f)] public float stopMarkerRadius = 0.08f;
    public Color stopMarkerColor = new Color(0.1f, 1f, 0.1f, 0.9f);
    public bool showPreAlignmentMarker = false;
    [Min(0.03f)] public float preAlignmentMarkerRadius = 0.07f;
    public Color preAlignmentMarkerColor = new Color(1f, 0.55f, 0.05f, 0.95f);

    public bool IsNavigating { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public Vector3 StopPosition { get; private set; }
    public Vector3 DesiredStopFacing { get; private set; }
    public Vector3 PlayStartPosition { get; private set; }
    public Vector3 PlayStartFacing { get; private set; }
    public bool HasPlayStartPose { get; private set; }
    public bool IsReturningHome { get; private set; }

    public event Action Arrived;
    public event Action ReturnedHome;
    public event Action<string> NavigationFailed;

    NavMeshPath _path;
    int _cornerIndex;
    bool _savedKeyboardMode;
    bool _ownsKeyboardMode;
    LineRenderer _pathLine;
    LineRenderer _stopMarkerLine;
    LineRenderer _preAlignmentMarkerLine;
    Material _pathMaterial;
    Material _stopMarkerMaterial;
    Material _preAlignmentMarkerMaterial;
    ArticulationBody _robotRootBody;
    bool _hasPlannedPath;
    bool _isFinalizingStop;
    bool _handoffBrakingLogged;
    bool _positionDocked;
    FinalDockPhase _finalDockPhase;
    Vector3 _pathEndPosition;
    Vector3 _preAlignmentPosition;
    Vector3 _filteredFinalLocalVelocity;
    Vector3 _filteredStandstillLocalVelocity;
    float _filteredStandstillYawRate;
    Vector3 _filteredDockLocalError;
    int _translationAxis;
    bool _finalFacingBrakeLatched;
    float _brakeStableTimer;
    [SerializeField, HideInInspector] int runtimeConfigurationVersion;
    int _loggedTransitionCorner = -1;

    const int CurrentRuntimeConfigurationVersion = 85;
    float _facingStableTimer;
    float _nextDockTelemetryTime;
    bool _postGraspRetreatActive;
    Vector3 _postGraspRetreatStart;
    Vector3 _postGraspRetreatFacing;
    float _postGraspRetreatTravelled;
    float _postGraspRetreatStableTimer;
    float _postGraspRetreatLastProgress;
    float _postGraspRetreatLastProgressTime;
    bool _postGraspRetreatRecoveryLogged;
    float _nextReturnTelemetryTime;
    bool _playStartRetreatActive;
    bool _playStartRetreatCompleted;
    Vector3 _playStartRetreatStart;
    Vector3 _playStartRetreatFacing;
    float _playStartRetreatTravelled;
    float _playStartRetreatStableTimer;
    float _nextPlayStartRetreatTelemetryTime;
    Vector3 _pendingFirstTargetPosition;
    Vector3 _pendingFirstStopFacing;
    float _pendingFirstStopDistance;
    bool _pathRotateOnlyActive;
    bool _pathRotateOnlyBraking;
    bool _pathRotateOnlyBackwardPreloading;
    float _pathRotateOnlyBackwardPreloadTimer;
    Vector3 _pathRotateOnlyStartPosition;
    Vector3 _pathRotateOnlyStartFacing;
    float _nextPathRotationTelemetryTime;

    void Awake()
    {
        // Keep competition/runtime presentation clean even when an older scene
        // still has the visualization fields serialized as enabled.
        showPlannedPath = false;
        showStopMarker = false;
        showPreAlignmentMarker = false;

        if (runtimeConfigurationVersion < CurrentRuntimeConfigurationVersion)
        {
            ApplyCompetitionRuntimeDefaults();
            Debug.Log(
                "[ElderCareNavigation] Runtime configuration V86 loaded: terminal docking latches angular braking below 3 degrees and accepts the learned gait's stable 9-cm forward envelope instead of endlessly alternating turn and translation corrections.",
                this);
        }
        EnsurePath();
        AutoAssignRobot();
        CapturePlayStartPose();
    }

    public void ApplyCompetitionRuntimeDefaults()
    {
        stopDistance = 0.30f;
        stopLeftOffset = 0f;
        arrivalTolerance = 0.09f;
        finalLateralTolerance = 0.04f;
        finalBrakingDistance = 0.80f;
        alignmentStartDistance = 0.35f;
        handoffStopLeadDistance = 0.80f;
        handoffPhysicalStopSpeed = 0.04f;
        maxHandoffTiltDegrees = 12f;
        stopSettlingSeconds = 0.40f;
        facingToleranceDegrees = 5f;
        translationFacingToleranceDegrees = 8f;
        facingStableSeconds = 0.30f;
        maxFinalTurnSpeed = 0.42f;
        minFinalTurnSpeed = 0.15f;
        maxFaceStopTurnSpeed = 0.28f;
        maxFinalTranslationSpeed = 0.15f;
        minFinalTranslationSpeed = 0f;
        finalPositionGain = 0.9f;
        finalVelocityDamping = 0.65f;
        finalVelocityFilterSeconds = 0.15f;
        dockingCaptureTolerance = 0.20f;
        dockingForwardCutoffDegrees = 25f;
        dockingEffectiveDeceleration = 0.08f;
        dockingBrakingBuffer = 0.15f;
        dockingBrakeAcceleration = 0.60f;
        dockingBrakeResponseSeconds = 0.35f;
        dockingBrakeStableSeconds = 0.25f;
        // Retained for serialized-scene compatibility. V84 terminal docking
        // no longer emits fixed pulses; it uses maxFinalTranslationSpeed as
        // the ceiling of a distance/velocity closed-loop command.
        terminalForwardPulseCommand = 0.15f;
        terminalLateralPulseCommand = 0.15f;
        terminalPulseMinSeconds = 0.20f;
        terminalPulseMaxSeconds = 0.55f;
        terminalPulseSettleSeconds = 0.10f;
        terminalPulseContinueError = 0.10f;
        terminalSupportPhaseToleranceSteps = 1;
        dockingPauseTiltDegrees = 6f;
        dockingTelemetryInterval = 0.50f;
        physicalStopLinearSpeed = 0.08f;
        physicalStopAngularSpeed = 0.15f;
        navMeshSampleRadius = 0.15f;
        finalStopNavMeshSampleRadius = 0.20f;
        preAlignmentReachTolerance = 0.015f;
        handoffMaxDeviationDegrees = 45f;
        rightAngleTurnLeadDistance = 0.10f;
        rightAngleTurnMinimumDegrees = 70f;
        rightAngleTurnMaximumDegrees = 110f;
        forwardDeceleration = 0.45f;
        turnAcceleration = 0.5f;
        playStartRetreatDistance = 0.20f;
        playStartRetreatSpeed = 0.40f;
        playStartRetreatStableSeconds = 0.15f;
        postGraspRetreatSpeed = 0.40f;
        postGraspRetreatCommandStep = 0.01f;
        postGraspRetreatSlowdownDistance = 0.20f;
        postGraspRetreatMinimumSpeed = 0.40f;
        postGraspRetreatProgressTimeout = 1.50f;
        postGraspRetreatStableSeconds = 0.15f;
        postGraspRetreatStopLinearSpeed = 0.08f;
        postGraspRetreatStopAngularSpeed = 0.15f;
        postGraspRetreatMaxStableTilt = 3f;
        postGraspReturnMaxForwardSpeed = maxForwardSpeed;
        postGraspReturnMaxLateralSpeed = maxLateralSpeed;
        postGraspReturnMinPureTurnSpeed = 0.60f;
        postGraspReturnMaxTurnSpeed = maxTurnSpeed;
        postGraspReturnTurnBackwardBias = 0f;
        postGraspReturnTurnPreloadSeconds = 0f;
        postGraspReturnMaxDynamicTurnBackward = 0.10f;
        postGraspReturnTurnVelocityCompensationGain = 0.50f;
        postGraspReturnTurnDriftCompensationGain = 0.60f;
        postGraspReturnMotionBackwardBias = 0f;
        postGraspReturnLinearAcceleration = forwardAcceleration;
        postGraspReturnLinearDeceleration = forwardDeceleration;
        postGraspReturnTurnAcceleration = turnAcceleration;
        postGraspRetreatHeadingTolerance = 6f;
        runtimeConfigurationVersion = CurrentRuntimeConfigurationVersion;
    }

    public bool NavigateToTarget(Vector3 targetWorldPosition)
    {
        return NavigateToTarget(targetWorldPosition, Vector3.zero);
    }

    public bool NavigateToTarget(Vector3 targetWorldPosition, Vector3 desiredStopFacing)
    {
        return NavigateToTarget(targetWorldPosition, desiredStopFacing, stopDistance);
    }

    public bool NavigateToTarget(
        Vector3 targetWorldPosition,
        Vector3 desiredStopFacing,
        float requestedStopDistance)
    {
        EnsurePath();
        AutoAssignRobot();
        if (agent == null || robotBody == null)
            return Fail("X02newAgent or robotBody is not assigned.");

        if (!_playStartRetreatCompleted)
            return BeginPlayStartRetreat(
                targetWorldPosition,
                desiredStopFacing,
                requestedStopDistance);

        float finalRequestedStopDistance = Mathf.Max(0.10f, requestedStopDistance);
        // The NavMesh ends at the validated 30-cm table-front corridor. A
        // closer manipulation stop is reached only by the existing local
        // continuous-docking controller after the NavMesh handoff.
        float navMeshPlanningStopDistance = Mathf.Max(
            0.30f,
            finalRequestedStopDistance);
        stopDistance = navMeshPlanningStopDistance;

        TargetPosition = targetWorldPosition;
        DesiredStopFacing = Vector3.ProjectOnPlane(desiredStopFacing, Vector3.up).normalized;
        if (!TryBuildApproachPath(targetWorldPosition, DesiredStopFacing, out Vector3 stopPosition))
            return Fail(
                "No complete NavMesh path reaches the validated 0.3 m table-front handoff corridor.");

        stopDistance = finalRequestedStopDistance;
        if (finalRequestedStopDistance < navMeshPlanningStopDistance &&
            DesiredStopFacing.sqrMagnitude > 0.0001f)
        {
            stopPosition += DesiredStopFacing.normalized *
                            (navMeshPlanningStopDistance - finalRequestedStopDistance);
        }

        StopPosition = stopPosition;
        if (DesiredStopFacing.sqrMagnitude < 0.0001f)
        {
            Vector3 fallbackFacing = Vector3.ProjectOnPlane(TargetPosition - StopPosition, Vector3.up);
            DesiredStopFacing = fallbackFacing.sqrMagnitude > 0.0001f
                ? fallbackFacing.normalized
                : robotBody.forward;
        }
        _cornerIndex = _path.corners.Length > 1 ? 1 : 0;
        _loggedTransitionCorner = -1;
        _pathRotateOnlyActive = false;
        _pathRotateOnlyBraking = false;
        _pathRotateOnlyBackwardPreloading = false;
        _pathRotateOnlyBackwardPreloadTimer = 0f;
        _pathRotateOnlyStartPosition = Vector3.zero;
        _pathRotateOnlyStartFacing = Vector3.zero;
        _nextPathRotationTelemetryTime = 0f;
        _isFinalizingStop = false;
        _handoffBrakingLogged = false;
        _positionDocked = false;
        _finalFacingBrakeLatched = false;
        _facingStableTimer = 0f;
        _nextDockTelemetryTime = 0f;
        _filteredFinalLocalVelocity = Vector3.zero;
        _filteredDockLocalError = Vector3.zero;
        UpdatePathVisualization();
        UpdateStopVisualization();
        UpdatePreAlignmentVisualization();
        if (!_ownsKeyboardMode)
        {
            _savedKeyboardMode = agent.keyboard;
            _ownsKeyboardMode = true;
        }
        agent.keyboard = false;
        IsNavigating = true;
        Vector3 desiredRearDirection = -Vector3.ProjectOnPlane(DesiredStopFacing, Vector3.up).normalized;
        Vector3 stopToHandoff = Vector3.ProjectOnPlane(_preAlignmentPosition - StopPosition, Vector3.up).normalized;
        float handoffDeviation = desiredRearDirection.sqrMagnitude > 0.0001f &&
                                 stopToHandoff.sqrMagnitude > 0.0001f
            ? Mathf.Abs(Vector3.SignedAngle(desiredRearDirection, stopToHandoff, Vector3.up))
            : 0f;
        Debug.Log(
            $"[ElderCareNavigation] Planned stop distance: {PlanarDistance(TargetPosition, StopPosition):F3} m " +
            $"(forward distance {stopDistance:F3} m, left offset {stopLeftOffset:F3} m); " +
            $"NavMesh handoff planned at {navMeshPlanningStopDistance:F3} m; " +
            "staging-to-pre-alignment: " +
            $"{PlanarDistance(_pathEndPosition, _preAlignmentPosition):F3} m; " +
            $"pre-alignment-to-stop: {PlanarDistance(_preAlignmentPosition, StopPosition):F3} m; " +
            $"handoff rear-axis deviation: {handoffDeviation:F1} deg.",
            this);
        return true;
    }

    public void CancelNavigation()
    {
        IsNavigating = false;
        IsReturningHome = false;
        _postGraspRetreatActive = false;
        _playStartRetreatActive = false;
        _isFinalizingStop = false;
        _finalFacingBrakeLatched = false;
        StopMotion();
        RestoreKeyboardMode();
        if (!keepPathAfterArrival)
            ClearPathVisualization();
    }

    bool BeginPlayStartRetreat(
        Vector3 targetWorldPosition,
        Vector3 desiredStopFacing,
        float requestedStopDistance)
    {
        _pendingFirstTargetPosition = targetWorldPosition;
        _pendingFirstStopFacing = desiredStopFacing;
        _pendingFirstStopDistance = requestedStopDistance;
        if (_playStartRetreatActive)
            return true;

        if (!_ownsKeyboardMode)
        {
            _savedKeyboardMode = agent.keyboard;
            _ownsKeyboardMode = true;
        }
        agent.keyboard = false;
        _playStartRetreatStart = robotBody.position;
        _playStartRetreatFacing = Vector3.ProjectOnPlane(
            robotBody.forward,
            Vector3.up).normalized;
        if (_playStartRetreatFacing.sqrMagnitude < 0.0001f)
            _playStartRetreatFacing = PlayStartFacing.sqrMagnitude > 0.0001f
                ? PlayStartFacing
                : Vector3.forward;
        _playStartRetreatTravelled = 0f;
        _playStartRetreatStableTimer = 0f;
        _nextPlayStartRetreatTelemetryTime = 0f;
        _playStartRetreatActive = true;
        IsNavigating = true;
        StopMotion();
        agent.SetAutomaticReverseHeld(true, playStartRetreatSpeed);
        Debug.Log(
            $"[ElderCareNavigation] Play-start retreat started: moving straight " +
            $"backward {playStartRetreatDistance:F2} m along the initial facing " +
            $"{_playStartRetreatFacing}; target navigation and turning are " +
            "locked until a complete standstill.",
            this);
        return true;
    }

    void UpdatePlayStartRetreat()
    {
        if (agent == null || robotBody == null)
        {
            Fail("Robot control was lost during the Play-start retreat.");
            return;
        }

        Vector3 displacement = Vector3.ProjectOnPlane(
            robotBody.position - _playStartRetreatStart,
            Vector3.up);
        float projectedRetreat = Vector3.Dot(
            displacement,
            -_playStartRetreatFacing);
        _playStartRetreatTravelled = Mathf.Max(
            _playStartRetreatTravelled,
            projectedRetreat);
        float remaining = Mathf.Max(
            0f,
            playStartRetreatDistance - _playStartRetreatTravelled);

        if (remaining > 0f)
        {
            // Use the same trained reverse command as holding S. Yaw and
            // lateral commands stay zero throughout this phase.
            agent.SetAutomaticReverseHeld(true, playStartRetreatSpeed);
            agent.vd = 0f;
            agent.wr = 0f;
            _playStartRetreatStableTimer = 0f;
        }
        else
        {
            agent.SetAutomaticReverseHeld(false);
            agent.vr = Mathf.MoveTowards(
                agent.vr,
                0f,
                postGraspRetreatCommandStep);
            agent.vd = Mathf.MoveTowards(
                agent.vd,
                0f,
                postGraspRetreatCommandStep);
            agent.wr = Mathf.MoveTowards(
                agent.wr,
                0f,
                postGraspRetreatCommandStep);
            float planarSpeed = CurrentPhysicalPlanarSpeed();
            float yawRate = Mathf.Abs(CurrentPhysicalYawRate());
            float tilt = Vector3.Angle(robotBody.up, Vector3.up);
            bool commandsStopped = Mathf.Abs(agent.vr) <= 0.01f &&
                                   Mathf.Abs(agent.vd) <= 0.01f &&
                                   Mathf.Abs(agent.wr) <= 0.01f;
            bool physicallyStopped =
                planarSpeed <= postGraspRetreatStopLinearSpeed &&
                yawRate <= postGraspRetreatStopAngularSpeed;
            bool fullyStable = commandsStopped && physicallyStopped &&
                               tilt <= postGraspRetreatMaxStableTilt &&
                               agent.IsGaitSettled();
            if (fullyStable)
                _playStartRetreatStableTimer += Time.fixedDeltaTime;
            else
                _playStartRetreatStableTimer = 0f;

            if (_playStartRetreatStableTimer >= playStartRetreatStableSeconds)
            {
                Vector3 pendingTarget = _pendingFirstTargetPosition;
                Vector3 pendingFacing = _pendingFirstStopFacing;
                float pendingDistance = _pendingFirstStopDistance;
                _playStartRetreatActive = false;
                _playStartRetreatCompleted = true;
                IsNavigating = false;
                Debug.Log(
                    $"[ElderCareNavigation] Play-start retreat complete and " +
                    $"stable: travelled={_playStartRetreatTravelled:F3} m, " +
                    $"speed={planarSpeed:F3} m/s, yawRate={yawRate:F3} rad/s. " +
                    "Planning the target path now; turning is unlocked.",
                    this);
                NavigateToTarget(
                    pendingTarget,
                    pendingFacing,
                    pendingDistance);
                return;
            }
        }

        if (Time.time >= _nextPlayStartRetreatTelemetryTime)
        {
            _nextPlayStartRetreatTelemetryTime = Time.time + 0.5f;
            Debug.Log(
                $"[ElderCareNavigation] Play-start retreat " +
                $"{_playStartRetreatTravelled:F3}/{playStartRetreatDistance:F3} m; " +
                $"remaining={remaining:F3} m; command=({agent.vr:F3},0,0); " +
                $"mode={(remaining > 0f ? "pure-reverse" : "waiting-for-standstill")}.",
                this);
        }
    }

    public void CapturePlayStartPose()
    {
        AutoAssignRobot();
        if (robotBody == null || HasPlayStartPose)
            return;

        PlayStartPosition = robotBody.position;
        PlayStartFacing = Vector3.ProjectOnPlane(robotBody.forward, Vector3.up).normalized;
        if (PlayStartFacing.sqrMagnitude < 0.0001f)
            PlayStartFacing = Vector3.forward;
        HasPlayStartPose = true;
        Debug.Log(
            $"[ElderCareNavigation] Play-start pose captured: " +
            $"position={PlayStartPosition}, facing={PlayStartFacing}.",
            this);
    }

    public bool BeginPostGraspRetreatAndReturnHome()
    {
        EnsurePath();
        AutoAssignRobot();
        if (agent == null || robotBody == null)
            return Fail("Cannot return home without X02newAgent and robotBody.");
        if (!HasPlayStartPose)
            return Fail("The Play-start robot pose was not captured.");

        CancelNavigation();
        if (!_ownsKeyboardMode)
        {
            _savedKeyboardMode = agent.keyboard;
            _ownsKeyboardMode = true;
        }
        agent.keyboard = false;
        IsReturningHome = true;
        _postGraspRetreatActive = true;
        _postGraspRetreatStart = robotBody.position;
        _postGraspRetreatFacing = Vector3.ProjectOnPlane(robotBody.forward, Vector3.up).normalized;
        if (_postGraspRetreatFacing.sqrMagnitude < 0.0001f)
            _postGraspRetreatFacing = PlayStartFacing;
        _postGraspRetreatTravelled = 0f;
        _postGraspRetreatStableTimer = 0f;
        _postGraspRetreatLastProgress = 0f;
        _postGraspRetreatLastProgressTime = Time.time;
        _postGraspRetreatRecoveryLogged = false;
        _nextReturnTelemetryTime = 0f;
        StopMotion();
        agent.SetAutomaticReverseHeld(true, postGraspRetreatSpeed);
        Debug.Log(
            $"[ElderCareNavigation] Post-grasp return started: backing straight " +
            $"{postGraspRetreatDistance:F2} m before navigating to the captured Play-start pose. " +
            $"Current position={robotBody.position}, captured position={PlayStartPosition}, " +
            $"captured facing={PlayStartFacing}.",
            this);
        return true;
    }

    void UpdatePostGraspRetreat()
    {
        if (agent == null || robotBody == null)
        {
            Fail("Robot control was lost during the post-grasp retreat.");
            return;
        }

        // A walking humanoid rocks forward and backward on every step. The old
        // radial distance could therefore decrease after a valid backward
        // step, trapping this phase forever. Measure progress only along the
        // originally captured retreat ray and retain the furthest point
        // reached, so gait sway cannot undo completed retreat distance.
        Vector3 planarFromStart = Vector3.ProjectOnPlane(
            robotBody.position - _postGraspRetreatStart,
            Vector3.up);
        float projectedRetreat = Vector3.Dot(
            planarFromStart,
            -_postGraspRetreatFacing);
        _postGraspRetreatTravelled = Mathf.Max(
            _postGraspRetreatTravelled,
            projectedRetreat);
        float travelled = _postGraspRetreatTravelled;
        if (travelled >= _postGraspRetreatLastProgress + 0.005f)
        {
            _postGraspRetreatLastProgress = travelled;
            _postGraspRetreatLastProgressTime = Time.time;
            _postGraspRetreatRecoveryLogged = false;
        }
        Vector3 currentFacing = Vector3.ProjectOnPlane(robotBody.forward, Vector3.up).normalized;
        float headingError = Vector3.SignedAngle(
            currentFacing,
            _postGraspRetreatFacing,
            Vector3.up);

        float remaining = Mathf.Max(0f, postGraspRetreatDistance - travelled);
        if (remaining > 0f)
        {
            // Mirror holding S in X02newAgent: change vr by exactly 0.01 per
            // 0.01-second physics step. There is no foot lock, support-phase
            // gate, tilt interruption, or AddForce root braking.
            float retreatSpeed = Mathf.Clamp(
                postGraspRetreatSpeed,
                0.40f,
                1.0f);
            bool progressStalled = Time.time - _postGraspRetreatLastProgressTime >=
                                   postGraspRetreatProgressTimeout;
            if (progressStalled && !_postGraspRetreatRecoveryLogged)
            {
                _postGraspRetreatRecoveryLogged = true;
                Debug.LogWarning(
                    $"[ElderCareReturn] Backward progress stalled at " +
                    $"{travelled:F3} m for " +
                    $"{Time.time - _postGraspRetreatLastProgressTime:F2} s; " +
                    $"continuing the unchanged {-retreatSpeed:F2} backward command.",
                    this);
            }
            // The Agent itself now updates this through exactly the same code
            // path as holding S. This controller only holds/releases the key.
            agent.SetAutomaticReverseHeld(true, retreatSpeed);
            _postGraspRetreatStableTimer = 0f;
        }
        else
        {
            // Do not hand control directly from the backward gait to yaw.
            // Hold all three commands at zero until measured translation,
            // rotation, body tilt and the learned gait have remained settled
            // continuously. Only then may path planning enable a turn.
            agent.SetAutomaticReverseHeld(false);
            agent.vr = Mathf.MoveTowards(agent.vr, 0f, postGraspRetreatCommandStep);
            agent.vd = Mathf.MoveTowards(agent.vd, 0f, postGraspRetreatCommandStep);
            agent.wr = Mathf.MoveTowards(agent.wr, 0f, postGraspRetreatCommandStep);
            float planarSpeed = CurrentPhysicalPlanarSpeed();
            float yawRate = Mathf.Abs(CurrentPhysicalYawRate());
            float tilt = Vector3.Angle(robotBody.up, Vector3.up);
            bool commandsStopped = Mathf.Abs(agent.vr) <= 0.01f &&
                                   Mathf.Abs(agent.vd) <= 0.01f &&
                                   Mathf.Abs(agent.wr) <= 0.01f;
            bool physicallyStopped = planarSpeed <= postGraspRetreatStopLinearSpeed &&
                                     yawRate <= postGraspRetreatStopAngularSpeed;
            bool stableBody = tilt <= postGraspRetreatMaxStableTilt;
            bool gaitSettled = agent.IsGaitSettled();
            bool fullyStable = commandsStopped && physicallyStopped && stableBody && gaitSettled;

            if (fullyStable)
                _postGraspRetreatStableTimer += Time.fixedDeltaTime;
            else
                _postGraspRetreatStableTimer = 0f;

            if (_postGraspRetreatStableTimer >= postGraspRetreatStableSeconds)
            {
                _postGraspRetreatActive = false;
                Vector3 virtualTarget = PlayStartPosition +
                                        PlayStartFacing * stopDistance;
                Debug.Log(
                    $"[ElderCareNavigation] Backward retreat complete and stable: " +
                    $"travelled={travelled:F3} m; planarSpeed={planarSpeed:F3} m/s; " +
                    $"yawRate={yawRate:F3} rad/s; tilt={tilt:F1} deg; " +
                    $"stableFor={_postGraspRetreatStableTimer:F2} s. " +
                    $"Planning Play-start return to {PlayStartPosition}.",
                    this);
                if (!NavigateToTarget(virtualTarget, PlayStartFacing))
                    IsReturningHome = false;
                return;
            }
        }

        if (Time.time >= _nextReturnTelemetryTime)
        {
            _nextReturnTelemetryTime = Time.time + 0.5f;
            Debug.Log(
                $"[ElderCareReturn] retreat travelled={travelled:F3}/" +
                $"{postGraspRetreatDistance:F3} m; headingError={headingError:F1} deg; " +
                $"remaining={remaining:F3} m; " +
                $"mode={(remaining > 0f ? "pure-reverse" : "waiting-for-natural-standstill")}; " +
                $"command=({agent.vr:F3}, {agent.vd:F3}, {agent.wr:F3}); " +
                $"physicalSpeed={CurrentPhysicalPlanarSpeed():F3} m/s; " +
                $"tilt={Vector3.Angle(robotBody.up, Vector3.up):F1} deg; " +
                $"stableFor={_postGraspRetreatStableTimer:F2}/{postGraspRetreatStableSeconds:F2} s.",
                this);
        }
    }

    public void HandleAgentEpisodeReset(string reason)
    {
        bool interruptedReturn = IsReturningHome || _postGraspRetreatActive;
        IsNavigating = false;
        IsReturningHome = false;
        _postGraspRetreatActive = false;
        _isFinalizingStop = false;
        StopMotion();
        RestoreKeyboardMode();
        ClearPathVisualization();
        if (interruptedReturn)
            Debug.LogError(
                $"[ElderCareNavigation] Return cancelled before agent episode reset: {reason}.",
                this);
    }

    void FixedUpdate()
    {
        if (_playStartRetreatActive)
        {
            UpdatePlayStartRetreat();
            return;
        }

        if (_postGraspRetreatActive)
        {
            UpdatePostGraspRetreat();
            return;
        }

        if (!IsNavigating || agent == null || robotBody == null)
            return;

        Vector3 robotPosition = robotBody.position;
        float remainingToPathEnd = PlanarDistance(robotPosition, _pathEndPosition);
        if (_isFinalizingStop)
        {
            FinalizeStopPose();
            return;
        }

        if (remainingToPathEnd <= handoffStopLeadDistance)
        {
            BeginContinuousDocking();
            return;
        }

        Vector3[] corners = _path.corners;
        if (corners == null || corners.Length == 0)
        {
            Fail("The active NavMesh path has no corners.");
            return;
        }

        if (!TryGetPathTrackingData(
                robotPosition,
                out Vector3 closestPoint,
                out Vector3 lookAheadPoint,
                out float crossTrackError,
                out float distanceToCorner,
                out bool approachingCorner))
        {
            Fail("The active NavMesh path cannot provide a tracking segment.");
            return;
        }

        DriveAlongPath(
            lookAheadPoint,
            closestPoint,
            crossTrackError,
            remainingToPathEnd,
            distanceToCorner,
            approachingCorner);
    }

    void BeginContinuousDocking()
    {
        _isFinalizingStop = true;
        _positionDocked = false;
        _finalDockPhase = FinalDockPhase.FaceStopPoint;
        _translationAxis = 0;
        _finalFacingBrakeLatched = false;
        _brakeStableTimer = 0f;
        _facingStableTimer = 0f;
        _nextDockTelemetryTime = 0f;
        _filteredFinalLocalVelocity = robotBody.InverseTransformDirection(
            Vector3.ProjectOnPlane(
                _robotRootBody != null ? _robotRootBody.velocity : Vector3.zero,
                Vector3.up));
        _filteredStandstillLocalVelocity = _filteredFinalLocalVelocity;
        _filteredStandstillYawRate = _robotRootBody != null
            ? _robotRootBody.angularVelocity.y
            : 0f;
        _filteredDockLocalError = robotBody.InverseTransformDirection(
            Vector3.ProjectOnPlane(StopPosition - robotBody.position, Vector3.up));
        Debug.Log(
            $"[ElderCareNavigation] Continuous docking started; orange offset " +
            $"{PlanarDistance(robotBody.position, _preAlignmentPosition):F3} m, " +
            $"green offset {PlanarDistance(robotBody.position, StopPosition):F3} m, " +
            $"current command ({agent.vr:F3}, {agent.vd:F3}, {agent.wr:F3}).",
            this);
    }

    void FinalizeStopPose()
    {
        const float motionStoppedThreshold = 0.02f;
        Vector3 positionError = Vector3.ProjectOnPlane(StopPosition - robotBody.position, Vector3.up);
        Vector3 localError = robotBody.InverseTransformDirection(positionError);
        // The X02new gait keeps its 40-degree scaffold active at zero command,
        // so the pelvis position oscillates within every 0.6-second gait cycle.
        // Filter that periodic sway instead of repeatedly correcting each peak.
        float dockErrorAlpha = 1f - Mathf.Exp(-Time.fixedDeltaTime / 0.30f);
        _filteredDockLocalError = Vector3.Lerp(
            _filteredDockLocalError,
            localError,
            dockErrorAlpha);
        Vector3 controlError = _filteredDockLocalError;
        Vector3 currentFacing = Vector3.ProjectOnPlane(robotBody.forward, Vector3.up).normalized;
        float tilt = Vector3.Angle(robotBody.up, Vector3.up);
        if (tilt > dockingPauseTiltDegrees)
        {
            ApplyPhysicalDockingBrake(0f);
            ApplyCommand(0f, 0f, 0f);
            if (_positionDocked)
            {
                _translationAxis = 0;
                _brakeStableTimer = 0f;
                _finalDockPhase = FinalDockPhase.BrakeAfterTranslationPulse;
            }
            _facingStableTimer = 0f;
            LogDockingTelemetry("tilt-recovery", localError, 0f, tilt);
            return;
        }

        bool filteredInsideEnterTolerance = Mathf.Abs(controlError.z) <= arrivalTolerance &&
                                            Mathf.Abs(controlError.x) <= finalLateralTolerance;
        bool insideCaptureTolerance = Mathf.Abs(localError.z) <= dockingCaptureTolerance &&
                                      Mathf.Abs(localError.x) <= dockingCaptureTolerance;
        // X02new keeps its 60-step gait oscillator active at zero command.
        // A single-frame pelvis position can cross even the 9 cm boundary
        // even when the robot has no net drift, which previously restarted a
        // full forward/back correction after final verification. Use the
        // gait-filtered pose for docking precision; retain the much wider raw
        // capture bound as a safety check against genuine displacement.
        bool insideEnterTolerance = filteredInsideEnterTolerance &&
                                    insideCaptureTolerance;
        if (!_positionDocked && insideCaptureTolerance)
        {
            _positionDocked = true;
            _finalDockPhase = FinalDockPhase.FaceStopPoint;
            _translationAxis = 0;
            _finalFacingBrakeLatched = false;
            _brakeStableTimer = 0f;
        }

        if (!_positionDocked)
        {
            Vector3 directionToStop = positionError.sqrMagnitude > 0.000001f
                ? positionError.normalized
                : currentFacing;
            float bearingError = Vector3.SignedAngle(
                currentFacing,
                directionToStop,
                Vector3.up);
            UpdateFilteredFinalLocalVelocity();
            float distance = positionError.magnitude;
            float targetForward = EffectiveFinalTranslationCommand(
                distance,
                _filteredFinalLocalVelocity.z);
            float allowedPhysicalSpeed = Mathf.Sqrt(
                2f * dockingEffectiveDeceleration *
                Mathf.Max(0f, distance - dockingBrakingBuffer));
            if (_filteredFinalLocalVelocity.z > allowedPhysicalSpeed)
                targetForward = 0f;
            else
                targetForward = Mathf.Min(targetForward, allowedPhysicalSpeed);
            float headingScale = Mathf.Clamp01(
                1f - Mathf.Abs(bearingError) / dockingForwardCutoffDegrees);
            targetForward *= headingScale;
            float targetTurn = Mathf.Abs(bearingError) > facingToleranceDegrees
                ? FinalTurnCommand(bearingError)
                : 0f;
            if (IsReturningHome && Mathf.Abs(targetTurn) > 0.001f)
            {
                targetTurn = Mathf.Clamp(
                    targetTurn,
                    -postGraspReturnMaxTurnSpeed,
                    postGraspReturnMaxTurnSpeed);
                float backwardBias = TurnBackwardBias(targetTurn);
                ApplyPhysicalDockingBrake(backwardBias);
                ApplyCommand(-backwardBias, 0f, targetTurn);
                _facingStableTimer = 0f;
                LogDockingTelemetry(
                    "held-approach-turn",
                    localError,
                    bearingError,
                    tilt);
                return;
            }
            float brakingSpeedLimit = targetForward <= 0.001f
                ? 0f
                : allowedPhysicalSpeed;
            ApplyPhysicalDockingBrake(brakingSpeedLimit);
            float balancedForward = HeldReturnBiasedForward(
                targetForward,
                Mathf.Abs(targetForward) > 0.001f);
            ApplyCommand(balancedForward, 0f, targetTurn);
            _facingStableTimer = 0f;
            LogDockingTelemetry("approach", localError, bearingError, tilt);
            return;
        }

        Vector3 finalFacing = Vector3.ProjectOnPlane(DesiredStopFacing, Vector3.up).normalized;
        float finalFacingError = finalFacing.sqrMagnitude > 0.0001f
            ? Vector3.SignedAngle(currentFacing, finalFacing, Vector3.up)
            : 0f;
        UpdateFilteredFinalLocalVelocity();
        UpdateFilteredStandstillMotion();

        // Re-entry hysteresis follows the independently configured axes, so a
        // residual lateral error cannot be accepted merely because the arm can
        // still reach the object from that offset.
        float forwardReentryTolerance = arrivalTolerance + 0.02f;
        float lateralReentryTolerance = finalLateralTolerance + 0.02f;
        const float facingBrakeEntryTolerance = 3f;
        const float facingBrakeExitTolerance = 7f;
        const float stopBearingReentryTolerance = 7f;
        bool outsidePositionReentry =
            Mathf.Abs(controlError.z) > forwardReentryTolerance ||
            Mathf.Abs(controlError.x) > lateralReentryTolerance;
        // Once terminal docking has genuinely entered the requested stop
        // region, the standing gait keeps the pelvis swaying a few
        // centimetres even under a zero command. Use the existing re-entry
        // hysteresis as the settle/verification band as well. Previously the
        // controller stopped correcting below 60 mm lateral error but still
        // demanded the strict 40 mm entry tolerance on every settle frame,
        // creating a 40-60 mm dead zone: visibly stopped, yet unable either
        // to correct or complete and trigger delivery.
        bool insideSettlingTolerance =
            _positionDocked &&
            !outsidePositionReentry &&
            insideCaptureTolerance;

        // Keep the one requested object-facing direction throughout terminal
        // docking. Position is corrected one policy axis at a time: vr first
        // or vd first according to the larger error, never both together.

        switch (_finalDockPhase)
        {
            case FinalDockPhase.FaceStopPoint:
            {
                ApplyPhysicalDockingBrake(
                    IsReturningHome ? postGraspReturnTurnBackwardBias : 0f);
                if (insideEnterTolerance)
                {
                    ApplyCommand(0f, 0f, 0f);
                    _finalDockPhase = FinalDockPhase.BrakeAtStopPoint;
                    _finalFacingBrakeLatched = false;
                    _brakeStableTimer = 0f;
                    _facingStableTimer = 0f;
                    return;
                }

                float absoluteFacingError = Mathf.Abs(finalFacingError);
                if (!_finalFacingBrakeLatched &&
                    absoluteFacingError <= facingBrakeEntryTolerance)
                {
                    _finalFacingBrakeLatched = true;
                }
                else if (_finalFacingBrakeLatched &&
                         absoluteFacingError > facingBrakeExitTolerance)
                {
                    _finalFacingBrakeLatched = false;
                }

                float targetTurn = !_finalFacingBrakeLatched
                    ? FinalTurnCommand(finalFacingError)
                    : 0f;
                targetTurn = Mathf.Clamp(
                    targetTurn,
                    -maxFaceStopTurnSpeed,
                    maxFaceStopTurnSpeed);
                if (IsReturningHome)
                {
                    targetTurn = Mathf.Clamp(
                        targetTurn,
                        -postGraspReturnMaxTurnSpeed,
                        postGraspReturnMaxTurnSpeed);
                }
                float backwardBias = IsReturningHome
                    ? TurnBackwardBias(targetTurn)
                    : 0f;
                ApplyPhysicalDockingBrake(backwardBias);
                ApplyCommand(-backwardBias, 0f, targetTurn);
                _facingStableTimer = 0f;
                LogDockingTelemetry("face-final-before-translation", localError, finalFacingError, tilt);
                if (_finalFacingBrakeLatched &&
                    Mathf.Abs(agent.wr) <= motionStoppedThreshold &&
                    IsFilteredDockingRotationStopped())
                {
                    _translationAxis = 0;
                    _finalFacingBrakeLatched = false;
                    _finalDockPhase = FinalDockPhase.DriveToStopPoint;
                }
                return;
            }

            case FinalDockPhase.DriveToStopPoint:
            {
                if (insideEnterTolerance)
                {
                    ApplyPhysicalDockingBrake(0f);
                    ApplyCommand(0f, 0f, 0f);
                    _finalDockPhase = FinalDockPhase.BrakeAfterTranslationPulse;
                    _brakeStableTimer = 0f;
                    _facingStableTimer = 0f;
                    return;
                }

                if (Mathf.Abs(finalFacingError) > stopBearingReentryTolerance)
                {
                    ApplyCommand(0f, 0f, 0f);
                    _finalDockPhase = FinalDockPhase.FaceStopPoint;
                    _translationAxis = 0;
                    _finalFacingBrakeLatched = false;
                    _facingStableTimer = 0f;
                    return;
                }

                if (_translationAxis == 0)
                {
                    float normalizedForwardError = Mathf.Abs(controlError.z) / arrivalTolerance;
                    float normalizedLateralError = Mathf.Abs(controlError.x) / finalLateralTolerance;
                    _translationAxis = normalizedForwardError >= normalizedLateralError ? 1 : 2;
                }

                // Never accelerate a new axis while the previous axis is
                // still winding down. This keeps terminal motion genuinely
                // one-dimensional instead of producing diagonal commands.
                bool inactiveAxisStopped = _translationAxis == 1
                    ? Mathf.Abs(agent.vd) <= motionStoppedThreshold
                    : Mathf.Abs(agent.vr) <= motionStoppedThreshold;
                if (!inactiveAxisStopped || Mathf.Abs(agent.wr) > motionStoppedThreshold)
                {
                    ApplyCommand(0f, 0f, 0f);
                    LogDockingTelemetry("axis-handoff-deceleration", localError, finalFacingError, tilt);
                    return;
                }

                bool selectedAxisComplete = _translationAxis == 1
                    ? Mathf.Abs(localError.z) <= arrivalTolerance
                    : Mathf.Abs(localError.x) <= finalLateralTolerance;
                if (selectedAxisComplete)
                {
                    ApplyPhysicalDockingBrake(0f);
                    ApplyCommand(0f, 0f, 0f);
                    _finalDockPhase = FinalDockPhase.BrakeAfterTranslationPulse;
                    _brakeStableTimer = 0f;
                    _facingStableTimer = 0f;
                    return;
                }

                float activeError = _translationAxis == 1 ? controlError.z : controlError.x;
                float activeVelocity = _translationAxis == 1
                    ? _filteredFinalLocalVelocity.z
                    : _filteredFinalLocalVelocity.x;
                float activeTolerance = _translationAxis == 1
                    ? arrivalTolerance
                    : finalLateralTolerance;
                float remainingBrakingDistance = Mathf.Max(
                    0f,
                    Mathf.Abs(activeError) - activeTolerance);
                float terminalSpeedLimit = Mathf.Min(
                    maxFinalTranslationSpeed,
                    Mathf.Sqrt(
                        2f * dockingEffectiveDeceleration *
                        remainingBrakingDistance));
                float continuousCommand = EffectiveFinalTranslationCommand(
                    activeError,
                    activeVelocity);
                bool movingTowardTarget =
                    Mathf.Abs(activeVelocity) > 0.001f &&
                    Mathf.Sign(activeVelocity) == Mathf.Sign(activeError);
                if (movingTowardTarget &&
                    Mathf.Abs(activeVelocity) > terminalSpeedLimit)
                {
                    continuousCommand = 0f;
                }
                else
                {
                    continuousCommand = Mathf.Clamp(
                        continuousCommand,
                        -terminalSpeedLimit,
                        terminalSpeedLimit);
                }
                float continuousForward = _translationAxis == 1 ? continuousCommand : 0f;
                float continuousLateral = _translationAxis == 2 ? continuousCommand : 0f;
                continuousForward = HeldReturnBiasedForward(
                    continuousForward,
                    Mathf.Abs(continuousForward) > 0.001f ||
                    Mathf.Abs(continuousLateral) > 0.001f);

                // Brake the physical root before the tolerance boundary. The
                // allowed speed shrinks with the distance still available to
                // stop, preventing the old cross-target/reverse/cross-target
                // loop caused by a fixed 0.45/0.30 correction command.
                ApplyPhysicalDockingBrake(terminalSpeedLimit);
                ApplyCommand(continuousForward, continuousLateral, 0f);
                _facingStableTimer = 0f;
                LogDockingTelemetry(
                    _translationAxis == 1 ? "continuous-forward" : "continuous-lateral",
                    localError,
                    finalFacingError,
                    tilt);
                return;
            }

            case FinalDockPhase.BrakeAfterTranslationPulse:
            {
                // Wind the active terminal command down through the same
                // acceleration-limited path. Do not replace a trained-range
                // walking command with zero in one physics frame.
                ApplyCommand(0f, 0f, 0f);
                bool terminalCommandsStopped =
                    Mathf.Abs(agent.vr) <= motionStoppedThreshold &&
                    Mathf.Abs(agent.vd) <= motionStoppedThreshold &&
                    Mathf.Abs(agent.wr) <= motionStoppedThreshold;
                bool terminalBodyStopped = IsFilteredDockingBodyStopped();
                LogDockingTelemetry("continuous-deceleration", localError, finalFacingError, tilt);
                if (!terminalCommandsStopped || !terminalBodyStopped)
                    return;

                _finalDockPhase = insideEnterTolerance
                    ? FinalDockPhase.BrakeAtStopPoint
                    : FinalDockPhase.FaceStopPoint;
                _translationAxis = 0;
                _finalFacingBrakeLatched = false;
                _brakeStableTimer = 0f;
                return;
            }

            case FinalDockPhase.BrakeAtStopPoint:
            {
                ApplyPhysicalDockingBrake(0f);
                ApplyCommand(0f, 0f, 0f);
                _facingStableTimer = 0f;
                LogDockingTelemetry("brake-at-green", localError, finalFacingError, tilt);

                if (outsidePositionReentry)
                {
                    _finalDockPhase = FinalDockPhase.FaceStopPoint;
                    _translationAxis = 0;
                    _finalFacingBrakeLatched = false;
                    _brakeStableTimer = 0f;
                    return;
                }

                bool translationCommandStopped =
                    Mathf.Abs(agent.vr) <= motionStoppedThreshold &&
                    Mathf.Abs(agent.vd) <= motionStoppedThreshold;
                bool bodyStoppedAtTarget = IsFilteredDockingBodyStopped();
                if (insideSettlingTolerance &&
                    translationCommandStopped &&
                    bodyStoppedAtTarget)
                {
                    _brakeStableTimer += Time.fixedDeltaTime;
                    if (_brakeStableTimer >= dockingBrakeStableSeconds)
                    {
                        _finalDockPhase = insideSettlingTolerance
                            ? FinalDockPhase.AlignFinalFacing
                            : FinalDockPhase.FaceStopPoint;
                        if (_finalDockPhase == FinalDockPhase.FaceStopPoint)
                            _translationAxis = 0;
                        _brakeStableTimer = 0f;
                    }
                }
                else
                    _brakeStableTimer = 0f;
                return;
            }

            case FinalDockPhase.AlignFinalFacing:
            {
                ApplyPhysicalDockingBrake(
                    IsReturningHome ? postGraspReturnTurnBackwardBias : 0f);
                if (outsidePositionReentry)
                {
                    ApplyCommand(0f, 0f, 0f);
                    _finalDockPhase = FinalDockPhase.FaceStopPoint;
                    _translationAxis = 0;
                    _finalFacingBrakeLatched = false;
                    _brakeStableTimer = 0f;
                    _facingStableTimer = 0f;
                    return;
                }

                float absoluteFacingError = Mathf.Abs(finalFacingError);
                if (!_finalFacingBrakeLatched &&
                    absoluteFacingError <= facingBrakeEntryTolerance)
                {
                    _finalFacingBrakeLatched = true;
                }
                else if (_finalFacingBrakeLatched &&
                         absoluteFacingError > facingBrakeExitTolerance)
                {
                    _finalFacingBrakeLatched = false;
                }

                float targetTurn = !_finalFacingBrakeLatched
                    ? FinalTurnCommand(finalFacingError)
                    : 0f;
                if (IsReturningHome)
                {
                    targetTurn = Mathf.Clamp(
                        targetTurn,
                        -postGraspReturnMaxTurnSpeed,
                        postGraspReturnMaxTurnSpeed);
                }
                float backwardBias = IsReturningHome
                    ? TurnBackwardBias(targetTurn)
                    : 0f;
                ApplyPhysicalDockingBrake(backwardBias);
                ApplyCommand(-backwardBias, 0f, targetTurn);
                _facingStableTimer = 0f;
                LogDockingTelemetry("final-facing", localError, finalFacingError, tilt);
                if (_finalFacingBrakeLatched &&
                    Mathf.Abs(agent.wr) <= motionStoppedThreshold &&
                    IsFilteredDockingRotationStopped())
                {
                    _finalFacingBrakeLatched = false;
                    _finalDockPhase = FinalDockPhase.Verify;
                }
                return;
            }

            case FinalDockPhase.Verify:
                ApplyPhysicalDockingBrake(0f);
                ApplyCommand(0f, 0f, 0f);
                LogDockingTelemetry("final-verify", localError, finalFacingError, tilt);
                if (!insideSettlingTolerance)
                {
                    _finalDockPhase = FinalDockPhase.FaceStopPoint;
                    _translationAxis = 0;
                    _finalFacingBrakeLatched = false;
                    _brakeStableTimer = 0f;
                    _facingStableTimer = 0f;
                    return;
                }
                if (Mathf.Abs(finalFacingError) > facingToleranceDegrees)
                {
                    _finalDockPhase = FinalDockPhase.AlignFinalFacing;
                    _finalFacingBrakeLatched = false;
                    _facingStableTimer = 0f;
                    return;
                }
                break;
        }

        bool commandsStopped = Mathf.Abs(agent.vr) <= motionStoppedThreshold &&
                               Mathf.Abs(agent.vd) <= motionStoppedThreshold &&
                               Mathf.Abs(agent.wr) <= motionStoppedThreshold;
        bool bodyStopped = IsFilteredDockingBodyStopped();
        if (!insideSettlingTolerance ||
            Mathf.Abs(finalFacingError) > facingToleranceDegrees ||
            !commandsStopped ||
            !bodyStopped)
        {
            _facingStableTimer = 0f;
            return;
        }

        _facingStableTimer += Time.fixedDeltaTime;
        if (_facingStableTimer >= facingStableSeconds)
            CompleteNavigation();
    }

    void LogDockingTelemetry(string phase, Vector3 localError, float headingError, float tilt)
    {
        if (Time.time < _nextDockTelemetryTime)
            return;

        _nextDockTelemetryTime = Time.time + dockingTelemetryInterval;
        Debug.Log(
            $"[ElderCareDock] phase={phase}; localError=({localError.x:F3}, {localError.z:F3}) m; " +
            $"heading={headingError:F1} deg; command=({agent.vr:F3}, {agent.vd:F3}, {agent.wr:F3}); " +
            $"physicalSpeed={CurrentPhysicalPlanarSpeed():F3} m/s; " +
            $"yawRate={CurrentPhysicalYawRate():F3} rad/s; tilt={tilt:F1} deg.",
            this);
    }

    float CurrentPhysicalYawRate()
    {
        if (_robotRootBody == null)
            return 0f;

        return _robotRootBody.angularVelocity.y;
    }

    void ApplyPhysicalDockingBrake(float allowedPlanarSpeed)
    {
        if (_robotRootBody == null)
            return;

        Vector3 planarVelocity = Vector3.ProjectOnPlane(_robotRootBody.velocity, Vector3.up);
        float speed = planarVelocity.magnitude;
        if (speed <= allowedPlanarSpeed || speed < 0.001f)
            return;

        Vector3 allowedVelocity = planarVelocity.normalized * allowedPlanarSpeed;
        Vector3 requiredAcceleration = (allowedVelocity - planarVelocity) /
                                       Mathf.Max(0.05f, dockingBrakeResponseSeconds);
        requiredAcceleration = Vector3.ClampMagnitude(
            requiredAcceleration,
            dockingBrakeAcceleration);
        _robotRootBody.AddForce(requiredAcceleration, ForceMode.Acceleration);
    }

    float FinalTurnCommand(float signedAngle)
    {
        float turnMagnitude = Mathf.Clamp(
            Mathf.Abs(signedAngle) / 45f,
            minFinalTurnSpeed,
            maxFinalTurnSpeed);
        return Mathf.Sign(signedAngle) * turnMagnitude;
    }

    float EffectiveFinalTranslationCommand(float localError, float localVelocity)
    {
        if (Mathf.Abs(localError) < 0.0001f)
            return 0f;

        float dampedCommand = localError * finalPositionGain -
                              localVelocity * finalVelocityDamping;

        // When velocity feedback asks for braking, command zero rather than
        // reversing before the target has actually been crossed.
        if (Mathf.Abs(dampedCommand) < 0.0001f ||
            Mathf.Sign(dampedCommand) != Mathf.Sign(localError))
            return 0f;

        float magnitude = Mathf.Clamp(
            Mathf.Abs(dampedCommand),
            minFinalTranslationSpeed,
            maxFinalTranslationSpeed);
        return Mathf.Sign(localError) * magnitude;
    }

    void UpdateFilteredFinalLocalVelocity()
    {
        if (_robotRootBody == null)
        {
            _filteredFinalLocalVelocity = Vector3.zero;
            return;
        }

        Vector3 planarVelocity = Vector3.ProjectOnPlane(_robotRootBody.velocity, Vector3.up);
        Vector3 localVelocity = robotBody.InverseTransformDirection(planarVelocity);
        float alpha = 1f - Mathf.Exp(
            -Time.fixedDeltaTime / Mathf.Max(0.02f, finalVelocityFilterSeconds));
        _filteredFinalLocalVelocity = Vector3.Lerp(
            _filteredFinalLocalVelocity,
            localVelocity,
            alpha);
    }

    void UpdateFilteredStandstillMotion()
    {
        if (_robotRootBody == null)
        {
            _filteredStandstillLocalVelocity = Vector3.zero;
            _filteredStandstillYawRate = 0f;
            return;
        }

        Vector3 planarVelocity = Vector3.ProjectOnPlane(
            _robotRootBody.velocity,
            Vector3.up);
        Vector3 localVelocity = robotBody.InverseTransformDirection(planarVelocity);
        // X02new keeps a 60-step oscillator active at zero command. Average
        // over roughly one full gait cycle so normal pelvis sway does not
        // repeatedly cancel a genuine standstill, while net drift still blocks
        // arrival and grasp startup.
        const float gaitCycleFilterSeconds = 0.60f;
        float alpha = 1f - Mathf.Exp(-Time.fixedDeltaTime / gaitCycleFilterSeconds);
        _filteredStandstillLocalVelocity = Vector3.Lerp(
            _filteredStandstillLocalVelocity,
            localVelocity,
            alpha);
        _filteredStandstillYawRate = Mathf.Lerp(
            _filteredStandstillYawRate,
            _robotRootBody.angularVelocity.y,
            alpha);
    }

    bool IsFilteredDockingBodyStopped()
    {
        // A zero-command X02 remains physically standing while its learned gait
        // produces a small periodic pelvis sway. The instantaneous speed seen in
        // a valid stand is normally 0.01-0.03 m/s; treating that harmless sway as
        // continued navigation can keep Arrived from firing forever. Accept
        // either the gait-cycle average or the existing physical standstill
        // envelope, while retaining the angular-velocity check.
        bool gaitAverageStopped =
            _filteredStandstillLocalVelocity.magnitude <= handoffPhysicalStopSpeed;
        bool physicallyStanding =
            CurrentPhysicalPlanarSpeed() <= physicalStopLinearSpeed;
        return (gaitAverageStopped || physicallyStanding) &&
               IsFilteredDockingRotationStopped();
    }

    bool IsFilteredDockingRotationStopped()
    {
        return Mathf.Abs(_filteredStandstillYawRate) <= physicalStopAngularSpeed;
    }

    void DriveAlongPath(
        Vector3 lookAheadPoint,
        Vector3 closestPoint,
        float crossTrackError,
        float remainingToHandoff,
        float distanceToCorner,
        bool approachingCorner)
    {
        Vector3 toLookAhead = Vector3.ProjectOnPlane(lookAheadPoint - robotBody.position, Vector3.up);
        if (toLookAhead.sqrMagnitude < 0.0001f)
        {
            ApplyCommand(0f, 0f, 0f);
            return;
        }

        float signedAngle = Vector3.SignedAngle(robotBody.forward, toLookAhead.normalized, Vector3.up);
        float targetForward = 0f;
        float handoffScale = Mathf.Clamp01(
            Mathf.InverseLerp(preAlignmentReachTolerance, finalBrakingDistance, remainingToHandoff));
        float controlledHandoffScale = Mathf.Max(0.05f, handoffScale);
        // Enforce a physical stopping envelope throughout the final 0.8 m,
        // before terminal docking takes ownership. Command scaling alone did
        // not remove the floating base's momentum, so it could enter the
        // capture region too fast and require several reverse corrections.
        if (remainingToHandoff <= finalBrakingDistance)
        {
            float pathBrakingSpeedLimit = Mathf.Min(
                IsReturningHome
                    ? postGraspReturnMaxForwardSpeed
                    : maxForwardSpeed,
                Mathf.Sqrt(
                    2f * dockingEffectiveDeceleration *
                    Mathf.Max(
                        0f,
                        remainingToHandoff - preAlignmentReachTolerance)));
            ApplyPhysicalDockingBrake(pathBrakingSpeedLimit);
        }
        if (!_handoffBrakingLogged && remainingToHandoff <= finalBrakingDistance)
        {
            _handoffBrakingLogged = true;
            Debug.Log(
                $"[ElderCareNavigation] Braking for orange hand-off marker; " +
                $"remaining {remainingToHandoff:F3} m.",
                this);
        }

        float activeMaxTurnSpeed = IsReturningHome
            ? Mathf.Min(maxTurnSpeed, postGraspReturnMaxTurnSpeed)
            : maxTurnSpeed;
        float targetTurn;
        if (IsReturningHome && Mathf.Abs(signedAngle) > rotateOnlyAboveDegrees)
        {
            // The locomotion policy was trained with yaw commands starting at
            // about 0.6. A 0.1 command starts its gait oscillator but does not
            // produce a reliable left/right leg difference, which was seen as
            // straight forward walking. Keep pure turns inside the trained
            // command range; translation remains locked below.
            float trainedTurnMagnitude = Mathf.Clamp(
                Mathf.Abs(signedAngle) / 45f,
                Mathf.Min(postGraspReturnMinPureTurnSpeed, postGraspReturnMaxTurnSpeed),
                Mathf.Max(postGraspReturnMinPureTurnSpeed, postGraspReturnMaxTurnSpeed));
            targetTurn = Mathf.Sign(signedAngle) * trainedTurnMagnitude;
        }
        else
        {
            targetTurn = Mathf.Clamp(
                signedAngle / 45f,
                -activeMaxTurnSpeed,
                activeMaxTurnSpeed) * controlledHandoffScale;
        }

        // A held-object return must never combine a large turn with forward
        // or lateral path motion. With the current zero-bias configuration,
        // measured translation must stop before yaw is enabled and both
        // translation commands remain exactly zero throughout the turn.
        if (Mathf.Abs(signedAngle) > rotateOnlyAboveDegrees)
        {
            if (!_pathRotateOnlyActive)
            {
                _pathRotateOnlyActive = true;
                _pathRotateOnlyBraking = true;
                _pathRotateOnlyBackwardPreloading = false;
                _pathRotateOnlyBackwardPreloadTimer = 0f;
                _pathRotateOnlyStartPosition = robotBody.position;
                _pathRotateOnlyStartFacing = Vector3.ProjectOnPlane(
                    robotBody.forward,
                    Vector3.up).normalized;
                _nextPathRotationTelemetryTime = 0f;
                if (IsReturningHome)
                {
                    Debug.Log(
                        $"[ElderCareNavigation] Pure-rotation path alignment: " +
                        $"heading={signedAngle:F1} deg; forward/lateral path " +
                        "commands locked to zero.",
                        this);
                }
                else
                {
                    Debug.Log(
                        $"[ElderCareNavigation] Rotation-only sharp turn: " +
                        $"heading={signedAngle:F1} deg; forward and lateral " +
                        "commands locked to zero.",
                        this);
                }
            }

            if (_pathRotateOnlyBraking)
            {
                bool useBackwardTurnBias =
                    IsReturningHome && postGraspReturnTurnBackwardBias > 0.001f;
                if (useBackwardTurnBias)
                {
                    ApplyPhysicalDockingBrake(postGraspReturnTurnBackwardBias);
                    ApplyCommand(-postGraspReturnTurnBackwardBias, 0f, 0f);
                    bool backwardBalanceReady =
                        agent.vr <= -postGraspReturnTurnBackwardBias * 0.9f &&
                        Mathf.Abs(agent.vd) <= 0.02f &&
                        CurrentPhysicalPlanarSpeed() <=
                            Mathf.Max(physicalStopLinearSpeed,
                                      postGraspReturnTurnBackwardBias + 0.03f) &&
                        agent.IsGaitSupportPhase(terminalSupportPhaseToleranceSteps);
                    if (!backwardBalanceReady)
                        return;
                }
                else
                {
                    ApplyPhysicalDockingBrake(0f);
                    ApplyCommand(0f, 0f, 0f);
                    bool translationStopped =
                        Mathf.Abs(agent.vr) <= 0.02f &&
                        Mathf.Abs(agent.vd) <= 0.02f &&
                        CurrentPhysicalPlanarSpeed() <= physicalStopLinearSpeed &&
                        Mathf.Abs(agent.wr) <= 0.02f &&
                        IsPhysicalRotationStopped() &&
                        (IsReturningHome
                            ? agent.IsGaitSupportPhase(terminalSupportPhaseToleranceSteps)
                            : agent.IsGaitSettled());
                    if (!translationStopped)
                        return;
                }
                _pathRotateOnlyBraking = false;
                _pathRotateOnlyBackwardPreloading = useBackwardTurnBias;
                _pathRotateOnlyBackwardPreloadTimer = 0f;
                Debug.Log(useBackwardTurnBias
                    ? $"[ElderCareNavigation] Retreat reduced continuously to " +
                      $"the {-postGraspReturnTurnBackwardBias:F2} backward support command " +
                      $"before rotation at heading={signedAngle:F1} deg."
                    : $"[ElderCareNavigation] Translation fully stopped; starting pure " +
                      $"rotation at heading={signedAngle:F1} deg.",
                    this);
            }

            if (_pathRotateOnlyBackwardPreloading)
            {
                // Merely setting vr negative in the same frame as wr is not
                // enough for this heavy humanoid: yaw can begin while the
                // body is still pitched forward from the preceding stop.
                // Establish a short support-synchronised backward gait first,
                // then retain that same negative vr throughout the turn.
                ApplyCommand(-postGraspReturnTurnBackwardBias, 0f, 0f);
                bool backwardCommandEstablished =
                    agent.vr <= -postGraspReturnTurnBackwardBias * 0.9f;
                if (backwardCommandEstablished)
                    _pathRotateOnlyBackwardPreloadTimer += Time.fixedDeltaTime;
                else
                    _pathRotateOnlyBackwardPreloadTimer = 0f;

                if (_pathRotateOnlyBackwardPreloadTimer <
                        postGraspReturnTurnPreloadSeconds ||
                    !agent.IsGaitSupportPhase(terminalSupportPhaseToleranceSteps))
                    return;

                _pathRotateOnlyBackwardPreloading = false;
                Debug.Log(
                    $"[ElderCareNavigation] Backward support established for " +
                    $"{_pathRotateOnlyBackwardPreloadTimer:F2} s; starting held-object " +
                    $"rotation with forward/lateral path motion still locked.",
                    this);
            }

            if (IsReturningHome)
            {
                Vector3 planarTravel = Vector3.ProjectOnPlane(
                    robotBody.position - _pathRotateOnlyStartPosition,
                    Vector3.up);
                float forwardDrift = _pathRotateOnlyStartFacing.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(planarTravel, _pathRotateOnlyStartFacing)
                    : 0f;
                Vector3 localVelocity = _robotRootBody != null
                    ? robotBody.InverseTransformDirection(_robotRootBody.velocity)
                    : Vector3.zero;
                float dynamicBackwardCompensation = Mathf.Clamp(
                    Mathf.Max(0f, localVelocity.z) *
                        postGraspReturnTurnVelocityCompensationGain +
                    Mathf.Max(0f, forwardDrift) *
                        postGraspReturnTurnDriftCompensationGain,
                    0f,
                    postGraspReturnMaxDynamicTurnBackward);

                // Enter the trained yaw range immediately. Ramping through
                // 0.05-0.5 recreates the out-of-distribution interval that
                // made the policy walk straight before it ever began yawing.
                SetTerminalCommandImmediate(
                    -dynamicBackwardCompensation,
                    0f,
                    targetTurn);

                if (Time.time >= _nextPathRotationTelemetryTime)
                {
                    _nextPathRotationTelemetryTime = Time.time + 0.5f;
                    Debug.Log(
                        $"[ElderCareTurn] headingError={signedAngle:F1} deg; " +
                        $"command=({agent.vr:F3}, {agent.vd:F3}, {agent.wr:F3}); " +
                        $"localVelocity=({localVelocity.x:F3}, {localVelocity.z:F3}) m/s; " +
                        $"dynamicBackward={dynamicBackwardCompensation:F3} m/s; " +
                        $"yawRate={CurrentPhysicalYawRate():F3} rad/s; " +
                        $"forwardDrift={forwardDrift:F3} m; " +
                        $"tilt={Vector3.Angle(robotBody.up, Vector3.up):F1} deg.",
                        this);
                }
            }
            else
                ApplyCommand(0f, 0f, targetTurn);
            return;
        }

        if (_pathRotateOnlyActive)
        {
            if (IsReturningHome)
            {
                ApplyPhysicalDockingBrake(postGraspReturnTurnBackwardBias);
                ApplyCommand(-postGraspReturnTurnBackwardBias, 0f, 0f);
            }
            else
            {
                ApplyPhysicalDockingBrake(0f);
                ApplyCommand(0f, 0f, 0f);
            }
            if (Mathf.Abs(agent.wr) > 0.02f || !IsPhysicalRotationStopped())
                return;
            _pathRotateOnlyActive = false;
            _pathRotateOnlyBraking = false;
            _pathRotateOnlyBackwardPreloading = false;
            _pathRotateOnlyBackwardPreloadTimer = 0f;
            _pathRotateOnlyStartPosition = Vector3.zero;
            _pathRotateOnlyStartFacing = Vector3.zero;
            _nextPathRotationTelemetryTime = 0f;
            Debug.Log(
                $"[ElderCareNavigation] Path rotation settled at " +
                $"heading={signedAngle:F1} deg; translation unlocked.",
                this);
        }

        Vector3 correctionWorld = Vector3.ProjectOnPlane(closestPoint - robotBody.position, Vector3.up);
        Vector3 correctionLocal = robotBody.InverseTransformDirection(correctionWorld);
        float activeMaxLateralSpeed = IsReturningHome
            ? Mathf.Min(maxLateralSpeed, postGraspReturnMaxLateralSpeed)
            : maxLateralSpeed;
        float targetLateral = Mathf.Clamp(
            correctionLocal.x * lateralCorrectionGain,
            -activeMaxLateralSpeed,
            activeMaxLateralSpeed) * controlledHandoffScale;

        if (crossTrackError <= correctionOnlyDistance && Mathf.Abs(signedAngle) <= rotateOnlyAboveDegrees)
        {
            float headingScale = Mathf.Clamp01(1f - Mathf.Abs(signedAngle) / rotateOnlyAboveDegrees);
            float pathScale = 1f - Mathf.InverseLerp(pathTolerance, correctionOnlyDistance, crossTrackError);
            float cornerScale = approachingCorner
                ? Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(distanceToCorner / cornerSlowDistance))
                : 1f;
            float activeMaxForwardSpeed = IsReturningHome
                ? Mathf.Min(maxForwardSpeed, postGraspReturnMaxForwardSpeed)
                : maxForwardSpeed;
            targetForward = activeMaxForwardSpeed *
                            controlledHandoffScale *
                            headingScale *
                            Mathf.Max(0.2f, pathScale) *
                            cornerScale;
        }

        // Held-return translation and rotation are deliberately exclusive.
        // Preserve the already validated outbound follower, which may blend a
        // small turn into normal unloaded path tracking.
        float balancedForward = HeldReturnBiasedForward(
            targetForward,
            Mathf.Abs(targetForward) > 0.001f ||
            Mathf.Abs(targetLateral) > 0.001f);
        ApplyCommand(
            balancedForward,
            targetLateral,
            IsReturningHome ? 0f : targetTurn);
    }

    void ApplyCommand(float targetForward, float targetLateral, float targetTurn)
    {
        float activeLinearAcceleration = IsReturningHome
            ? Mathf.Min(forwardAcceleration, postGraspReturnLinearAcceleration)
            : forwardAcceleration;
        float activeLinearDeceleration = IsReturningHome
            ? Mathf.Min(forwardDeceleration, postGraspReturnLinearDeceleration)
            : forwardDeceleration;
        float activeTurnAcceleration = IsReturningHome
            ? Mathf.Min(turnAcceleration, postGraspReturnTurnAcceleration)
            : turnAcceleration;
        float forwardRate = IsSlowing(agent.vr, targetForward)
            ? activeLinearDeceleration
            : activeLinearAcceleration;
        float lateralRate = IsSlowing(agent.vd, targetLateral)
            ? activeLinearDeceleration
            : activeLinearAcceleration;
        agent.vr = Mathf.MoveTowards(agent.vr, targetForward, forwardRate * Time.fixedDeltaTime);
        agent.vd = Mathf.MoveTowards(agent.vd, targetLateral, lateralRate * Time.fixedDeltaTime);
        agent.wr = Mathf.MoveTowards(agent.wr, targetTurn, activeTurnAcceleration * Time.fixedDeltaTime);
    }

    float TurnBackwardBias(float turnCommand)
    {
        float turnReference = Mathf.Max(0.05f, postGraspReturnMaxTurnSpeed);
        return postGraspReturnTurnBackwardBias *
               Mathf.Clamp01(Mathf.Abs(turnCommand) / turnReference);
    }

    float HeldReturnBiasedForward(float plannedForward, bool motionActive)
    {
        if (!IsReturningHome || !motionActive)
            return plannedForward;

        float backwardBias = postGraspReturnMotionBackwardBias;
        // A small positive path command must remain positive; otherwise the
        // balance term could reverse the robot near a hand-off marker. During
        // lateral or already-backward motion, apply the full configured bias.
        if (plannedForward > 0f)
            backwardBias = Mathf.Min(backwardBias, plannedForward * 0.5f);
        return Mathf.Clamp(
            plannedForward - backwardBias,
            -postGraspReturnMaxForwardSpeed,
            postGraspReturnMaxForwardSpeed);
    }

    float ActiveTerminalForwardPulseCommand()
    {
        return IsReturningHome
            ? Mathf.Min(terminalForwardPulseCommand, postGraspReturnMaxForwardSpeed)
            : terminalForwardPulseCommand;
    }

    float ActiveTerminalLateralPulseCommand()
    {
        return IsReturningHome
            ? Mathf.Min(terminalLateralPulseCommand, postGraspReturnMaxLateralSpeed)
            : terminalLateralPulseCommand;
    }

    void SetTerminalCommandImmediate(float forward, float lateral, float turn)
    {
        agent.vr = forward;
        agent.vd = lateral;
        agent.wr = turn;
    }

    static bool IsSlowing(float current, float target)
    {
        return Mathf.Abs(target) < Mathf.Abs(current) ||
               (Mathf.Abs(current) > 0.0001f && Mathf.Sign(current) != Mathf.Sign(target));
    }

    bool TryGetPathTrackingData(
        Vector3 robotPosition,
        out Vector3 closestPoint,
        out Vector3 lookAheadPoint,
        out float crossTrackError,
        out float distanceToCorner,
        out bool approachingCorner)
    {
        closestPoint = default;
        lookAheadPoint = default;
        crossTrackError = 0f;
        distanceToCorner = 0f;
        approachingCorner = false;

        Vector3[] corners = _path.corners;
        if (corners == null || corners.Length < 2)
            return false;

        int firstSegment = Mathf.Clamp(_cornerIndex - 1, 0, corners.Length - 2);
        int bestSegment = firstSegment;
        float bestSegmentT = 0f;
        float bestDistance = float.PositiveInfinity;

        for (int segment = firstSegment; segment < corners.Length - 1; segment++)
        {
            Vector3 candidate = ClosestPointOnPlanarSegment(
                robotPosition,
                corners[segment],
                corners[segment + 1],
                out float segmentT);
            float distance = PlanarDistance(robotPosition, candidate);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestSegment = segment;
            bestSegmentT = segmentT;
            closestPoint = candidate;
        }

        _cornerIndex = bestSegment + 1;
        crossTrackError = bestDistance;
        distanceToCorner = PlanarDistance(closestPoint, corners[_cornerIndex]);

        if (_cornerIndex < corners.Length - 1)
        {
            Vector3 corner = corners[_cornerIndex];
            Vector3 incomingSegment = Vector3.ProjectOnPlane(
                corner - corners[_cornerIndex - 1],
                Vector3.up);
            Vector3 incoming = Vector3.ProjectOnPlane(corner - closestPoint, Vector3.up);
            Vector3 outgoing = Vector3.ProjectOnPlane(
                corners[_cornerIndex + 1] - corner,
                Vector3.up);
            float cornerAngle = incomingSegment.sqrMagnitude > 0.0001f &&
                                outgoing.sqrMagnitude > 0.0001f
                ? Vector3.Angle(incomingSegment, outgoing)
                : 0f;
            approachingCorner = incoming.sqrMagnitude > 0.0001f &&
                                outgoing.sqrMagnitude > 0.0001f &&
                                Vector3.Angle(incoming, outgoing) > 10f;

            // For an approximately 90-degree corner, replace only the final
            // 10 cm of the incoming line and first 10 cm of the outgoing line
            // with a short chord. Once the robot passes the chord midpoint,
            // the nearest-segment search naturally selects the outgoing line
            // again and the normal cross-track controller presses it back
            // onto that line. Other corner angles retain the tagged behavior.
            float availableLead = Mathf.Min(
                rightAngleTurnLeadDistance,
                Mathf.Min(incomingSegment.magnitude, outgoing.magnitude) * 0.45f);
            bool useEarlyRightAngleTurn =
                cornerAngle >= rightAngleTurnMinimumDegrees &&
                cornerAngle <= rightAngleTurnMaximumDegrees &&
                availableLead >= 0.03f &&
                distanceToCorner <= availableLead + pathTolerance;
            if (useEarlyRightAngleTurn)
            {
                Vector3 entry = corner - incomingSegment.normalized * availableLead;
                Vector3 exit = corner + outgoing.normalized * availableLead;
                closestPoint = ClosestPointOnPlanarSegment(
                    robotPosition,
                    entry,
                    exit,
                    out float transitionT);
                crossTrackError = PlanarDistance(robotPosition, closestPoint);
                distanceToCorner = PlanarDistance(closestPoint, exit);
                approachingCorner = true;

                float chordRemaining = PlanarDistance(closestPoint, exit);
                lookAheadPoint = chordRemaining > 0.0001f
                    ? Vector3.Lerp(
                        closestPoint,
                        exit,
                        Mathf.Clamp01(lookAheadDistance / chordRemaining))
                    : exit;
                if (_loggedTransitionCorner != _cornerIndex)
                {
                    _loggedTransitionCorner = _cornerIndex;
                    Debug.Log(
                        $"[ElderCareNavigation] Entering {cornerAngle:F1}-deg corner " +
                        $"transition {_cornerIndex}: turning {availableLead:F2} m early, " +
                        "then reacquiring the outgoing path line.",
                        this);
                }
                return true;
            }
        }

        float remainingLookAhead = lookAheadDistance;
        Vector3 cursor = Vector3.Lerp(corners[bestSegment], corners[bestSegment + 1], bestSegmentT);
        for (int segment = bestSegment; segment < corners.Length - 1; segment++)
        {
            Vector3 segmentEnd = corners[segment + 1];
            float segmentRemaining = PlanarDistance(cursor, segmentEnd);
            if (remainingLookAhead <= segmentRemaining && segmentRemaining > 0.0001f)
            {
                lookAheadPoint = Vector3.Lerp(cursor, segmentEnd, remainingLookAhead / segmentRemaining);
                return true;
            }

            remainingLookAhead -= segmentRemaining;
            cursor = segmentEnd;
        }

        lookAheadPoint = corners[corners.Length - 1];
        return true;
    }

    static Vector3 ClosestPointOnPlanarSegment(
        Vector3 point,
        Vector3 segmentStart,
        Vector3 segmentEnd,
        out float segmentT)
    {
        Vector3 startPlanar = new Vector3(segmentStart.x, 0f, segmentStart.z);
        Vector3 endPlanar = new Vector3(segmentEnd.x, 0f, segmentEnd.z);
        Vector3 pointPlanar = new Vector3(point.x, 0f, point.z);
        Vector3 segment = endPlanar - startPlanar;
        float lengthSquared = segment.sqrMagnitude;
        segmentT = lengthSquared > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(pointPlanar - startPlanar, segment) / lengthSquared)
            : 0f;
        return Vector3.Lerp(segmentStart, segmentEnd, segmentT);
    }

    bool TryBuildApproachPath(
        Vector3 targetPosition,
        Vector3 desiredStopFacing,
        out Vector3 stopPosition)
    {
        EnsurePath();
        stopPosition = default;
        Vector3 robotPosition = robotBody.position;
        if (!NavMesh.SamplePosition(robotPosition, out NavMeshHit startHit, 2f, NavMesh.AllAreas))
            return false;

        Vector3 startNavPosition = startHit.position;
        Vector3 fixedFacing = Vector3.ProjectOnPlane(desiredStopFacing, Vector3.up);
        if (fixedFacing.sqrMagnitude > 0.0001f)
            return TryCandidate(-fixedFacing.normalized, targetPosition, startNavPosition, out stopPosition);

        Vector3 targetToRobot = Vector3.ProjectOnPlane(robotPosition - targetPosition, Vector3.up);
        if (targetToRobot.sqrMagnitude < 0.0001f)
            targetToRobot = -robotBody.forward;

        Vector3 baseDirection = targetToRobot.normalized;
        int steps = Mathf.FloorToInt(maxCandidateAngle / Mathf.Max(1f, candidateAngleStep));

        for (int step = 0; step <= steps; step++)
        {
            if (step == 0 && TryCandidate(baseDirection, targetPosition, startNavPosition, out stopPosition))
                return true;

            if (step == 0)
                continue;

            float angle = step * candidateAngleStep;
            Vector3 left = Quaternion.AngleAxis(-angle, Vector3.up) * baseDirection;
            if (TryCandidate(left, targetPosition, startNavPosition, out stopPosition))
                return true;

            Vector3 right = Quaternion.AngleAxis(angle, Vector3.up) * baseDirection;
            if (TryCandidate(right, targetPosition, startNavPosition, out stopPosition))
                return true;
        }

        return false;
    }

    bool TryCandidate(
        Vector3 directionFromTarget,
        Vector3 targetPosition,
        Vector3 startNavPosition,
        out Vector3 sampledPosition)
    {
        Vector3 approachDirection = directionFromTarget.normalized;
        Vector3 desiredFacing = -approachDirection;
        Vector3 desiredLeft = Vector3.Cross(desiredFacing, Vector3.up).normalized;
        Vector3 exactStop = targetPosition +
                            approachDirection * stopDistance +
                            desiredLeft * stopLeftOffset;
        exactStop.y = startNavPosition.y;
        if (!NavMesh.SamplePosition(
                exactStop,
                out NavMeshHit exactStopHit,
                finalStopNavMeshSampleRadius,
                NavMesh.AllAreas))
        {
            sampledPosition = default;
            return false;
        }
        exactStop = exactStopHit.position;

        // Keep the orange hand-off point on the requested docking axis whenever
        // NavMesh permits it. Only widen symmetrically when that point is not
        // reachable; choosing the robot-arrival side first can leave the local
        // controller with a large and unreliable lateral correction.
        float preAlignmentPathOffset = alignmentStartDistance;
        int deviationSteps = Mathf.FloorToInt(
            handoffMaxDeviationDegrees / Mathf.Max(1f, candidateAngleStep));
        for (int step = 0; step <= deviationSteps; step++)
        {
            if (step == 0)
            {
                if (TryPreAlignmentDirection(
                        approachDirection,
                        exactStop,
                        preAlignmentPathOffset,
                        startNavPosition))
                {
                    sampledPosition = exactStop;
                    return true;
                }
                continue;
            }

            float angle = step * candidateAngleStep;
            Vector3 left = Quaternion.AngleAxis(-angle, Vector3.up) * approachDirection;
            if (TryPreAlignmentDirection(
                    left,
                    exactStop,
                    preAlignmentPathOffset,
                    startNavPosition))
            {
                sampledPosition = exactStop;
                return true;
            }

            Vector3 right = Quaternion.AngleAxis(angle, Vector3.up) * approachDirection;
            if (TryPreAlignmentDirection(
                    right,
                    exactStop,
                    preAlignmentPathOffset,
                    startNavPosition))
            {
                sampledPosition = exactStop;
                return true;
            }
        }

        sampledPosition = default;
        return false;
    }

    bool TryPreAlignmentDirection(
        Vector3 directionFromStop,
        Vector3 exactStop,
        float offset,
        Vector3 startNavPosition)
    {
        Vector3 requestedPoint = exactStop + directionFromStop.normalized * offset;
        if (!NavMesh.SamplePosition(
                requestedPoint,
                out NavMeshHit hit,
                navMeshSampleRadius,
                NavMesh.AllAreas))
            return false;

        if (!NavMesh.CalculatePath(startNavPosition, hit.position, NavMesh.AllAreas, _path) ||
            _path.status != NavMeshPathStatus.PathComplete)
            return false;

        _pathEndPosition = hit.position;
        _preAlignmentPosition = requestedPoint;
        return true;
    }

    void CompleteNavigation()
    {
        Vector3 currentFacing = Vector3.ProjectOnPlane(robotBody.forward, Vector3.up).normalized;
        Vector3 desiredFacing = Vector3.ProjectOnPlane(DesiredStopFacing, Vector3.up).normalized;
        float finalFacingError = currentFacing.sqrMagnitude > 0.0001f &&
                                 desiredFacing.sqrMagnitude > 0.0001f
            ? Mathf.Abs(Vector3.SignedAngle(currentFacing, desiredFacing, Vector3.up))
            : 0f;
        Vector3 finalPositionError = Vector3.ProjectOnPlane(
            StopPosition - robotBody.position,
            Vector3.up);
        Vector3 finalLocalError = robotBody.InverseTransformDirection(finalPositionError);
        Debug.Log(
            $"[ElderCareNavigation] Stop accepted; position error " +
            $"{PlanarDistance(robotBody.position, StopPosition):F3} m, " +
            $"forward/back error {Mathf.Abs(finalLocalError.z):F3} m, " +
            $"left/right error {Mathf.Abs(finalLocalError.x):F3} m, " +
            $"facing error {finalFacingError:F1} deg.",
            this);

        IsNavigating = false;
        _isFinalizingStop = false;
        StopMotion();
        RestoreKeyboardMode();
        if (!keepPathAfterArrival)
            ClearPathVisualization();
        if (IsReturningHome)
        {
            IsReturningHome = false;
            Debug.Log(
                "[ElderCareNavigation] Post-grasp return complete at the captured Play-start pose.",
                this);
            ReturnedHome?.Invoke();
        }
        else
            Arrived?.Invoke();
    }

    bool Fail(string reason)
    {
        IsNavigating = false;
        IsReturningHome = false;
        _postGraspRetreatActive = false;
        StopMotion();
        RestoreKeyboardMode();
        ClearPathVisualization();
        Debug.LogError($"[ElderCareNavigation] {reason}", this);
        NavigationFailed?.Invoke(reason);
        return false;
    }

    void StopMotion()
    {
        if (agent == null)
            return;

        agent.SetAutomaticReverseHeld(false);
        agent.vr = 0f;
        agent.vd = 0f;
        agent.wr = 0f;
    }

    void RestoreKeyboardMode()
    {
        if (!_ownsKeyboardMode || agent == null)
            return;

        agent.keyboard = _savedKeyboardMode;
        _ownsKeyboardMode = false;
    }

    void AutoAssignRobot()
    {
        if (agent == null)
            agent = FindFirstObjectByType<X02newAgent>();
        if (robotBody == null && agent != null)
        {
            ArticulationBody[] bodies = agent.GetComponentsInChildren<ArticulationBody>();
            foreach (ArticulationBody body in bodies)
            {
                if (!body.isRoot)
                    continue;

                _robotRootBody = body;
                robotBody = body.transform;
                break;
            }

            if (robotBody == null)
                robotBody = agent.transform;
        }

        if (_robotRootBody == null && robotBody != null)
            _robotRootBody = robotBody.GetComponent<ArticulationBody>();

    }

    bool IsPhysicalTranslationStopped()
    {
        return IsPhysicalTranslationStopped(physicalStopLinearSpeed);
    }

    bool IsPhysicalTranslationStopped(float maximumSpeed)
    {
        if (_robotRootBody == null)
            return true;

        return CurrentPhysicalPlanarSpeed() <= maximumSpeed;
    }

    float CurrentPhysicalPlanarSpeed()
    {
        if (_robotRootBody == null)
            return 0f;

        Vector3 planarVelocity = Vector3.ProjectOnPlane(_robotRootBody.velocity, Vector3.up);
        return planarVelocity.magnitude;
    }

    bool IsSafeHandoffStandstill()
    {
        bool upright = Vector3.Angle(robotBody.up, Vector3.up) <= maxHandoffTiltDegrees;
        return upright &&
               IsPhysicalTranslationStopped(handoffPhysicalStopSpeed);
    }

    bool IsPhysicalRotationStopped()
    {
        if (_robotRootBody == null)
            return true;

        return Mathf.Abs(_robotRootBody.angularVelocity.y) <= physicalStopAngularSpeed;
    }

    void EnsurePath()
    {
        if (_path == null)
            _path = new NavMeshPath();
    }

    void UpdatePathVisualization()
    {
        if (!showPlannedPath || _path == null || _path.corners == null || _path.corners.Length == 0)
        {
            ClearPathVisualization();
            return;
        }

        EnsurePathLine();
        Vector3[] corners = _path.corners;
        // Cyan shows only the real NavMesh route. The local terminal segment
        // from the orange hand-off marker to the green stop marker is not a
        // NavMesh path and must not be drawn as one.
        _pathLine.positionCount = corners.Length;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 position = corners[i];
            position.y += pathHeightOffset;
            _pathLine.SetPosition(i, position);
        }

        _pathLine.startWidth = pathLineWidth;
        _pathLine.endWidth = pathLineWidth;
        _pathLine.startColor = pathColor;
        _pathLine.endColor = pathColor;
        _pathLine.enabled = true;
        _hasPlannedPath = true;
    }

    void EnsurePathLine()
    {
        if (_pathLine != null)
            return;

        GameObject lineObject = new GameObject("PlannedPathLine");
        lineObject.transform.SetParent(transform, false);
        _pathLine = lineObject.AddComponent<LineRenderer>();
        _pathLine.useWorldSpace = true;
        _pathLine.loop = false;
        _pathLine.numCapVertices = 4;
        _pathLine.numCornerVertices = 4;
        _pathLine.textureMode = LineTextureMode.Stretch;
        _pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _pathLine.receiveShadows = false;
        _pathLine.sortingOrder = 1000;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            _pathMaterial = new Material(shader) { name = "ElderCare Planned Path (Runtime)" };
            _pathLine.material = _pathMaterial;
        }
    }

    void ClearPathVisualization()
    {
        _hasPlannedPath = false;
        if (_stopMarkerLine != null)
            _stopMarkerLine.enabled = false;
        if (_preAlignmentMarkerLine != null)
            _preAlignmentMarkerLine.enabled = false;
        if (_pathLine == null)
            return;

        _pathLine.positionCount = 0;
        _pathLine.enabled = false;
    }

    void UpdateStopVisualization()
    {
        if (!showStopMarker)
        {
            if (_stopMarkerLine != null)
                _stopMarkerLine.enabled = false;
            return;
        }

        EnsureStopMarker();
        const int segmentCount = 40;
        _stopMarkerLine.positionCount = segmentCount;
        for (int i = 0; i < segmentCount; i++)
        {
            float angle = i * Mathf.PI * 2f / segmentCount;
            _stopMarkerLine.SetPosition(
                i,
                StopPosition + new Vector3(
                    Mathf.Cos(angle) * stopMarkerRadius,
                    pathHeightOffset,
                    Mathf.Sin(angle) * stopMarkerRadius));
        }
        _stopMarkerLine.startWidth = pathLineWidth * 1.5f;
        _stopMarkerLine.endWidth = pathLineWidth * 1.5f;
        _stopMarkerLine.startColor = stopMarkerColor;
        _stopMarkerLine.endColor = stopMarkerColor;
        _stopMarkerLine.enabled = true;
    }

    void UpdatePreAlignmentVisualization()
    {
        if (!showPreAlignmentMarker)
        {
            if (_preAlignmentMarkerLine != null)
                _preAlignmentMarkerLine.enabled = false;
            return;
        }

        EnsurePreAlignmentMarker();
        const int segmentCount = 40;
        _preAlignmentMarkerLine.positionCount = segmentCount;
        for (int i = 0; i < segmentCount; i++)
        {
            float angle = i * Mathf.PI * 2f / segmentCount;
            _preAlignmentMarkerLine.SetPosition(
                i,
                _preAlignmentPosition + new Vector3(
                    Mathf.Cos(angle) * preAlignmentMarkerRadius,
                    pathHeightOffset,
                    Mathf.Sin(angle) * preAlignmentMarkerRadius));
        }
        _preAlignmentMarkerLine.startWidth = pathLineWidth * 1.5f;
        _preAlignmentMarkerLine.endWidth = pathLineWidth * 1.5f;
        _preAlignmentMarkerLine.startColor = preAlignmentMarkerColor;
        _preAlignmentMarkerLine.endColor = preAlignmentMarkerColor;
        _preAlignmentMarkerLine.enabled = true;
    }

    void EnsurePreAlignmentMarker()
    {
        if (_preAlignmentMarkerLine != null)
            return;

        GameObject markerObject = new GameObject("PreAlignmentMarker");
        markerObject.transform.SetParent(transform, false);
        _preAlignmentMarkerLine = markerObject.AddComponent<LineRenderer>();
        _preAlignmentMarkerLine.useWorldSpace = true;
        _preAlignmentMarkerLine.loop = true;
        _preAlignmentMarkerLine.numCapVertices = 4;
        _preAlignmentMarkerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _preAlignmentMarkerLine.receiveShadows = false;
        _preAlignmentMarkerLine.sortingOrder = 1002;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            _preAlignmentMarkerMaterial = new Material(shader)
            {
                name = "ElderCare Pre-Alignment Marker (Runtime)"
            };
            _preAlignmentMarkerMaterial.color = preAlignmentMarkerColor;
            if (_preAlignmentMarkerMaterial.HasProperty("_BaseColor"))
                _preAlignmentMarkerMaterial.SetColor("_BaseColor", preAlignmentMarkerColor);
            _preAlignmentMarkerLine.material = _preAlignmentMarkerMaterial;
        }
    }

    void EnsureStopMarker()
    {
        if (_stopMarkerLine != null)
            return;

        GameObject markerObject = new GameObject("PlannedStopMarker");
        markerObject.transform.SetParent(transform, false);
        _stopMarkerLine = markerObject.AddComponent<LineRenderer>();
        _stopMarkerLine.useWorldSpace = true;
        _stopMarkerLine.loop = true;
        _stopMarkerLine.numCapVertices = 4;
        _stopMarkerLine.numCornerVertices = 4;
        _stopMarkerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _stopMarkerLine.receiveShadows = false;
        _stopMarkerLine.sortingOrder = 1001;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            _stopMarkerMaterial = new Material(shader)
            {
                name = "ElderCare Stop Marker (Runtime)"
            };
            _stopMarkerMaterial.color = stopMarkerColor;
            if (_stopMarkerMaterial.HasProperty("_BaseColor"))
                _stopMarkerMaterial.SetColor("_BaseColor", stopMarkerColor);
            _stopMarkerLine.material = _stopMarkerMaterial;
        }
    }

    void OnDrawGizmos()
    {
        if (!showPlannedPath || !_hasPlannedPath)
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(StopPosition + Vector3.up * pathHeightOffset, 0.06f);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(TargetPosition + Vector3.up * pathHeightOffset, 0.05f);
    }

    static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDisable()
    {
        CancelNavigation();
    }

    void OnDestroy()
    {
        if (_pathMaterial != null)
            Destroy(_pathMaterial);
        if (_stopMarkerMaterial != null)
            Destroy(_stopMarkerMaterial);
        if (_preAlignmentMarkerMaterial != null)
            Destroy(_preAlignmentMarkerMaterial);
    }
}
