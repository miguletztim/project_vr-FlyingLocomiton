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

    private const float MaxVelocity = 10f;
    private const float Gravity = -9.81f;

    private Vector3 previousLeftPos;
    private Vector3 previousRightPos;
    private Vector3 velocityPerSecond;

    private float maxHorizontalArmLength;

    private Logger.Logger logger;

    private enum MovingMethod
    {
        Fly,
        Walk
    }

    private MovingMethod currentMovingMethod = MovingMethod.Fly;


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
            velocityPerSecond = Vector3.zero;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        previousLeftPos = leftPos;
        previousRightPos = rightPos;

        logger.DebugLog($"Position: {transform.position}, Rotation: {transform.rotation.eulerAngles}, Velocity: {velocityPerSecond}");
    }

    private void ResetGliding()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) && OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            maxHorizontalArmLength = 0f;
        }
    }

    private void HardReset()
    {
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) && OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch))
        {
            velocityPerSecond = Vector3.zero;
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            maxHorizontalArmLength = 0f;
            logger.DebugLog("Hard Reset Triggered: Position, Rotation, Velocity, and Max Horizontal Arm Length reset.");
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
        Vector3 leftOffset = leftPos - previousLeftPos;
        Vector3 rightOffset = rightPos - previousRightPos;
        Vector3 controllerVelocity = -(leftOffset + rightOffset) / deltaTime;

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
        
        bool isGliding = LocomotionMath.CalculateIfGliding(leftPos, rightPos, ref maxHorizontalArmLength);
        logger.DebugLog($"Max Horizontal Arm Length: {maxHorizontalArmLength}, Is Gliding: {isGliding}");

        if (isFlapping)
        {
            velocityPerSecond += flapStrength;
            logger.DebugLog($"Velocity after flap: {velocityPerSecond}");
        }
        else if (Mathf.Abs(armAngle) < 0.3f &&  isGliding)
        {
            velocityPerSecond.y =
                LocomotionMath.CalculateGlideFallSpeed(
                    velocityPerSecond.y,
                    -1f,
                    deltaTime);
        }
        
        velocityPerSecond += LocomotionMath.ApplyGravity(Gravity, deltaTime);
        logger.DebugLog($"Velocity after gravity: {velocityPerSecond}");

        velocityPerSecond =
            LocomotionMath.ClampSpeed(
                velocityPerSecond,
                MaxVelocity);
        logger.DebugLog($"Clamped Velocity: {velocityPerSecond}");


        // ----------------------------
        // ROTATION
        // ----------------------------
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
        

        // ----------------------------
        // MOVEMENT
        // ----------------------------
        Vector3 movement =
            LocomotionMath.CalculateMovement(
                transform.rotation,
                velocityPerSecond,
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
                    velocityPerSecond,
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

        ////////////////////////////////////////////////////////////////////////////////
        // These are for the game mechanism.
        ResetPosition();
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