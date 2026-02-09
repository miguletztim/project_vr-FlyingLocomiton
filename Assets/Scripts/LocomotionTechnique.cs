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
    private float verticalVelocityPerSecond;
    float forwardVelocityPerSecond;

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
        Vector3 leftPos = OVRInput.GetLocalControllerPosition(leftController);
        Vector3 rightPos = OVRInput.GetLocalControllerPosition(rightController);

        logger.DebugLog($"Left Position: {leftPos}, Right Position: {rightPos}");

        SwitchMoving();

        if (currentMovingMethod == MovingMethod.Fly)
        {
            Fly(leftPos, rightPos);
        }
        else
        {
            Walk();
        }

        // Speichere die Positionen für die nächste Berechnung
        prevLeftPos = leftPos;
        prevRightPos = rightPos;

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

    private void Fly(Vector3 leftPos, Vector3 rightPos)
    {
        // Berechne Geschwindigkeit der Controller
        Vector3 leftOffset = leftPos - prevLeftPos;
        Vector3 rightOffset = rightPos - prevRightPos;


        bool isFlapping = TryCalculateFlapStrength(leftOffset, rightOffset, out Vector3 flapStrength);
        logger.DebugLog($"Flap Strength: {flapStrength}");

        // Apply Gravity
        ApplyGravity();

        float currentAngle = CalculateAngle(leftPos, rightPos);
        if (isFlapping)
        {
            Flap(flapStrength);
        }
        else if (Mathf.Abs(currentAngle) < 0.3f)
        {           
            Glide();
        }

        // Caps Speed
        verticalVelocityPerSecond = Mathf.Clamp(verticalVelocityPerSecond, Mathf.NegativeInfinity, -gravity/4f);
        forwardVelocityPerSecond = Mathf.Clamp(forwardVelocityPerSecond, -maxVelocity, maxVelocity);

        logger.DebugLog($"Vertical Velocity: {verticalVelocityPerSecond}");
        logger.DebugLog($"Forward Velocity: {forwardVelocityPerSecond}");

        transform.rotation = CalculateRotation(currentAngle, forwardVelocityPerSecond);
        UpdateMovement();
    }

    private void ApplyGravity()
    {
        verticalVelocityPerSecond += gravity * Time.deltaTime;
    }


    private void Flap(Vector3 flapStrength)
    {
        float maxHeight = 30f;
        
        verticalVelocityPerSecond += flapStrength.y * Mathf.Clamp(1-transform.position.y/maxHeight, 0f, 1f);

        forwardVelocityPerSecond += flapStrength.z;
    }


    private void Glide()
    {
        float glideFallSpeedPerSecond = -1f;
        float glideSmoothness = Time.deltaTime;

        if (verticalVelocityPerSecond > glideFallSpeedPerSecond)
        {
            return;
        }

        logger.DebugLog($"Vertical Velocity before glide: {verticalVelocityPerSecond}");
        
        verticalVelocityPerSecond -= glideSmoothness * verticalVelocityPerSecond;
        if(verticalVelocityPerSecond > glideFallSpeedPerSecond)
        {
            verticalVelocityPerSecond = glideFallSpeedPerSecond;
        }
        
        logger.DebugLog($"Vertical Velocity after glide: {verticalVelocityPerSecond}");
    }

    private static bool TryCalculateFlapStrength(Vector3 leftOffset, Vector3 rightOffset, out Vector3 flapStrength)
    {
        // Berechne die kombinierte Geschwindigkeit beider Flügel, skaliert mit der Zeit
        flapStrength = -(leftOffset + rightOffset) / Time.deltaTime;

        // Überprüfe, ob die Flügelbewegung die Schwellwerte überschreitet
        return flapStrength.y > 3f || -flapStrength.z > 10;
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

    logger.DebugLog($"Forward Velocity before drag: {forwardVelocityPerSecond}");
    forwardVelocityPerSecond -= dragPercentage * forwardVelocityPerSecond;
    logger.DebugLog($"Forward Velocity after drag: {forwardVelocityPerSecond}");
}

    private Vector3 GetMovementDirection()
    {
        Vector3 currentSpeed = Vector3.up * verticalVelocityPerSecond + transform.forward * forwardVelocityPerSecond;
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
            verticalVelocityPerSecond = 0f;
            direction = forwardVelocityPerSecond * Time.deltaTime * transform.forward;

            // Wenn der Treffer kein Boden ist (also eine steile Wand oder Decke), Abprall-Logik
            if (!onGround)
            {
                direction = -transform.forward;
                verticalVelocityPerSecond = 0f;
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

    private Quaternion CalculateRotation(float radAngle, float velocity)
    {
        const float minAngleToRotate = 3f;
        float maxRotationSpeed = 110f * Time.deltaTime;
        float minRotationSpeed = 20f * Time.deltaTime;

        float angleInDegrees = radAngle * Mathf.Rad2Deg;

        // 1. Wir nutzen NUR den aktuellen Transform-Yaw als Basis.
        // Das HMD ignorieren wir hier für die Berechnung der Körper-Rotation,
        // damit der Körper die volle Kontrolle behält.
        float currentYaw = transform.eulerAngles.y;

        if (Math.Abs(angleInDegrees) > minAngleToRotate)
        {            
            float rotationSpeed = radAngle * maxRotationSpeed * (Mathf.Abs(velocity) / maxVelocity);
            rotationSpeed = Mathf.Clamp(rotationSpeed, -maxRotationSpeed, maxRotationSpeed);
            
            if (Mathf.Abs(rotationSpeed) < minRotationSpeed)
            {
                rotationSpeed = Mathf.Sign(rotationSpeed) * minRotationSpeed;
            }

            // 2. Wir berechnen den neuen Yaw und wenden das Rollen (Z) an.
            // Wir addieren die Drehung auf den aktuellen Stand auf.
            float targetYaw = currentYaw + rotationSpeed;

            // Wir geben eine Rotation zurück, die den Körper dreht UND neigt.
            return Quaternion.Euler(0f, targetYaw, Mathf.Sign(velocity) * -angleInDegrees);
        }

        // Wenn keine Drehung stattfindet, halten wir den Körper gerade (Z = 0),
        // behalten aber die aktuelle Y-Ausrichtung bei.
        return Quaternion.Euler(0f, currentYaw, 0f);
    }
}