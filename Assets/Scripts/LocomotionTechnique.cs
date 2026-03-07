using UnityEngine;
using Logger;
using UnityEngine.UIElements;

/// <summary>
/// Unity MonoBehaviour that orchestrates locomotion.
/// Handles input, time, physics and applies results to the transform.
/// All calculations are delegated to pure functions.
/// The automated simulation sequence is handled by <see cref="FlyingSimulator"/>.
/// </summary>
public class LocomotionTechnique : MonoBehaviour
{
    public OVRInput.Controller leftController;
    public OVRInput.Controller rightController;
    public GameObject hmd;

    private Vector3 previousLeftPos;
    private Vector3 previousRightPos;
    private Logger.Logger logger;

    private const float Gravity = -9.81f;

    public enum MovingMethod
    {
        Fly,
        Walk,
        SimulateFlying
    }

    public struct Movement
    {
        public Vector3    velocityPerSecond;
        public Quaternion rotation;
        public Vector3    position;
        public float      maxControllerDistance;
    }

    public MovingMethod currentMovingMethod      = MovingMethod.Fly;
    public float        maxControllerDistance    = 0f;
    public Vector3      currentVelocityPerSecond = Vector3.zero;
    public const float  MaxVelocityPerSecond     = 10f;

    [Header("Simulation Testability")]
    public bool simResetKinematicStateOnPhaseStart  = true;
    public bool simNormalizeYawOnPhaseStart         = true;
    public bool simMirrorTurnRightFromTurnLeftStart = true;
    public bool simKeepFixedAnchorAcrossRepeats     = true;
    public bool simCycleCardinalDirectionsAcrossRepeats = true;
    public bool verboseLocomotionDiagnostics        = true;

    private FlyingSimulator _flyingSimulator;

    /////////////////////////////////////////////////////////
    // These are for the game mechanism.
    public ParkourCounter       parkourCounter;
    public string               stage;
    public SelectionTaskMeasure selectionTaskMeasure;

    void Start()
    {
        logger = new Logger.Logger(LogLevel.Debug);

        previousLeftPos  = OVRInput.GetLocalControllerPosition(leftController);
        previousRightPos = OVRInput.GetLocalControllerPosition(rightController);

        FlyingSimulator.SimulationOptions simOptions = new FlyingSimulator.SimulationOptions
        {
            resetKinematicStateOnPhaseStart  = simResetKinematicStateOnPhaseStart,
            normalizeYawOnPhaseStart         = simNormalizeYawOnPhaseStart,
            mirrorTurnRightFromTurnLeftStart = simMirrorTurnRightFromTurnLeftStart,
            keepFixedAnchorAcrossRepeats     = simKeepFixedAnchorAcrossRepeats,
            cycleCardinalDirectionsAcrossRepeats = simCycleCardinalDirectionsAcrossRepeats
        };

        _flyingSimulator = new FlyingSimulator(Fly, logger, simOptions);
    }

