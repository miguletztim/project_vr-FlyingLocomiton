using System;
using UnityEngine;

public class MyGrab : MonoBehaviour
{
    public OVRInput.Controller controller;
    private GameObject windTarget;
    public SelectionTaskMeasure selectionTaskMeasure;

    public float dampingFactor = 0.8f;

    [Header("Wind Settings")]
    /// <summary>
    /// Rechter Controller dreht gegen den Uhrzeigersinn (negatives Y-Drehmoment),
    /// linker Controller dreht im Uhrzeigersinn.
    /// </summary>

    public float windRotationTorque = 5f;
    public float aerodynamicAlignmentTorque = 3f;
    public float autoTargetMaxDistance = 10f;
    public float windCastRadius = 0.35f;
    [SerializeField] private Transform trackingSpace;
    [SerializeField] private bool useYawOnlyDirection = true;
    [SerializeField] private ParticleSystem windParticles;
    [SerializeField] private float particleSpeedMultiplier = 20f;
    [SerializeField] private float minParticleSpeed = 0.1f;

    // Tracks whether a valid flap occurred this frame
    private bool flapActiveThisFrame = false;

    void Update()
    {
        // Reset flap tracking at the start of each frame
        flapActiveThisFrame = false;

        /*if (IsValidObjectT(windTarget))
        {
            Rigidbody rb = windTarget.transform.parent != null
            ? windTarget.transform.parent.GetComponent<Rigidbody>()
            : windTarget.GetComponent<Rigidbody>();

            rb.AddForce(-rb.linearVelocity * dampingFactor * Time.deltaTime, ForceMode.Impulse);
        }*/

        // Gemessene Handgeschwindigkeit des rechten Controllers (Tracking-Space).
        Vector3 controllerVelocityLocal = OVRInput.GetLocalControllerVelocity(controller);

        // Blickrichtung der HMD-Kamera als Referenz für vorwärts/rückwärts.
        if (Camera.main == null)
        {
            Logger.Logger.ErrorLog("[MyGrab] Camera was not found!");
            StopWindParticles();
            return;
        }

        Vector3 controllerVelocityWorld = TransformControllerVelocityToWorld(controllerVelocityLocal);

        // Mindeststärke gegen Rauschen/Kleinstbewegungen.
        float flapStrength = controllerVelocityWorld.magnitude;
        if (flapStrength <= 1f)
        {
            StopWindParticles(); // <-- Partikel stoppen wenn kein gültiger Flap
            return;
        }
        Logger.Logger.DebugLog($"[MyGrab] Flap Strength: {flapStrength}");

        // Berechnet die aktuelle Vorwärtsrichtung
        Vector3 viewForward = Camera.main.transform.forward;
        if (viewForward.sqrMagnitude > 0.001f)
        {
            viewForward.Normalize();
        }
        Logger.Logger.DebugLog($"[MyGrab] View Forward: {viewForward}");

        // Rückwärtsbewegungen werden ignoriert, damit nur Vorwärts-Flügelschläge zählen.
        bool isMovingBackward = Vector3.Dot(controllerVelocityWorld, viewForward) < 0f;
        if (isMovingBackward)
        {
            Logger.Logger.DebugLog($"[MyGrab] IsMovingBackwards: {isMovingBackward}");
            StopWindParticles(); // <-- Partikel stoppen bei Rückwärtsbewegung
            return;
        }

        // Windstoßrichtung folgt der aktuellen Schlagrichtung.
        Vector3 windDirection = controllerVelocityWorld.normalized;
        Logger.Logger.DebugLog($"[MyGrab] WindDirection: {windDirection}");

        /*
        // Flap ist valide – Partikel anzeigen
        flapActiveThisFrame = true;
        UpdateWindParticles(windDirection, flapStrength);
        */

        // Ziel entlang des Windvektors ermitteln und den Impuls anwenden.
        GameObject target = ResolveWindTarget(windDirection);
        if (target != null)
        {
            Logger.Logger.DebugLog($"[MyGrab] Target {target}");
            ApplyWindGust(target, windDirection, flapStrength);
        }
    }

