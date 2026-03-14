using UnityEngine;

public class VignetteController : MonoBehaviour
{
    [Header("Referenzen")]
    public OVRVignette vignette;
    public Transform playerRig; // Hier das OVRCameraRig reinziehen

    [Header("Schwellenwerte")]
    public float moveThreshold = 0.01f;   // Ab welcher Bewegung (Meter)
    public float rotateThreshold = 0.1f; // Ab welcher Drehung (Grad)

    [Header("Vignette Einstellungen")]
    public float fovMoving = 55f;   // Sichtfeld bei Bewegung (kleiner = mehr Schutz)
    public float fovDefault = 120f; // Sichtfeld im Stillstand (normal)
    public float lerpSpeed = 5f;    // Wie weich blendet es ein/aus?

    private Vector3 lastPosition;
    private Quaternion lastRotation;

    void Start()
    {
        if (playerRig != null)
        {
            lastPosition = playerRig.position;
            lastRotation = playerRig.rotation;
        }
    }

    void Update()
    {
        if (vignette == null || playerRig == null) return;

        // 1. Bewegung messen
        float moveDelta = Vector3.Distance(playerRig.position, lastPosition);
        
        // 2. Drehung messen
        float rotateDelta = Quaternion.Angle(playerRig.rotation, lastRotation);

        // Prüfen, ob eine der Schwellen überschritten wurde
        bool isMoving = (moveDelta > moveThreshold) || (rotateDelta > rotateThreshold);

        // Ziel-FOV bestimmen
        float targetFOV = isMoving ? fovMoving : fovDefault;

        // Sanft anwenden
        vignette.VignetteFieldOfView = Mathf.Lerp(vignette.VignetteFieldOfView, targetFOV, Time.deltaTime * lerpSpeed);

        // Werte für den nächsten Frame speichern
        lastPosition = playerRig.position;
        lastRotation = playerRig.rotation;
    }
}