using UnityEngine;
using Logger;
using UnityEngine.UIElements;

/// <summary>
/// Unity MonoBehaviour that orchestrates locomotion.
/// Handles input, time, physics and applies results to the currentOrientation.
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

    private const float Gravity = -9.81f;

    public bool runTests = false;

    [Header("runTests Configuration")]
    [SerializeField] private Vector3 testForwardDirection = Vector3.forward;
    [SerializeField] private ArmAngleMode testArmAngleMode = ArmAngleMode.Zero;
    [SerializeField] private DistanceMode testDistanceMode = DistanceMode.Zero;
    [SerializeField] private Vector3 testVelocityDirection = Vector3.forward;
    [SerializeField] private float testVelocityMagnitude = 10f;
    [SerializeField] private float testVerticalVelocity = 1f;

    public enum MovingMethod
    {
        Flying,
        Walking
    }

    public enum ArmAngleMode
    {
        Negative45,
        Zero,
        Positive45
    }

    public enum DistanceMode
    {
        Zero,
        Hundred
    }

    public MovingMethod currentMovingMethod = MovingMethod.Flying;
    public const float  MaxVelocityPerSecond = 10f;

    public Orientation currentOrientation;

    public ControllerVariables currentControllerVariables;

    public struct Orientation
    {
        public Vector3 position;
        public Quaternion yaw;
        public Quaternion roll;
        public Vector3 movementPerSecond;
        public float maxControllerDistance;
    }

    public struct ControllerVariables
    {
        public float armAngleDegree;
        public Vector3 forward;
        public float distance;
        public Vector3 velocityPerSecond;
    }

    /////////////////////////////////////////////////////////
    // These are for the game mechanism.
    public ParkourCounter       parkourCounter;
    public string               stage;
    public SelectionTaskMeasure selectionTaskMeasure;

    void Start()
    {
        Logger.Logger.Configure(LogLevel.Debug);

        previousLeftPos  = GetControllerWorldPosition(leftController);
        previousRightPos = GetControllerWorldPosition(rightController);

        currentOrientation = new Orientation()
        {
            position = transform.position,
            yaw = transform.rotation,
            roll = Quaternion.identity,
            movementPerSecond = Vector3.zero,
            maxControllerDistance = 1f
        };
    }

    void Update()
    {
        Vector3 leftPos = GetControllerWorldPosition(leftController);
        Vector3 rightPos = GetControllerWorldPosition(rightController);
        float deltaTime = Time.deltaTime;

        currentControllerVariables = UpdateControllerVariables(leftPos, rightPos, previousLeftPos, previousRightPos, deltaTime);

        if (runTests) {
            currentControllerVariables = ConfigureRunTestsControllerVariables();
        }

        if (currentMovingMethod == MovingMethod.Flying)
        {
            currentOrientation = Fly(currentOrientation, currentControllerVariables, deltaTime);
        }
        else if (currentMovingMethod == MovingMethod.Walking)
        {
            currentOrientation.yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            currentOrientation.roll = Quaternion.identity;
            currentOrientation.movementPerSecond = Vector3.zero;
            currentOrientation.position = transform.position;
        }
        
        UpdateTransform(currentOrientation);

        previousLeftPos = leftPos;
        previousRightPos = rightPos;

        HardReset();
        ResetGliding();
        SwitchMovingMode();
        ResetPosition();
    }

    private static ControllerVariables UpdateControllerVariables(Vector3 leftPos, Vector3 rightPos, Vector3 previousLeftPos, Vector3 previousRightPos, float deltaTime)
    {
        Vector3 controllerVelocity = LocomotionMath.CalculateCombinedControllerVelocity(leftPos, rightPos, previousLeftPos, previousRightPos, deltaTime);
        Vector3 forward = LocomotionMath.CalculateForwardDirection(leftPos, rightPos);
        float armAngleDegree = LocomotionMath.CalculateArmAngleDegree(leftPos, rightPos);
        float currentControllerDistance = LocomotionMath.CalculateArmDistance(leftPos, rightPos);

        return new ControllerVariables()
        {
            velocityPerSecond = controllerVelocity,
            forward = forward,
            distance = currentControllerDistance,
            armAngleDegree = armAngleDegree,
        };
    }

    private ControllerVariables ConfigureRunTestsControllerVariables()
    {
        Vector3 configuredForward = testForwardDirection.sqrMagnitude > 0f
            ? testForwardDirection.normalized
            : Vector3.forward;

        float configuredArmAngle = testArmAngleMode == ArmAngleMode.Positive45 ? 45f : -45f;
        configuredArmAngle = testArmAngleMode == ArmAngleMode.Zero ? 0f : configuredArmAngle;

        float configuredDistance = testDistanceMode == DistanceMode.Hundred ? 100f : 0f;

        Vector3 configuredVelocityDirection = testVelocityDirection.sqrMagnitude > 0f
            ? testVelocityDirection.normalized
            : Vector3.forward;

        Vector3 configuredVelocity = configuredVelocityDirection * testVelocityMagnitude;
        configuredVelocity.y = testVerticalVelocity;

        return new ControllerVariables
        {
            armAngleDegree = configuredArmAngle,
            forward = configuredForward,
            distance = configuredDistance,
            velocityPerSecond = configuredVelocity
        };
    }

    public void UpdateTransform(Orientation result)
    {
        Quaternion newRoll = result.roll;
        Quaternion newYaw = result.yaw;

        // Apply heading first.
        transform.rotation = newYaw;

        // Apply forward-oriented roll in world space to match CalculateForwardDirection axis.
        transform.rotation = newRoll * transform.rotation;
        transform.position = result.position;
    }

    private void ResetGliding()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) &&
            OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            currentOrientation.maxControllerDistance = 0f;
        }
    }

    private void HardReset()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) &&
            OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch))
        {
            currentOrientation.movementPerSecond = Vector3.zero;
            currentOrientation.position = Vector3.zero;
            currentOrientation.maxControllerDistance = 0f;
            Logger.Logger.DebugLog("Hard Reset Triggered: Position, Velocity, and Max Horizontal Arm Length reset.");
        }
    }

    private void SwitchMovingMode()
    {
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            currentMovingMethod = MovingMethod.Flying;

        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
            currentMovingMethod = MovingMethod.Walking;
    }

    // Public so FlyingSimulator can reference it via delegate.
    public static Orientation Fly(
        Orientation currentOrientation,
        ControllerVariables controllerVariables,
        float deltaTime)
    {
        Vector3 newMovementPerSecond = currentOrientation.movementPerSecond;
        Logger.Logger.DebugLog($"Current Movement Per Second: {newMovementPerSecond}");

        newMovementPerSecond += Flap(controllerVariables.velocityPerSecond, currentOrientation.position.y);
        Logger.Logger.DebugLog($"Movement Per Second after applying Controller Movement: {newMovementPerSecond}");

        (Quaternion addedYaw, Vector3 a) = LocomotionMath.VelocityWithRotation(newMovementPerSecond, controllerVariables.forward, controllerVariables.armAngleDegree, deltaTime);
        newMovementPerSecond = a;
        Logger.Logger.DebugLog($"Movement Per Second after applying Added Rotation: {newMovementPerSecond}");

        newMovementPerSecond = MovementWithEnvironment(currentOrientation.position, deltaTime, newMovementPerSecond);
        Logger.Logger.DebugLog($"Movement Per Second after applying Environment Effects: {newMovementPerSecond}");

        newMovementPerSecond = ClampedMovement(currentOrientation, controllerVariables, deltaTime, newMovementPerSecond);
        Logger.Logger.DebugLog($"Movement Per Second after Clamping: {newMovementPerSecond}");

        Quaternion newRoll = LocomotionMath.CalculateRoll(controllerVariables.armAngleDegree, controllerVariables.forward);

        Vector3 movement = newMovementPerSecond * deltaTime;

        float newMaxControllerDistance = Mathf.Max(currentOrientation.maxControllerDistance, controllerVariables.distance);

        return new Orientation()
        {
            movementPerSecond = newMovementPerSecond,
            yaw = currentOrientation.yaw * addedYaw,
            roll = newRoll,
            position = currentOrientation.position + movement,
            maxControllerDistance = newMaxControllerDistance
        };
    }


    private static Vector3 ClampedMovement(Orientation currentOrientation, ControllerVariables controllerVariables, float deltaTime, Vector3 newMovementPerSecond)
    {
        var isGliding = LocomotionMath.IsGliding(controllerVariables.distance, currentOrientation.maxControllerDistance, controllerVariables.armAngleDegree);
        if (isGliding)
        {
            newMovementPerSecond.y = LocomotionMath.GlideFallSpeed(newMovementPerSecond.y, deltaTime);
        }
        Logger.Logger.DebugLog($"Movement Per Second after Gliding: {newMovementPerSecond}, IsGliding: {isGliding}");

        newMovementPerSecond = LocomotionMath.ClampSpeed(newMovementPerSecond, MaxVelocityPerSecond);

        return newMovementPerSecond;
    }

    private static Vector3 Flap(Vector3 controllerVelocity, float currentHeight)
    {
        (Vector3 flapStrength, bool isFlapping) = LocomotionMath.CalculateFlapStrength(controllerVelocity, currentHeight);

        if (!isFlapping)
        {
            return Vector3.zero;
        }

        return flapStrength;
    }

    private static Vector3 MovementWithEnvironment(Vector3 position, float deltaTime, Vector3 newMovementPerSecond)
    {
        bool isGrounded = LocomotionMath.IsGrounded(position);
        newMovementPerSecond += LocomotionMath.Gravity(Gravity, deltaTime);
        newMovementPerSecond = LocomotionMath.VelocityAfterDrag(newMovementPerSecond, isGrounded, deltaTime);
        newMovementPerSecond = LocomotionMath.VelocityAfterCollision(newMovementPerSecond, position, deltaTime);

        return newMovementPerSecond;
    }

    private void ResetPosition()
    {
        bool resetPressed =
            OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) ||
            OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch);
    
        if (resetPressed && parkourCounter != null && parkourCounter.parkourStart)
            currentOrientation.position = parkourCounter.currentRespawnPos;
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

    private Vector3 GetControllerWorldPosition(OVRInput.Controller controller)
    {
        return Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f) * OVRInput.GetLocalControllerPosition(controller);
    }
}