    void UpdateWindParticles(Vector3 direction, float strength)
    {
        if (windParticles == null)
        {
            Logger.Logger.ErrorLog("[MyGrab] windParticles ist nicht im Inspector zugewiesen!");
            return;
        }

        // Partikel in Windrichtung ausrichten
        if (direction.sqrMagnitude > 0.001f)
        {
            windParticles.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        // Geschwindigkeit anpassen
        float speed = Mathf.Max(minParticleSpeed, strength * particleSpeedMultiplier);
        ParticleSystem.MainModule main = windParticles.main;
        main.startSpeed = speed;

        // Emissionsrate dynamisch nach Stärke skalieren (RateOverTime statt RateOverDistance!)
        ParticleSystem.EmissionModule emission = windParticles.emission;
        emission.rateOverTime = 1f * particleSpeedMultiplier;
        emission.rateOverDistance = 0f; // Sicherstellen dass Distance-Rate deaktiviert ist

        // Nur starten wenn nicht bereits aktiv – verhindert Reset-Flackern
        if (!windParticles.isPlaying)
        {
            windParticles.Play();
        }
    }

    void StopWindParticles()
    {
        if (windParticles == null)
        {
            return;
        }

        if (windParticles.isPlaying)
        {
            //windParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    Vector3 TransformControllerVelocityToWorld(Vector3 localVelocity)
    {
        Transform reference = trackingSpace != null ? trackingSpace : Camera.main.transform;

        if (useYawOnlyDirection)
        {
            Quaternion yawOnly = Quaternion.Euler(0f, reference.eulerAngles.y, 0f);
            return yawOnly * localVelocity;
        }

        return reference.TransformDirection(localVelocity);
    }

    GameObject ResolveWindTarget(Vector3 windDirection)
    {
        if (IsValidObjectT(windTarget))
        {
            return windTarget;
        }

        if (windDirection.sqrMagnitude < 0.001f)
        {
            return null;
        }

        Vector3 origin = transform.position;
        Vector3 direction = windDirection.normalized;
        GameObject best = FindObjectTBySphereCast(origin, direction);

        windTarget = best;
        return windTarget;
    }

    static bool IsValidObjectT(GameObject candidate)
    {
        return candidate != null && candidate.activeInHierarchy && candidate.CompareTag("objectT");
    }

    GameObject FindObjectTBySphereCast(Vector3 origin, Vector3 direction)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            windCastRadius,
            direction,
            autoTargetMaxDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);

        GameObject best = null;
        float bestDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
            {
                continue;
            }

            GameObject candidate = hit.collider.gameObject;
            if (!IsValidObjectT(candidate))
            {
                continue;
            }

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                best = candidate;
            }
        }

        return best;
    }

    GameObject FindObjectTNearby(Vector3 origin, Vector3 direction)
    {
        Collider[] nearby = Physics.OverlapSphere(
            origin + direction * 0.75f,
            windCastRadius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);

        foreach (Collider col in nearby)
        {
            if (col == null)
            {
                continue;
            }

            if (IsValidObjectT(col.gameObject))
            {
                return col.gameObject;
            }
        }

        return null;
    }

    void ApplyWindGust(GameObject target, Vector3 windDirection, float strength)
    {
        float flapStrength = strength * 0.01f;

        Rigidbody rb = target.transform.parent != null
            ? target.transform.parent.GetComponent<Rigidbody>()
            : target.GetComponent<Rigidbody>();

        if (rb == null)
        {
            Logger.Logger.ErrorLog("No Rigitbody found!");
            return;
        }

        ApplyAerodynamicAlignment(rb, windDirection, strength);
    }

    void ApplyAerodynamicAlignment(Rigidbody rb, Vector3 windDirection, float strength)
    {
        // Richtung vom Objekt zum Spieler (nur horizontal)
        Vector3 toPlayer = Camera.main.transform.position - rb.transform.position;
        toPlayer.y = 0f;
        toPlayer.Normalize();

        // Basis-Rotation: Z (Blau) zeigt zum Spieler
        Quaternion lookAtPlayer = Quaternion.LookRotation(toPlayer, Vector3.up);

        // Windrichtung in lokale Koordinaten des Spielers umrechnen
        Vector3 localWind = Quaternion.Inverse(lookAtPlayer) * windDirection;

        // Horizontaler Anteil (links/rechts) vs. vertikaler Anteil (oben/unten)
        float horizontalMagnitude = Mathf.Abs(localWind.x);
        float verticalMagnitude   = Mathf.Abs(localWind.y);

        Quaternion rotationOffset;

        if (horizontalMagnitude > verticalMagnitude)
        {
            // Windstoß von rechts: X +45°, von links: X -45°
            float xAngle = localWind.x > 0f ? 45f : -45f;
            rotationOffset = Quaternion.Euler(xAngle, 90f, -90f);
        }
        else
        {
            // Schlag von unten → X zu 0°, Schlag von oben → X zu -180°
            float targetXAngle = localWind.y > 0f ? 0f : -180f;
            rotationOffset = Quaternion.AngleAxis(targetXAngle, Vector3.right);
        }

        Quaternion targetRotation = lookAtPlayer * rotationOffset;

        // Prüfen ob aktuelle Rotation bereits nah an Zielrotation ist
        float angleDiff = Quaternion.Angle(rb.rotation, targetRotation);
        float alignmentThreshold = 10f;

        if (angleDiff < alignmentThreshold)
        {
            Vector3 pushDirection = -toPlayer;
            rb.AddForce(pushDirection * strength * 0.001f, ForceMode.Impulse);
        }

        rb.MoveRotation(targetRotation);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("objectT"))
        {
            windTarget = other.gameObject;
        }
        else if (other.gameObject.CompareTag("selectionTaskStart"))
        {
            if (!selectionTaskMeasure.isCountdown)
            {
                selectionTaskMeasure.isTaskStart = true;
                selectionTaskMeasure.StartOneTask();
            }
        }
        else if (other.gameObject.CompareTag("done"))
        {
            selectionTaskMeasure.isTaskStart = false;
            selectionTaskMeasure.EndOneTask();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("objectT") && windTarget == other.gameObject)
        {
            windTarget = null;
        }
    }
}