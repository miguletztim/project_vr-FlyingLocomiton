using System;
using UnityEngine;
using Logger;

/// <summary>
/// Unity MonoBehaviour that orchestrates locomotion.
/// Handles input, time, physics and applies results to the transform.
/// All calculations are delegated to pure functions.
/// </summary>
public class LocomotionTechnique : MonoBehaviour
{
    // Please implement your locomotion technique in this script. 
    public OVRInput.Controller leftController;
    public OVRInput.Controller rightController;
    public GameObject hmd;


    private Vector3 previousLeftPos;
    private Vector3 previousRightPos;
    private Logger.Logger logger;

    // Custom constants for game mechanism.
    private const float Gravity = -9.81f;
    
    public enum MovingMethod
    {
        Fly,
        Walk
    }


    // Customizable variables for gliding and game mechanism.
    public MovingMethod currentMovingMethod = MovingMethod.Fly;
    public float maxControllerDistance = 0f;
    public Vector3 currentVelocityPerSecond = Vector3.zero;
    public const float MaxVelocityPerSecond = 10f;


    /////////////////////////////////////////////////////////
    // These are for the game mechanism.
    public ParkourCounter parkourCounter;
    public string stage;
    public SelectionTaskMeasure selectionTaskMeasure;

    void Start()
    {
        logger = new Logger.Logger(LogLevel.Debug);

        previousLeftPos = OVRInput.GetLocalControllerPosition(leftController);
        previousRightPos = OVRInput.GetLocalControllerPosition(rightController);

        //TestYawCalculation();
    }

    public void TestYawCalculation()
    {
        float currentYaw;
        float armAngleRad = Mathf.PI / 6;
        Vector3 velocity = new Vector3(1f, 0f, 1f);
        float maxVelocity = 10f;
        float deltaTime = 1f;

        // Outer loop to rotate 1 degree clockwise each iteration
        for (int j = -360; j < 360; j++)
        {
            for (int i = -360; i < 360; i++)
            {
                currentYaw = i;
                float yaw = LocomotionMath.CalculateYaw(currentYaw, armAngleRad, velocity, maxVelocity, deltaTime);

                Vector3 forwardDirection = Quaternion.Euler(0, j, 0) * Vector3.forward; // Rotate forward direction by j degrees
                Quaternion orientation = LocomotionMath.CalculateOrientation(yaw, armAngleRad, forwardDirection);
                
                float roll = Mathf.Atan2(orientation.eulerAngles.z, orientation.eulerAngles.x) * Mathf.Rad2Deg;
                logger.DebugLog($"Iteration {i}: Current Yaw: {currentYaw}°, Calculated Yaw: {yaw}°, Orientation: {orientation.eulerAngles}, Roll: {roll}°");
            }
        }
    }

