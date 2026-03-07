using UnityEngine;

/// <summary>
/// Encapsulates the automated flying simulation sequence.
///
/// Reproducibility guarantees:
///   1. Every repeat snaps transform to a fixed anchor (position + yaw) at FlyUp start.
///      Physics state never carries over from the previous repeat.
///   2. velocity and maxControllerDistance are zeroed at EVERY phase boundary
///      via AdvancePhase() → no accumulated state bleeds between phases.
///   3. TurnLeft and TurnRight use identical arm geometry (same TurnArmY magnitude,
///      only the sign differs) → symmetric rotational stimuli.
///   4. FlyUp targets an ABSOLUTE altitude (_anchorPos.y + FlyUpLift), fixed at
///      phase start → target never drifts with physics overshoot.
///   5. RotatePlayer hard-snaps to an exact multiple of 90°, eliminating
///      floating-point residual so each repeat begins at a clean yaw.
///   6. Velocity is reset before TurnLeft/TurnRight start (via AdvancePhase),
///      so both turns always see the same initial speed → comparable results.
///
/// Phase sequence per repeat (× TotalRepeats):
///   FlyUp → FlyForward → TurnLeft → TurnRight → Glide → RotatePlayer
/// </summary>
public class FlyingSimulator
{
    /// <summary>
    /// Runtime options that trade physical continuity for stronger reproducibility.
    /// Defaults are tuned for deterministic simulation runs.
    /// </summary>
    public struct SimulationOptions
    {
        public bool resetKinematicStateOnPhaseStart;
        public bool normalizeYawOnPhaseStart;
        public bool mirrorTurnRightFromTurnLeftStart;
        public bool keepFixedAnchorAcrossRepeats;
        public bool cycleCardinalDirectionsAcrossRepeats;

        public static SimulationOptions DeterministicDefault => new SimulationOptions
        {
            resetKinematicStateOnPhaseStart      = true,
            normalizeYawOnPhaseStart             = true,
            mirrorTurnRightFromTurnLeftStart     = true,
            keepFixedAnchorAcrossRepeats         = true,
            cycleCardinalDirectionsAcrossRepeats = true
        };
    }

    /// <summary>
    /// Immutable state snapshot that can be asserted in tests.
    /// </summary>
    public struct SimulationSnapshot
    {
        public string phase;
        public int repeatIndex;
        public float phaseTimer;
        public Vector3 anchorPos;
        public Quaternion anchorYaw;
        public float targetY;
        public bool phaseJustStarted;
    }

    // -----------------------------------------------------------------------
    // Phase enum
    // -----------------------------------------------------------------------

    private enum SimPhase
    {
        FlyUp,
        FlyForward,
        TurnLeft,
        TurnRight,
        Glide,
        RotatePlayer,
        Done
    }

    // -----------------------------------------------------------------------
    // Tuning constants
    // -----------------------------------------------------------------------

    /// <summary>Absolute altitude gain per repeat (from anchor Y, not cumulative).</summary>
    private const float FlyUpLift           = 10f;
    /// <summary>Fallback timeout if the target altitude is never reached.</summary>
    private const float SimFlyUpDuration    = 3f;
    private const float SimForwardDuration  = 3f;
    /// <summary>Both TurnLeft and TurnRight share this duration → equal exposure time.</summary>
    private const float SimTurnDuration     = 2f;
    private const float SimGlideDuration    = 2.5f;
    private const float SimRotateDuration   = 1f;

    // Arm geometry – all positions relative to transform.right so they automatically
    // follow the player's current facing direction.
    private const float ShoulderHalf = 0.5f;   // neutral lateral half-width
    private const float GlideHalf    = 1.2f;   // wide glide pose

    /// <summary>
    /// Vertical arm offset for turn phases.
    ///   TurnLeft:  left +TurnArmY, right -TurnArmY  (arm-angle ≈ +30°)
    ///   TurnRight: left -TurnArmY, right +TurnArmY  (arm-angle ≈ -30°)
    /// Identical magnitude → symmetric stimuli.
    /// (tan 30° × ShoulderHalf ≈ 0.289)
    /// </summary>
    private const float TurnArmY = 0.289f;

