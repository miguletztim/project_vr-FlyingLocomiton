using System;
using UnityEngine;
using Logger;

public class LocomotionTechnique : MonoBehaviour
{
    // Please implement your locomotion technique in this script. 
    public OVRInput.Controller leftController;
    public OVRInput.Controller rightController;
    public GameObject hmd;

    public float gravity = -9.81f;  // Gravity
    public float collisionRadius = 0.2f;  // Collision radius for SphereCast
    private readonly float maxVelocity = 10f;

    private Vector3 prevLeftPos;
    private Vector3 prevRightPos;
    private Vector3 velocityPerSecond;

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
            Fly(leftControllerPosition, rightControllerPosition);
        }
        else
        {
            velocityPerSecond = Vector3.zero;
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

    private void Fly(Vector3 leftControllerPosition, Vector3 rightControllerPosition)
    {
        // Berechne Geschwindigkeit der Controller

        Vector3 flapStrength = CalculateControllerVelocity(leftControllerPosition, rightControllerPosition, out bool isFlapping);
        logger.DebugLog($"Flap Strength: {flapStrength}");

        // Apply Gravity
        ApplyGravity();

        float currentAngle = CalculateAngle(leftControllerPosition, rightControllerPosition);
        if (isFlapping)
        {
            Flap(flapStrength);
        }
        else if (Mathf.Abs(currentAngle) < 0.3f)
        {           
            Glide();
        }

        // Caps Speed
        velocityPerSecond.y = Mathf.Clamp(velocityPerSecond.y, Mathf.NegativeInfinity, -gravity/4f);
        velocityPerSecond.x = Mathf.Clamp(velocityPerSecond.x, -maxVelocity, maxVelocity);
        velocityPerSecond.z = Mathf.Clamp(velocityPerSecond.z, -maxVelocity, maxVelocity);

        logger.DebugLog($"Velocity: {velocityPerSecond}");

        Vector3 forwardDirection = CalculateForwardDirection(leftControllerPosition, rightControllerPosition);
        transform.rotation = Quaternion.Euler(0f, CalculateYaw(currentAngle, velocityPerSecond), 0f) * CalculateRoll(currentAngle, forwardDirection);
        UpdateMovement();
    }

    private void ApplyGravity()
    {
        velocityPerSecond.y += gravity * Time.deltaTime;
    }


    private void Flap(Vector3 flapStrength)
    {
        float maxHeight = 30f;
        
        velocityPerSecond.y += flapStrength.y * Mathf.Clamp(1-transform.position.y/maxHeight, 0f, 1f);
        velocityPerSecond.x += flapStrength.x;
        velocityPerSecond.z += flapStrength.z;
    }


    private void Glide()
    {
        float glideFallSpeedPerSecond = -1f;
        float glideSmoothness = Time.deltaTime;

        if (velocityPerSecond.y > glideFallSpeedPerSecond)
        {
            return;
        }

        logger.DebugLog($"Vertical Velocity before glide: {velocityPerSecond.y}");
        
        velocityPerSecond.y -= glideSmoothness * velocityPerSecond.y;
        if(velocityPerSecond.y > glideFallSpeedPerSecond)
        {
            velocityPerSecond.y = glideFallSpeedPerSecond;
        }
        
        logger.DebugLog($"Vertical Velocity after glide: {velocityPerSecond.y}");
    }

    private Vector3 CalculateControllerVelocity(Vector3 leftControllerPosition, Vector3 rightControllerPosition, out bool isFlapping)
    {
        Vector3 leftControllerOffset = leftControllerPosition - prevLeftPos;
        Vector3 rightControllerOffset = rightControllerPosition - prevRightPos;

        // Berechne die kombinierte Geschwindigkeit beider Flügel, skaliert mit der Zeit
        Vector3 controllerVelocity = -(leftControllerOffset + rightControllerOffset) / Time.deltaTime;

        // Überprüfe, ob die Flügelbewegung die Schwellwerte überschreitet
        isFlapping = controllerVelocity.y > 3f || Mathf.Abs(controllerVelocity.x) > 10 || -controllerVelocity.z > 10;

        return controllerVelocity;
    }

    private void UpdateMovement()
    {
        // Berechne die Bewegungsrichtung
        Vector3 direction = GetMovementDirection();

        // Kollisionslogik anwenden
        direction = HandleCollision(direction, out bool onGround);

        ApplyDrag(onGround);

        // Position aktualisieren
        transform.position += direction;
    }

    private void ApplyDrag(bool onGround)
{
    float defaultDrag = 0.1f * Time.deltaTime;
    float groundDrag = 2f * Time.deltaTime;
    float dragPercentage = defaultDrag;

    // SphereCast durchführen, um Kollision zu prüfen
    if (onGround)
    {
        dragPercentage = groundDrag;
    }

    logger.DebugLog($"Velocity before drag: {velocityPerSecond}");
    velocityPerSecond.x -= dragPercentage * velocityPerSecond.x;
    velocityPerSecond.z -= dragPercentage * velocityPerSecond.z;
    logger.DebugLog($"Velocity after drag: {velocityPerSecond}");
}

    private Vector3 GetMovementDirection()
    {
        Vector3 currentSpeed = Vector3.up * velocityPerSecond.y + transform.forward * velocityPerSecond.z + transform.right * velocityPerSecond.x;
        // Berechnet die Bewegung unter Berücksichtigung der vertikalen und horizontalen Geschwindigkeiten
        return currentSpeed * Time.deltaTime;
    }

    private Vector3 HandleCollision(Vector3 direction, out bool onGround)
    {
        onGround = false;
        float maxSlopeAngle = 45f; // Alles über 45° gilt als Wand, nicht als Boden

        logger.DebugLog("HandleCollision direction:" + direction);

        // SphereCast durchführen
        if (Physics.SphereCast(transform.position, collisionRadius, direction.normalized, out RaycastHit hit, direction.magnitude))
        {
            // 1. Logik: Ist die getroffene Fläche flach genug?
            // Berechne den Winkel zwischen dem "Up"-Vektor und der Normalen der getroffenen Fläche
            float angle = Vector3.Angle(Vector3.up, hit.normal);
            
            // Wenn der Winkel klein genug ist, ist es der Boden
            onGround = angle <= maxSlopeAngle;

            logger.DebugLog($"Hit Angle: {angle} | onGround: {onGround}");

            // 2. Bewegung anpassen
            // Vertikale Geschwindigkeit bei jeder Kollision stoppen
            velocityPerSecond.y = 0f;
            direction = velocityPerSecond.z * Time.deltaTime * transform.forward + velocityPerSecond.x * Time.deltaTime * transform.right;

            // Wenn der Treffer kein Boden ist (also eine steile Wand oder Decke), Abprall-Logik
            if (!onGround)
            {
                direction = -transform.forward;
                velocityPerSecond.y = 0f;
            }
        }
        
        return direction;
    }

    private float CalculateAngle(Vector3 leftPos, Vector3 rightPos)
    {
        // Differenz berechnen
        Vector3 direction = leftPos - rightPos;

        // Winkel berechnen (in Radiant)
        float angle = Mathf.Atan2(direction.y, Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z));
        logger.DebugLog($"Calculated Angle (radians): {angle}");

        return angle;
    }

    private float CalculateYaw(float radAngle, Vector3 velocity)
    {
        const float minAngleToRotate = 5f;
        float maxRotationSpeed = 110f * Time.deltaTime;
        float minRotationSpeed = 20f * Time.deltaTime;

        float angleInDegrees = radAngle * Mathf.Rad2Deg;

        float targetYaw = transform.eulerAngles.y;
        if (Math.Abs(angleInDegrees) > minAngleToRotate)
        {            
            float rotationSpeed = radAngle * maxRotationSpeed * (Mathf.Abs(velocity.z) + Mathf.Abs(velocity.x)) / maxVelocity;
            rotationSpeed = Mathf.Clamp(rotationSpeed, -maxRotationSpeed, maxRotationSpeed);
            
            if (Mathf.Abs(rotationSpeed) < minRotationSpeed)
            {
                rotationSpeed = Mathf.Sign(rotationSpeed) * minRotationSpeed;
            }

            targetYaw += rotationSpeed;
        }

        return targetYaw;
    }
    
    private Quaternion CalculateRoll(float radAngle, Vector3 forwardDirection) {
        float angleInDegrees = radAngle * Mathf.Rad2Deg;
        
        // Normalisiere die horizontale Komponente der forward direction
        Vector2 horizontalDir = new Vector2(forwardDirection.x, forwardDirection.z).normalized;
        
        // Berechne Pitch und Roll basierend auf der normalisierten Richtung
        float pitchTilt = horizontalDir.y * angleInDegrees;  // Z-Komponente -> Pitch
        float rollTilt = horizontalDir.x * angleInDegrees;   // X-Komponente -> Roll
        
        Quaternion tilt = Quaternion.Euler(pitchTilt, 0f, -rollTilt);

        logger.DebugLog($"Forward Direction: {forwardDirection}, Horizontal Dir: {horizontalDir}, Angle in Degrees: {angleInDegrees}, Pitch: {pitchTilt}, Roll: {rollTilt}, Calculated Roll: {tilt.eulerAngles}");

        return tilt;
    }


    private static Vector3 CalculateForwardDirection(Vector3 leftPos, Vector3 rightPos)
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