    void Update()
    {
        Vector3 leftPos   = OVRInput.GetLocalControllerPosition(leftController);
        Vector3 rightPos  = OVRInput.GetLocalControllerPosition(rightController);
        float   deltaTime = Time.deltaTime;

        Vector3 controllerVelocity = LocomotionMath.CalculateCombinedControllerVelocity(
            leftPos, rightPos, previousLeftPos, previousRightPos, deltaTime);
        logger.DebugLog($"Current Combined Controller Velocity: {controllerVelocity}");

        HardReset();
        ResetGliding();
        SwitchMovingMode();

        logger.DebugLog($"Starting Velocity Per Second: {currentVelocityPerSecond}");
        logger.DebugLog($"Starting Transform Position: {transform.position}");
        logger.DebugLog($"Starting Transform Rotation: {transform.rotation.eulerAngles}");

        if (currentMovingMethod == MovingMethod.Fly)
        {
            Quaternion yawOnly = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
            controllerVelocity = yawOnly * controllerVelocity;
            logger.DebugLog($"Controller Velocity (world space): {controllerVelocity}");

            Movement newMovement = Fly(leftPos, rightPos, currentVelocityPerSecond, controllerVelocity, maxControllerDistance, deltaTime);
            ApplyMovement(newMovement);
        }
        else if (currentMovingMethod == MovingMethod.SimulateFlying)
        {
            _flyingSimulator.Update(deltaTime, transform, ref currentVelocityPerSecond, ref maxControllerDistance);
        }
        else
        {
            currentVelocityPerSecond = Vector3.zero;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        logger.DebugLog($"Final Velocity Per Second: {currentVelocityPerSecond}");
        logger.DebugLog($"Final Transform Position: {transform.position}");
        logger.DebugLog($"Final Transform Rotation: {transform.rotation.eulerAngles}");
        logger.DebugLog("--------------------------------------------------");

        previousLeftPos  = leftPos;
        previousRightPos = rightPos;

        ResetPosition();
    }

    private void ApplyMovement(Movement result)
    {
        currentVelocityPerSecond = result.velocityPerSecond;
        transform.rotation       = result.rotation;
        transform.position       = result.position;
        maxControllerDistance    = result.maxControllerDistance;
    }

    private void ResetGliding()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) &&
            OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            maxControllerDistance = 0f;
        }
    }

    private void HardReset()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) &&
            OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch))
        {
            currentVelocityPerSecond = Vector3.zero;
            transform.position       = Vector3.zero;
            maxControllerDistance    = 0f;
            logger.DebugLog("Hard Reset Triggered: Position, Velocity, and Max Horizontal Arm Length reset.");
        }
    }

    private void SwitchMovingMode()
    {
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            currentMovingMethod = MovingMethod.Fly;

        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
            currentMovingMethod = MovingMethod.Walk;
    }

    // Public so FlyingSimulator can reference it via delegate.
    public Movement Fly(
        Vector3 leftPos, Vector3 rightPos,
        Vector3 velocityPerSecond, Vector3 appliedVelocity,
        float   maxControllerDistance, float deltaTime)
    {
        Vector3 newVelocityPerSecond = velocityPerSecond;
        Vector3 startVelocity = velocityPerSecond;

        float armAngleDegree = LocomotionMath.CalculateArmAngleDegree(leftPos, rightPos);
        logger.DebugLog($"Arm Angle: {armAngleDegree} degrees");

        var (isGliding, newMaxDistance) = CalculateGliding(leftPos, rightPos, maxControllerDistance, armAngleDegree);
        logger.DebugLog($"Is Gliding: {isGliding}, Max Controller Distance: {newMaxDistance}");

        newVelocityPerSecond = AddAppliedVelocityBasedOnFlap(newVelocityPerSecond, appliedVelocity, deltaTime, isGliding);
        logger.DebugLog($"Velocity Per Second after applying Controller Movement: {newVelocityPerSecond}");

        newVelocityPerSecond += LocomotionMath.ApplyGravity(Gravity, deltaTime);
        logger.DebugLog($"Velocity Per Second after applying Gravity: {newVelocityPerSecond}");

        newVelocityPerSecond = LocomotionMath.ClampSpeed(newVelocityPerSecond, MaxVelocityPerSecond);
        logger.DebugLog($"Clamped Velocity Per Second after Flapping, Gliding: {newVelocityPerSecond}");

        float      percentageOfMaxSpeed = LocomotionMath.CalculatePercentageOfMaxSpeed(newVelocityPerSecond, MaxVelocityPerSecond);
        Quaternion addedYaw             = LocomotionMath.CalculateAddedYaw(armAngleDegree, percentageOfMaxSpeed, deltaTime);
        logger.DebugLog($"Percentage of Max Speed: {percentageOfMaxSpeed * 100f}% | Added Yaw: {addedYaw.eulerAngles}");

        newVelocityPerSecond = LocomotionMath.CalculateVelocityAfterAddedRotation(newVelocityPerSecond, addedYaw);
        logger.DebugLog($"Velocity Per Second after applying Added Rotation: {newVelocityPerSecond}");

        bool isGrounded = LocomotionMath.IsGrounded(transform.position);
        newVelocityPerSecond = LocomotionMath.CalculateVelocityAfterDrag(newVelocityPerSecond, isGrounded, deltaTime);
        logger.DebugLog($"Velocity Per Second after applying Drag: {newVelocityPerSecond}");

        newVelocityPerSecond = LocomotionMath.CalculateVelocityAfterCollision(newVelocityPerSecond, transform.position, deltaTime);
        logger.DebugLog($"Velocity Per Second after applying Collision: {newVelocityPerSecond}");

        Vector3 movement = newVelocityPerSecond * deltaTime;

        Vector3    forward = LocomotionMath.CalculateForwardDirection(leftPos, rightPos);
        Quaternion newRoll = LocomotionMath.CalculateRoll(armAngleDegree, forward);

        const float RotationSpeed = 5f;
        Quaternion  currentYaw    = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
        Quaternion  targetYaw     = currentYaw * addedYaw;
        Quaternion  newYaw        = Quaternion.Slerp(currentYaw, targetYaw, RotationSpeed * deltaTime);

        if (verboseLocomotionDiagnostics)
        {
            float horizontalStart = new Vector2(startVelocity.x, startVelocity.z).magnitude;
            float horizontalEnd = new Vector2(newVelocityPerSecond.x, newVelocityPerSecond.z).magnitude;
            logger.InfoLog(
                $"[DiagFlyPipeline] dt={deltaTime:F3} | arm={armAngleDegree:F2} | " +
                $"appliedVel={appliedVelocity} | vStart={startVelocity} (h={horizontalStart:F2}) | " +
                $"vEnd={newVelocityPerSecond} (h={horizontalEnd:F2}) | " +
                $"isGrounded={isGrounded} | addedYawY={addedYaw.eulerAngles.y:F2}");
        }

        return new Movement()
        {
            velocityPerSecond     = newVelocityPerSecond,
            rotation              = newYaw * newRoll,
            position              = transform.position + movement,
            maxControllerDistance = newMaxDistance
        };
    }

    private (bool isGliding, float newMaxDistance) CalculateGliding(
        Vector3 leftPos, Vector3 rightPos,
        float   currentMaxControllerDistance, float armAngleDegree)
    {
        float currentControllerDistance = LocomotionMath.CalculateArmDistance(leftPos, rightPos);
        float newMaxDistance            = currentMaxControllerDistance;
        bool  isGliding                 = LocomotionMath.CalculateIfGliding(armAngleDegree, currentControllerDistance, ref newMaxDistance);

        if (verboseLocomotionDiagnostics)
        {
            LocomotionMath.GlideDiagnostic glideDiagnostic = LocomotionMath.EvaluateGlideDiagnostic(
                armAngleDegree,
                currentControllerDistance,
                newMaxDistance);

            logger.InfoLog(
                $"[DiagGlide] arm={armAngleDegree:F2} | dist={currentControllerDistance:F3} | " +
                $"maxDist(prev={currentMaxControllerDistance:F3}, new={newMaxDistance:F3}) | " +
                $"threshold={glideDiagnostic.thresholdDistance:F3} | " +
                $"validDist={glideDiagnostic.validDistance} validAngle={glideDiagnostic.validAngle} | " +
                $"isGliding={isGliding}");
        }

        return (isGliding, newMaxDistance);
    }

    private Vector3 AddAppliedVelocityBasedOnFlap(
        Vector3 velocity, Vector3 controllerVelocity, float deltaTime, bool isGliding = false)
    {
        LocomotionMath.FlapDiagnostic flapDiagnostic = LocomotionMath.EvaluateFlapDiagnostic(controllerVelocity);
        Vector3 flapStrength = LocomotionMath.CalculateFlapStrength(
            controllerVelocity, transform.position.y, 30f, out bool isFlapping);
        logger.DebugLog($"Is Flapping: {isFlapping}, Flap Strength: {flapStrength}");

        if (verboseLocomotionDiagnostics)
        {
            logger.InfoLog(
                $"[DiagFlap] ctrlVel={controllerVelocity} | byY={flapDiagnostic.byVertical} " +
                $"byX={flapDiagnostic.byHorizontalX} byNegZ={flapDiagnostic.byForwardZ} | " +
                $"isFlapping={isFlapping} | isGliding={isGliding}");
        }

        if (isFlapping)
            velocity += flapStrength;
        else if (isGliding)
            velocity.y = LocomotionMath.CalculateGlideFallSpeed(velocity.y, -1f, deltaTime);

        return velocity;
    }

    private void ResetPosition()
    {
        if (OVRInput.Get(OVRInput.Button.Two) || OVRInput.Get(OVRInput.Button.Four))
        {
            if (parkourCounter.parkourStart)
                transform.position = parkourCounter.currentRespawnPos;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("banner"))
        {
            stage = other.gameObject.name;
            parkourCounter.isStageChange = true;
        }
        else if (other.CompareTag("objectInteractionTask"))
        {
            selectionTaskMeasure.isTaskStart    = true;
            selectionTaskMeasure.scoreText.text = "";
            selectionTaskMeasure.partSumErr     = 0f;
            selectionTaskMeasure.partSumTime    = 0f;
            float   tempValueY = other.transform.position.y > 0 ? 12 : 0;
            Vector3 tmpTarget  = new(hmd.transform.position.x, tempValueY, hmd.transform.position.z);
            selectionTaskMeasure.taskUI.transform.LookAt(tmpTarget);
            selectionTaskMeasure.taskUI.transform.Rotate(new Vector3(0, 180f, 0));
            selectionTaskMeasure.taskStartPanel.SetActive(true);
        }
        else if (other.CompareTag("coin"))
        {
            parkourCounter.coinCount += 1;
            GetComponent<AudioSource>().Play();
            other.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Restarts the simulation from the current transform pose.
    /// The current position and yaw become the fixed anchor for repeat 0.
    /// </summary>
    public void RestartSimulation()
    {
        currentVelocityPerSecond = Vector3.zero;
        maxControllerDistance    = 0f;
        _flyingSimulator.Restart(transform.position, transform.rotation);
    }
}