    /// <summary>
    /// Simulated Δpos magnitude for the flap input fed into
    /// CalculateCombinedControllerVelocity (prev position offset per frame).
    /// </summary>
    private const float FlapUpStrength      =  2f;
    private const float FlapForwardStrength = 15f;

    private const int TotalRepeats = 4;

    // -----------------------------------------------------------------------
    // Per-repeat anchor – set once, locked for the entire repeat
    // -----------------------------------------------------------------------

    /// <summary>World-space position snapped to at the start of each FlyUp.</summary>
    private Vector3    _anchorPos;
    /// <summary>Yaw-only quaternion locked at the start of each repeat.</summary>
    private Quaternion _anchorYaw;
    /// <summary>Absolute target Y for the current FlyUp phase.</summary>
    private float      _targetY;

    // -----------------------------------------------------------------------
    // Runtime state
    // -----------------------------------------------------------------------

    private SimPhase   _phase      = SimPhase.FlyUp;
    private int        _repeat     = 0;
    private float      _phaseTimer = 0f;
    private bool       _phaseJustStarted = true;

    /// <summary>Slerp start rotation for RotatePlayer.</summary>
    private Quaternion _rotateFrom;
    /// <summary>Slerp target rotation for RotatePlayer (hard-snapped exact multiple of 90°).</summary>
    private Quaternion _rotateTo;

    /// <summary>True once all repeats have finished.</summary>
    public bool IsDone => _phase == SimPhase.Done;
    public string CurrentPhaseName => _phase.ToString();

    private Vector3    _turnStartPos;
    private Quaternion _turnStartYaw;
    private bool       _hasTurnStartSnapshot;
    private float      _baseAnchorYawDeg;

    // -----------------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------------

    private readonly Logger.Logger _logger;

    /// <summary>Delegate pointing to <c>LocomotionTechnique.Fly()</c>.</summary>
    public delegate LocomotionTechnique.Movement FlyDelegate(
        Vector3 leftPos, Vector3 rightPos,
        Vector3 velocityPerSecond, Vector3 appliedVelocity,
        float   maxControllerDistance, float deltaTime);

    private readonly FlyDelegate _fly;
    private readonly SimulationOptions _options;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public FlyingSimulator(FlyDelegate fly, Logger.Logger logger)
        : this(fly, logger, SimulationOptions.DeterministicDefault)
    {
    }