    void Update()
    {
        // ----------------------------
        // ARRANGE
        // ----------------------------
        Vector3 leftPos = OVRInput.GetLocalControllerPosition(leftController);
        Vector3 rightPos = OVRInput.GetLocalControllerPosition(rightController);
        float deltaTime = Time.deltaTime;
        
        HardReset();
        ResetGliding();
        SwitchMovingMode();

        // ----------------------------
        // ACT
        // ----------------------------
        if (currentMovingMethod == MovingMethod.Fly)
        {
            Fly(leftPos, rightPos, deltaTime);
        }
        else
        {
            currentVelocityPerSecond = Vector3.zero;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        previousLeftPos = leftPos;
        previousRightPos = rightPos;

        logger.DebugLog($"Position: {transform.position}, Rotation: {transform.rotation.eulerAngles}, Velocity: {currentVelocityPerSecond}");

        
        ////////////////////////////////////////////////////////////////////////////////
        // These are for the game mechanism.
        ResetPosition();
    }

    private void ResetGliding()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) && OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            maxControllerDistance = 0f;
        }
    }

    private void HardReset()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) && OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch))
        {
            currentVelocityPerSecond = Vector3.zero;
            transform.position = Vector3.zero;
            maxControllerDistance = 0f;
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

    private void Fly(Vector3 leftPos, Vector3 rightPos, float deltaTime)
    {
        // ----------------------------
        // ARRANGE
        // ----------------------------
        Vector3 controllerVelocity = CalculateCombinedControllerVelocity(leftPos, rightPos, deltaTime);

        // ----------------------------
        // ACT (PURE LOGIC)
        // ----------------------------
        Vector3 flapStrength = LocomotionMath.CalculateFlapStrength(
            controllerVelocity,
            transform.position.y,
            30f,
            out bool isFlapping);
        logger.DebugLog($"Controller Velocity: {controllerVelocity}, Flap Strength: {flapStrength}, Is Flapping: {isFlapping}");

        float armAngle =
            LocomotionMath.CalculateArmAngle(leftPos, rightPos);
        logger.DebugLog($"Arm Angle: {armAngle * Mathf.Rad2Deg:F2} degrees");

        currentVelocityPerSecond = CalculateVelocityPerSecond(leftPos, rightPos, deltaTime, flapStrength, isFlapping, armAngle, currentVelocityPerSecond);


        // ----------------------------
        // ROTATION
        // ----------------------------
        /*
        Vector3 forward =
            LocomotionMath.CalculateForwardDirection(leftPos, rightPos);
        logger.DebugLog($"Forward Direction: {forward}");

        float yaw =
            LocomotionMath.CalculateYaw(
                transform.eulerAngles.y,
                armAngle,
                velocityPerSecond,
                MaxVelocity,
                deltaTime);
        logger.DebugLog($"Calculated Yaw: {yaw} degrees");

        logger.DebugLog($"Current Rotation: {transform.rotation.eulerAngles}");
        logger.DebugLog($"Applying Rotation - Yaw: {yaw} degrees, Arm Angle: {armAngle * Mathf.Rad2Deg:F2} degrees");
        transform.rotation =
            Quaternion.Euler(0f, yaw, 0f) *
            LocomotionMath.CalculateRoll(armAngle, forward);
        logger.DebugLog($"New Rotation: {transform.rotation.eulerAngles}");
        */

        // Yaw
        float yaw =
            LocomotionMath.CalculateYaw(
            transform.eulerAngles.y,
            armAngle,
            currentVelocityPerSecond,
            MaxVelocity,
            deltaTime);

        logger.DebugLog($"Calculated Yaw: {yaw} degrees");

        logger.DebugLog($"Current Rotation: {transform.rotation.eulerAngles}");
        logger.DebugLog($"Applying Rotation - Yaw: {yaw} degrees, Arm Angle: {armAngle * Mathf.Rad2Deg:F2} degrees");

        // Yaw und Roll als unabhängige Achsen kombinieren
        // Reihenfolge wichtig: erst Yaw (Y-Achse), dann Roll (Z-Achse)
        Quaternion yawRotation = Quaternion.AngleAxis(yaw, Vector3.up);
        Quaternion rollRotation = Quaternion.AngleAxis(armAngle * Mathf.Rad2Deg, Vector3.forward);

        // Roll im lokalen Raum anwenden, Yaw im Weltraum
        transform.rotation = yawRotation * rollRotation;


        // ----------------------------
        // MOVEMENT
        // ----------------------------
        Vector3 movement =
            LocomotionMath.CalculateMovement(
                transform.rotation,
                currentVelocityPerSecond,
                deltaTime);

        // ----------------------------
        // COLLISION (UNITY)
        // ----------------------------
        if (Physics.SphereCast(
            transform.position,
            0.2f,
            movement.normalized,
            out RaycastHit hit,
            movement.magnitude))
        {
            bool grounded =
                LocomotionMath.IsGrounded(hit.normal, 45f);

            movement =
                LocomotionMath.ResolveCollision(
                    currentVelocityPerSecond,
                    grounded);
        }

        movement =
            LocomotionMath.ApplyDrag(
                movement,
                false,
                deltaTime);
        
        logger.DebugLog($"Final Movement Vector: {movement}");

        // ----------------------------
        // ASSERT (SIDE EFFECT)
        // ----------------------------
        transform.position += movement;
    }

    private Vector3 CalculateVelocityPerSecond(Vector3 leftPos, Vector3 rightPos, float deltaTime, Vector3 flapStrength, bool isFlapping, float armAngle, Vector3 currentVelocityPerSecond)
    {
        if (isFlapping)
        {
            currentVelocityPerSecond += flapStrength;
            logger.DebugLog($"Velocity after flap: {currentVelocityPerSecond}");
        }
        else
        {
            float currentControllerDistance = LocomotionMath.CalculateArmDistance(leftPos, rightPos);
            bool isGliding = LocomotionMath.CalculateIfGliding(armAngle, currentControllerDistance, ref maxControllerDistance);

            logger.DebugLog($"Is Gliding: {isGliding}, Max Horizontal Arm Length: {maxControllerDistance}");

            if(isGliding)
            {
                currentVelocityPerSecond.y =
                LocomotionMath.CalculateGlideFallSpeed(
                    currentVelocityPerSecond.y,
                    -1f,
                    deltaTime);
            }
        }

        currentVelocityPerSecond += LocomotionMath.ApplyGravity(Gravity, deltaTime);
        logger.DebugLog($"Velocity after gravity: {currentVelocityPerSecond}");

        currentVelocityPerSecond =LocomotionMath.ClampSpeed(currentVelocityPerSecond, MaxVelocity);        
        logger.DebugLog($"Clamped Velocity: {currentVelocityPerSecond}");

        return currentVelocityPerSecond;
    }

    private Vector3 CalculateCombinedControllerVelocity(Vector3 leftPos, Vector3 rightPos, float deltaTime)
    {
        Vector3 leftOffset = leftPos - previousLeftPos;
        Vector3 rightOffset = rightPos - previousRightPos;
        Vector3 controllerVelocity = -(leftOffset + rightOffset) / deltaTime;
        return controllerVelocity;
    }

    private void ResetPosition()
    {
        if (OVRInput.Get(OVRInput.Button.Two) || OVRInput.Get(OVRInput.Button.Four))
        {
            if (parkourCounter.parkourStart)
            {
                transform.position = parkourCounter.currentRespawnPos;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {

        // These are for the game mechanism.
        if (other.CompareTag("banner"))
        {
            stage = other.gameObject.name;
            parkourCounter.isStageChange = true;
        }
        else if (other.CompareTag("objectInteractionTask"))
        {
            selectionTaskMeasure.isTaskStart = true;
            selectionTaskMeasure.scoreText.text = "";
            selectionTaskMeasure.partSumErr = 0f;
            selectionTaskMeasure.partSumTime = 0f;
            // rotation: facing the user's entering direction
            float tempValueY = other.transform.position.y > 0 ? 12 : 0;
            Vector3 tmpTarget = new(hmd.transform.position.x, tempValueY, hmd.transform.position.z);
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
        // These are for the game mechanism.
    }
}