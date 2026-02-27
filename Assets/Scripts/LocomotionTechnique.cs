using System;
using UnityEngine;
using Logger;

public class LocomotionTechnique : MonoBehaviour
{
    // Please implement your locomotion technique in this script. 
    public OVRInput.Controller leftController;
    public OVRInput.Controller rightController;
    public GameObject hmd;

    private readonly float maxVelocity = 10f;

    private Vector3 prevLeftPos;
    private Vector3 prevRightPos;
    private Vector3 currentVelocityPerSecond;

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
        logger = new(logLevel: LogLevel.Debug);

        prevLeftPos = OVRInput.GetLocalControllerPosition(leftController);
        prevRightPos = OVRInput.GetLocalControllerPosition(rightController);
    }

    void Update()
    {
        // Controller-Positionen abrufen
        Vector3 leftControllerPosition = OVRInput.GetLocalControllerPosition(leftController);
        Vector3 rightControllerPosition = OVRInput.GetLocalControllerPosition(rightController);

        logger.DebugLog($"Left Position: {leftControllerPosition}, Right Position: {rightControllerPosition}");

        SwitchMoving();

        if (currentMovingMethod == MovingMethod.Fly)
        {
            Fly(leftControllerPosition, rightControllerPosition, currentVelocityPerSecond);
        }
        else
        {
            currentVelocityPerSecond = Vector3.zero;
            transform.rotation = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
            Walk();
        }

        // Speichere die Positionen für die nächste Berechnung
        prevLeftPos = leftControllerPosition;
        prevRightPos = rightControllerPosition;

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


    void Walk()
    {
        Debug.Log("Walking");

        
    }

    private void SwitchMoving()
    {
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            logger.InfoLog("New Moving Method: flying");
            currentMovingMethod = MovingMethod.Fly;
        }

        if(OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)) {
            logger.InfoLog("New Moving Method: walking");
            currentMovingMethod = MovingMethod.Walk;            
        }
    }

    private void Fly(Vector3 leftControllerPosition, Vector3 rightControllerPosition, Vector3 velocityPerSecond)
    {
        // Berechne Geschwindigkeit der Controller
        Vector3 leftControllerOffset = leftControllerPosition - prevLeftPos;
        Vector3 rightControllerOffset = rightControllerPosition - prevRightPos;

        // Berechne die kombinierte Geschwindigkeit beider Flügel, skaliert mit der Zeit
        Vector3 controllerVelocity = -(leftControllerOffset + rightControllerOffset) / Time.deltaTime;

        Vector3 flapStrength = CalculateFlapStrength(controllerVelocity, transform.position.y, out bool isFlapping);
        logger.DebugLog($"Flap Strength: {flapStrength}");

        // Apply Gravity
        velocityPerSecond += CalculateGravity();

        float angleArms = CalculateAngle(leftControllerPosition, rightControllerPosition);
        logger.DebugLog($"Calculated Angle (radians): {angleArms}");

        if (isFlapping)
        {
            velocityPerSecond += flapStrength;
        }
        else if (Mathf.Abs(angleArms) < 0.3f)
        {
            logger.DebugLog($"Vertical Velocity before glide: {velocityPerSecond.y}");
            velocityPerSecond.y = CalculateFallingSpeedWhileGliding(velocityPerSecond.y);
            logger.DebugLog($"Vertical Velocity after glide: {velocityPerSecond.y}");
        }
        
        velocityPerSecond = CalculateClampedSpeed(velocityPerSecond, maxVelocity);

        logger.DebugLog($"Velocity: {velocityPerSecond}");

        transform.rotation = CalculateRotation(leftControllerPosition, rightControllerPosition, velocityPerSecond, angleArms);

        // Berechne die Bewegungsrichtung
        Vector3 movement = CalculateMovement(transform.rotation, velocityPerSecond);

        // Kollisionslogik anwenden
        if(IsColliding(transform.position, movement, out bool onGround)) {
            movement = AdjustMovementOnCollision(velocityPerSecond, onGround);
        }

        logger.DebugLog($"Velocity before drag: {movement}");
        movement = CalculateDrag(movement, onGround);
        logger.DebugLog($"Velocity after drag: {movement}");

        // Position aktualisieren
        transform.position += movement;
    }

    private Quaternion CalculateRotation(Vector3 leftControllerPosition, Vector3 rightControllerPosition, Vector3 velocityPerSecond, float rotationAngle)
    {
        Vector3 forwardDirection = CalculateForwardDirection(leftControllerPosition, rightControllerPosition);
        return Quaternion.Euler(0f, CalculateYaw(transform.eulerAngles.y, rotationAngle, velocityPerSecond, maxVelocity), 0f) * CalculateRoll(rotationAngle, forwardDirection);
    }

    internal static Vector3 CalculateClampedSpeed(Vector3 velocityPerSecond, float maxVelocity)
    {
        //velocityPerSecond.y = Mathf.Clamp(velocityPerSecond.y, Mathf.NegativeInfinity, -gravity / 4f);
        velocityPerSecond.x = Mathf.Clamp(velocityPerSecond.x, -maxVelocity, maxVelocity);
        velocityPerSecond.z = Mathf.Clamp(velocityPerSecond.z, -maxVelocity, maxVelocity);

        return velocityPerSecond;
    }

    internal static Vector3 CalculateGravity()
    {
        float gravity = -9.81f;  // Gravity
        return new(0, gravity * Time.deltaTime, 0);
    }

    internal static float CalculateFallingSpeedWhileGliding(float verticalVelocityPerSecond)
    {
        float glideFallSpeedPerSecond = -1f;
        float glideSmoothness = Time.deltaTime;

        if (verticalVelocityPerSecond > glideFallSpeedPerSecond)
        {
            return verticalVelocityPerSecond;
        }

        
        verticalVelocityPerSecond -= glideSmoothness * verticalVelocityPerSecond;
        if(verticalVelocityPerSecond > glideFallSpeedPerSecond)
        {
            verticalVelocityPerSecond = glideFallSpeedPerSecond;
        }

        return verticalVelocityPerSecond;
    }

    internal static Vector3 CalculateFlapStrength(Vector3 controllerVelocity, float positionY, out bool isFlapping)
    {
        float thresholdY = 3f;
        float thresholdX = 10f;
        float thresholdZ = 10f;
        
        // Überprüfe, ob die Flügelbewegung die Schwellwerte überschreitet
        isFlapping = controllerVelocity.y > thresholdY || Mathf.Abs(controllerVelocity.x) > thresholdX || -controllerVelocity.z > thresholdZ;

        float maxHeight = 30f;
        
        controllerVelocity.y *= Mathf.Clamp(1-(positionY/maxHeight), 0f, 1f);

        return controllerVelocity;
    }

    internal static Vector3 CalculateDrag(Vector3 movement, bool onGround)
    {
        float defaultDrag = 0.1f * Time.deltaTime;
        float groundDrag = 2f * Time.deltaTime;
        float dragPercentage = defaultDrag;

        // SphereCast durchführen, um Kollision zu prüfen
        if (onGround)
        {
            dragPercentage = groundDrag;
        }

        movement.x -= dragPercentage * movement.x;
        movement.z -= dragPercentage * movement.z;

        return movement;
    }

    internal static Vector3 CalculateMovement(Quaternion orientation, Vector3 velocityPerSecond)
    {
        Vector3 currentSpeed = orientation * velocityPerSecond;
        // Berechnet die Bewegung unter Berücksichtigung der vertikalen und horizontalen Geschwindigkeiten
        return currentSpeed * Time.deltaTime;
    }

    internal static bool IsColliding(Vector3 position, Vector3 direction, out bool onGround)
    {
        float collisionRadius = 0.2f;  // Collision radius for SphereCast
        onGround = false;

        bool isColliding = Physics.SphereCast(position, collisionRadius, direction.normalized, out RaycastHit hit, direction.magnitude);

        if(isColliding)
        {
            onGround = IsGrounded(hit.normal);
        }

        return isColliding;
    }

    internal static bool IsGrounded(Vector3 hitNormal)
    {
        float maxSlopeAngle = 45f; 
        float angle = Vector3.Angle(Vector3.up, hitNormal);
        
        return angle <= maxSlopeAngle;
    }

    internal static Vector3 AdjustMovementOnCollision(Vector3 direction, bool isGrounded)
    {
        // Vertikale Geschwindigkeit bei jeder Kollision stoppen
        direction.y = 0f;

        // Abprall-Logik, falls es eine Wand/Decke ist
        if (!isGrounded)
        {
            Vector3 horizontalDir = new(direction.x, 0f, direction.z);
            direction = -horizontalDir.normalized;
        }

        return direction;
    }

    internal static float CalculateAngle(Vector3 leftPos, Vector3 rightPos)
    {
        // Differenz berechnen
        Vector3 direction = leftPos - rightPos;

        // Winkel berechnen (in Radiant)
        float angle = Mathf.Atan2(direction.y, Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z));

        return angle;
    }

    internal static float CalculateYaw(float currentYaw, float radAngle, Vector3 velocity, float maxVelocity)
    {
        const float minAngleToRotate = 5f;
        float maxRotationSpeed = 110f * Time.deltaTime;
        float minRotationSpeed = 20f * Time.deltaTime;

        float angleInDegrees = radAngle * Mathf.Rad2Deg;

        if (Math.Abs(angleInDegrees) > minAngleToRotate)
        {            
            float rotationSpeed = radAngle * maxRotationSpeed * (Mathf.Abs(velocity.z) + Mathf.Abs(velocity.x)) / maxVelocity;
            rotationSpeed = Mathf.Clamp(rotationSpeed, -maxRotationSpeed, maxRotationSpeed);
            
            if (Mathf.Abs(rotationSpeed) < minRotationSpeed)
            {
                rotationSpeed = Mathf.Sign(rotationSpeed) * minRotationSpeed;
            }

            currentYaw += rotationSpeed;
        }

        return currentYaw;
    }
    
    internal static Quaternion CalculateRoll(float radAngle, Vector3 forwardDirection) {
        float angleInDegrees = radAngle * Mathf.Rad2Deg;
        
        // Normalisiere die horizontale Komponente der forward direction
        Vector2 horizontalDir = new Vector2(forwardDirection.x, forwardDirection.z).normalized;
        
        // Berechne Pitch und Roll basierend auf der normalisierten Richtung
        float pitchTilt = horizontalDir.y * angleInDegrees;  // Z-Komponente -> Pitch
        float rollTilt = horizontalDir.x * angleInDegrees;   // X-Komponente -> Roll
        
        Quaternion tilt = Quaternion.Euler(pitchTilt, 0f, -rollTilt);

        return tilt;
    }


    internal static Vector3 CalculateForwardDirection(Vector3 leftPos, Vector3 rightPos)
    {
        // 1. Vektor von links nach rechts (Right Vector)
        Vector3 rightVector = (rightPos - leftPos).normalized;

        rightVector.y = 0f; // Ignoriere vertikale Komponente für die Richtungsberechnung
        
        // 2. Up Vector (normalerweise Vector3.up, aber du kannst auch HMD.up verwenden)
        Vector3 upVector = Vector3.up;
        
        // 3. Forward Vector = Cross Product von Right x Up
        Vector3 forwardVector = Vector3.Cross(rightVector, upVector).normalized;
        
        return forwardVector;
    }
}