    public FlyingSimulator(FlyDelegate fly, Logger.Logger logger, SimulationOptions options)
    {
        _fly    = fly;
        _logger = logger;
        _options = options;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Call once per Update() frame while SimulateFlying is active.
    /// Mutates <paramref name="transform"/>, <paramref name="velocity"/>, and
    /// <paramref name="maxControllerDistance"/> in-place via ref.
    /// </summary>
    public void Update(
        float     deltaTime,
        Transform transform,
        ref Vector3 velocity,
        ref float   maxControllerDistance)
    {
        if (_phase == SimPhase.Done)
            return;

        _phaseTimer += deltaTime;

        bool isFirstFrameInPhase = _phaseJustStarted;
        if (isFirstFrameInPhase)
        {
            ApplyPhaseStartNormalization(_phase, transform, ref velocity, ref maxControllerDistance);
            _phaseJustStarted = false;
        }

        // Default: neutral pose at shoulder width, no movement.
        Vector3 simL     = transform.position + transform.right * -ShoulderHalf;
        Vector3 simR     = transform.position + transform.right *  ShoulderHalf;
        Vector3 simPrevL = simL;
        Vector3 simPrevR = simR;

        switch (_phase)
        {
            // ----------------------------------------------------------------
            // FlyUp: flap upward until absolute target altitude is reached.
            // On first frame, snap to anchor → guaranteed identical start state.
            // ----------------------------------------------------------------
            case SimPhase.FlyUp:
            {
                if (isFirstFrameInPhase)
                {
                    // Hard-snap to anchor: eliminates any physics residual from
                    // the previous repeat or from RestartSimulation().
                    transform.position    = _anchorPos;
                    transform.rotation    = _anchorYaw;
                    velocity              = Vector3.zero;
                    maxControllerDistance = 0f;

                    _targetY = _anchorPos.y + FlyUpLift;
                    _logger.DebugLog(
                        $"[SimFlyUp] Repeat {_repeat + 1}/{TotalRepeats}. " +
                        $"Anchor: {_anchorPos} | Yaw: {_anchorYaw.eulerAngles.y:F1}° | " +
                        $"Ziel-Y: {_targetY:F2}");
                }

                    simL = transform.position + transform.right * -ShoulderHalf;
                    simR = transform.position + transform.right *  ShoulderHalf;

                float flapDelta = FlapUpStrength * deltaTime;
                simPrevL = simL + Vector3.up * flapDelta;
                simPrevR = simR + Vector3.up * flapDelta;

                _logger.DebugLog(
                    $"[SimFlyUp] {_phaseTimer:F2}s | Y: {transform.position.y:F2} / {_targetY:F2}");

                if (transform.position.y >= _targetY)
                {
                    _logger.InfoLog(
                        $"[SimFlyUp] Ziel erreicht ({transform.position.y:F2} >= {_targetY:F2}). → FlyForward.");
                    AdvancePhase(SimPhase.FlyForward, ref velocity, ref maxControllerDistance);
                }
                else if (_phaseTimer >= SimFlyUpDuration)
                {
                    _logger.WarningLog(
                        $"[SimFlyUp] Timeout ({SimFlyUpDuration}s). Y={transform.position.y:F2} nicht erreicht. → FlyForward.");
                    AdvancePhase(SimPhase.FlyForward, ref velocity, ref maxControllerDistance);
                }
                break;
            }

            // ----------------------------------------------------------------
            // FlyForward: flap with larger magnitude to build forward speed.
            // Velocity is reset by AdvancePhase before this phase starts.
            // ----------------------------------------------------------------
            case SimPhase.FlyForward:
            {
                if (isFirstFrameInPhase)
                    _logger.DebugLog(
                        $"[SimFlyForward] Start. Pos: {transform.position} | " +
                        $"Yaw: {transform.rotation.eulerAngles.y:F1}°");

                simL = transform.position + transform.right * -ShoulderHalf;
                simR = transform.position + transform.right *  ShoulderHalf;

                float flapDelta = FlapForwardStrength * deltaTime;
                simPrevL = simL + transform.forward * flapDelta;
                simPrevR = simR + transform.forward * flapDelta;

                _logger.DebugLog(
                    $"[SimFlyForward] {_phaseTimer:F2}s / {SimForwardDuration}s | Pos: {transform.position}");

                if (_phaseTimer >= SimForwardDuration)
                {
                    _logger.InfoLog(
                        $"[SimFlyForward] Abgeschlossen. Endpos: {transform.position}. → TurnLeft.");
                    AdvancePhase(SimPhase.TurnLeft, ref velocity, ref maxControllerDistance);
                }
                break;
            }

            // ----------------------------------------------------------------
            // TurnLeft: left arm UP, right arm DOWN  (arm-angle ≈ +30°).
            // simPrev == simCur → no flap impulse, pure static tilt pose.
            // Velocity is reset by AdvancePhase → clean start for both turns.
            // ----------------------------------------------------------------
            case SimPhase.TurnLeft:
            {
                if (isFirstFrameInPhase)
                {
                    // Snapshot TurnLeft start so TurnRight can begin from identical conditions.
                    _turnStartPos         = transform.position;
                    _turnStartYaw         = Quaternion.Euler(0f, Mathf.Round(transform.rotation.eulerAngles.y), 0f);
                    _hasTurnStartSnapshot = true;

                    _logger.DebugLog(
                        $"[SimTurnLeft] Start. Links +{TurnArmY:F3} / Rechts -{TurnArmY:F3} | " +
                        $"Yaw: {transform.rotation.eulerAngles.y:F1}°");
                }

                // Static pose: prev == cur → zero controller velocity, only arm angle matters.
                simL     = transform.position + transform.right * -ShoulderHalf + Vector3.up *  TurnArmY;
                simR     = transform.position + transform.right *  ShoulderHalf + Vector3.up * -TurnArmY;
                simPrevL = simL;
                simPrevR = simR;

                _logger.DebugLog(
                    $"[SimTurnLeft] {_phaseTimer:F2}s / {SimTurnDuration}s | " +
                    $"Yaw: {transform.rotation.eulerAngles.y:F1}°");

                if (_phaseTimer >= SimTurnDuration)
                {
                    _logger.InfoLog(
                        $"[SimTurnLeft] Abgeschlossen. Yaw: {transform.rotation.eulerAngles.y:F1}°. → TurnRight.");
                    AdvancePhase(SimPhase.TurnRight, ref velocity, ref maxControllerDistance);
                }
                break;
            }

            // ----------------------------------------------------------------
            // TurnRight: MIRROR of TurnLeft – identical TurnArmY magnitude,
            // sign inverted. Same duration, same velocity reset → symmetric.
            // ----------------------------------------------------------------
            case SimPhase.TurnRight:
            {
                if (isFirstFrameInPhase)
                    _logger.DebugLog(
                        $"[SimTurnRight] Start. Links -{TurnArmY:F3} / Rechts +{TurnArmY:F3} | " +
                        $"Yaw: {transform.rotation.eulerAngles.y:F1}°");

                simL     = transform.position + transform.right * -ShoulderHalf + Vector3.up * -TurnArmY;
                simR     = transform.position + transform.right *  ShoulderHalf + Vector3.up *  TurnArmY;
                simPrevL = simL;
                simPrevR = simR;

                _logger.DebugLog(
                    $"[SimTurnRight] {_phaseTimer:F2}s / {SimTurnDuration}s | " +
                    $"Yaw: {transform.rotation.eulerAngles.y:F1}°");

                if (_phaseTimer >= SimTurnDuration)
                {
                    _logger.InfoLog(
                        $"[SimTurnRight] Abgeschlossen. Yaw: {transform.rotation.eulerAngles.y:F1}°. → Glide.");
                    AdvancePhase(SimPhase.Glide, ref velocity, ref maxControllerDistance);
                }
                break;
            }

            // ----------------------------------------------------------------
            // Glide: arms fully extended (static pose, no flap input).
            // ----------------------------------------------------------------
            case SimPhase.Glide:
            {
                if (isFirstFrameInPhase)
                    _logger.DebugLog(
                        $"[SimGlide] Start. ArmHalf: {GlideHalf:F2} | maxDist: {maxControllerDistance:F3}");

                simL     = transform.position + transform.right * -GlideHalf;
                simR     = transform.position + transform.right *  GlideHalf;
                simPrevL = simL;
                simPrevR = simR;

                _logger.DebugLog(
                    $"[SimGlide] {_phaseTimer:F2}s / {SimGlideDuration}s | maxDist: {maxControllerDistance:F3}");

                if (_phaseTimer >= SimGlideDuration)
                {
                    maxControllerDistance = 0f;
                    _logger.InfoLog("[SimGlide] Abgeschlossen. maxDist → 0. → RotatePlayer.");
                    AdvancePhase(SimPhase.RotatePlayer, ref velocity, ref maxControllerDistance);
                }
                break;
            }

            // ----------------------------------------------------------------
            // RotatePlayer: smoothly interpolates to an exact +90° yaw snap.
            // Bypasses Fly() entirely – always returns early.
            // ----------------------------------------------------------------
            case SimPhase.RotatePlayer:
            {
                if (isFirstFrameInPhase)
                {
                    velocity = Vector3.zero;

                    // Quantize to exact 90-degree buckets to guarantee deterministic headings.
                    float fromDeg = QuantizeYawToRightAngle(transform.rotation.eulerAngles.y);
                    float toDeg   = fromDeg + 90f;
                    _rotateFrom = Quaternion.Euler(0f, fromDeg, 0f);
                    _rotateTo   = Quaternion.Euler(0f, toDeg,   0f);

                    _logger.DebugLog(
                        $"[SimRotatePlayer] Start. {fromDeg:F0}° → {toDeg:F0}°");
                }

                float t = Mathf.Clamp01(_phaseTimer / SimRotateDuration);
                transform.rotation = Quaternion.Slerp(_rotateFrom, _rotateTo, t);

                _logger.DebugLog(
                    $"[SimRotatePlayer] {_phaseTimer:F2}s | t: {t:F2} | " +
                    $"Yaw: {transform.rotation.eulerAngles.y:F1}°");

                if (_phaseTimer >= SimRotateDuration)
                {
                    // Hard-snap: guarantees _rotateTo is the exact stored quaternion,
                    // no Slerp residual.
                    transform.rotation = _rotateTo;
                    velocity           = Vector3.zero;
                    _repeat++;

                    _logger.InfoLog(
                        $"[SimRotatePlayer] Abgeschlossen. " +
                        $"Yaw: {transform.rotation.eulerAngles.y:F1}° | {_repeat}/{TotalRepeats}");

                    if (_repeat >= TotalRepeats)
                    {
                        _phase = SimPhase.Done;
                        _logger.InfoLog(
                            $"[SimulateFlying] ✓ Sequenz abgeschlossen ({TotalRepeats}/{TotalRepeats}).");
                    }
                    else
                    {
                        if (_options.cycleCardinalDirectionsAcrossRepeats)
                        {
                            // Repeat 1 uses base yaw, repeat 2/3/4 use +90/+180/+270.
                            float nextYawDeg = _baseAnchorYawDeg + (_repeat * 90f);
                            _anchorYaw = Quaternion.Euler(0f, QuantizeYawToRightAngle(nextYawDeg), 0f);
                        }

                        if (!_options.keepFixedAnchorAcrossRepeats)
                        {
                            // Optionally pin anchor to the just-finished repeat end pose.
                            _anchorPos = transform.position;

                            // If cardinal cycling is disabled, continue from end yaw.
                            if (!_options.cycleCardinalDirectionsAcrossRepeats)
                                _anchorYaw = _rotateTo;
                        }

                        _logger.InfoLog(
                            $"[SimulateFlying] Starte Repeat {_repeat + 1}/{TotalRepeats}. " +
                            $"Anchor: {_anchorPos} | Yaw: {_anchorYaw.eulerAngles.y:F1}°");

                        AdvancePhase(SimPhase.FlyUp, ref velocity, ref maxControllerDistance);
                    }
                }
                // RotatePlayer drives the transform directly → skip Fly().
                return;
            }
        }

        // ----------------------------------------------------------------
        // Translate simulated arm positions into controller velocity and call Fly().
        // ----------------------------------------------------------------
        Vector3 ctrlVelocity = LocomotionMath.CalculateCombinedControllerVelocity(
            simL, simR, simPrevL, simPrevR, deltaTime);

        // Transform local-space velocity to world space (yaw only, no roll distortion).
        Quaternion yawOnly = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
        ctrlVelocity = yawOnly * ctrlVelocity;

        _logger.DebugLog(
            $"[SimFly()] ctrlVel: {ctrlVelocity} mag: {ctrlVelocity.magnitude:F3} | " +
            $"curV: {velocity}");

        float ctrlHorizontal = new Vector2(ctrlVelocity.x, ctrlVelocity.z).magnitude;
        float velHorizontal = new Vector2(velocity.x, velocity.z).magnitude;
        _logger.InfoLog(
            $"[DiagSimPhase:{_phase}] dt={deltaTime:F3} | ctrlH={ctrlHorizontal:F3} ctrlY={ctrlVelocity.y:F3} | " +
            $"velH={velHorizontal:F3} velY={velocity.y:F3} | pos={transform.position}");

        LocomotionTechnique.Movement result = _fly(
            simL, simR, velocity, ctrlVelocity, maxControllerDistance, deltaTime);

        _logger.DebugLog(
            $"[SimFly()] → vel: {result.velocityPerSecond} | pos: {result.position}");

        float resultHorizontal = new Vector2(result.velocityPerSecond.x, result.velocityPerSecond.z).magnitude;
        _logger.InfoLog(
            $"[DiagSimResult:{_phase}] velH={resultHorizontal:F3} velY={result.velocityPerSecond.y:F3} | " +
            $"pos={result.position} | yaw={result.rotation.eulerAngles.y:F1}");

        velocity              = result.velocityPerSecond;
        maxControllerDistance = result.maxControllerDistance;
        transform.rotation    = result.rotation;
        transform.position    = result.position;
    }

    /// <summary>
    /// Resets the simulator to the beginning.
    /// <paramref name="startPos"/> and <paramref name="startYaw"/> become the
    /// anchor for repeat 0, so the simulation always starts from a known pose.
    /// </summary>
    public void Restart(Vector3 startPos, Quaternion startYaw)
    {
        _logger.InfoLog(
            $"[SimulateFlying] Neustart. War: Phase={_phase} Repeat={_repeat}");

        _phase      = SimPhase.FlyUp;
        _repeat     = 0;
        _phaseTimer = 0f;
        _phaseJustStarted = true;
        _hasTurnStartSnapshot = false;

        // Store yaw-only rotation to avoid any roll/pitch from startYaw.
        _anchorPos  = startPos;
        _baseAnchorYawDeg = QuantizeYawToRightAngle(startYaw.eulerAngles.y);
        _anchorYaw  = Quaternion.Euler(0f, _baseAnchorYawDeg, 0f);

        _logger.InfoLog(
            $"[SimulateFlying] Anchor: {_anchorPos} | Yaw: {_anchorYaw.eulerAngles.y:F0}°. Bereit.");
    }

    /// <summary>
    /// Returns a full simulator snapshot to simplify unit/integration assertions.
    /// </summary>
    public SimulationSnapshot GetSnapshot()
    {
        return new SimulationSnapshot
        {
            phase            = _phase.ToString(),
            repeatIndex      = _repeat,
            phaseTimer       = _phaseTimer,
            anchorPos        = _anchorPos,
            anchorYaw        = _anchorYaw,
            targetY          = _targetY,
            phaseJustStarted = _phaseJustStarted
        };
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transitions to <paramref name="next"/> and resets all physics state.
    /// This is the single choke-point that guarantees no bleed between phases.
    /// </summary>
    private void AdvancePhase(
        SimPhase next,
        ref Vector3 velocity,
        ref float   maxControllerDistance)
    {
        _logger.InfoLog(
            $"[SimulateFlying] {_phase} → {next} | Timer: {_phaseTimer:F2}s");

        _phase                = next;
        _phaseTimer           = 0f;
        _phaseJustStarted     = true;
        velocity              = Vector3.zero;
        maxControllerDistance = 0f;
    }

    /// <summary>
    /// Centralized phase-entry normalization for deterministic tests/runs.
    /// </summary>
    private void ApplyPhaseStartNormalization(
        SimPhase phase,
        Transform transform,
        ref Vector3 velocity,
        ref float maxControllerDistance)
    {
        if (_options.normalizeYawOnPhaseStart)
        {
            float yaw = Mathf.Round(transform.rotation.eulerAngles.y);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        if (_options.resetKinematicStateOnPhaseStart)
        {
            velocity = Vector3.zero;
            maxControllerDistance = 0f;
        }

        if (_options.mirrorTurnRightFromTurnLeftStart &&
            phase == SimPhase.TurnRight &&
            _hasTurnStartSnapshot)
        {
            transform.position = _turnStartPos;
            transform.rotation = _turnStartYaw;
            velocity = Vector3.zero;
            maxControllerDistance = 0f;

            _logger.DebugLog(
                $"[SimTurnRight] Mirrored start from TurnLeft. Pos: {_turnStartPos} | " +
                $"Yaw: {_turnStartYaw.eulerAngles.y:F1}°");
        }
    }

    private static float QuantizeYawToRightAngle(float yawDeg)
    {
        return Mathf.Round(yawDeg / 90f) * 90f;
    }
}