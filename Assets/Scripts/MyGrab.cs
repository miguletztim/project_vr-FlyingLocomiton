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
    [SerializeField] private float particleSpeedMultiplier = 1f;
    [SerializeField] private float minParticleSpeed = 0.1f;

    void Update()
    {
        if (IsValidObjectT(windTarget))
        {
            Rigidbody rb = windTarget.transform.parent != null
            ? windTarget.transform.parent.GetComponent<Rigidbody>()
            : windTarget.GetComponent<Rigidbody>();

            rb.AddForce(-rb.linearVelocity * dampingFactor * Time.deltaTime, ForceMode.Impulse); // 0.5f anpassen
        }


        // Gemessene Handgeschwindigkeit des rechten Controllers (Tracking-Space).
        Vector3 controllerVelocityLocal = OVRInput.GetLocalControllerVelocity(controller);

        // Blickrichtung der HMD-Kamera als Referenz für vorwärts/rückwärts.
        if(Camera.main == null) {
            Logger.Logger.ErrorLog("[MyGrab] Camera was not found!");
            StopWindParticles();
            return;
        }

        Vector3 controllerVelocityWorld = TransformControllerVelocityToWorld(controllerVelocityLocal);

        // Mindeststärke gegen Rauschen/Kleinstbewegungen.
        float flapStrength = controllerVelocityWorld.magnitude;
        if(flapStrength <= 1f) {
            StopWindParticles();
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
        bool isMovingBackward = Vector3.Dot(controllerVelocityWorld, viewForward) < -0.1f;
        if(isMovingBackward) {
            Logger.Logger.DebugLog($"[MyGrab] IsMovingBackwards: {isMovingBackward}");
            StopWindParticles();
            return;
        }

        // Windstoßrichtung folgt der aktuellen Schlagrichtung.
        Vector3 windDirection = controllerVelocityWorld.normalized;
        Logger.Logger.DebugLog($"[MyGrab] WindDirection: {windDirection}");
        UpdateWindParticles(windDirection, flapStrength);
        
        // Ziel entlang des Windvektors ermitteln und den Impuls anwenden.
        GameObject target = ResolveWindTarget(windDirection);
        Logger.Logger.DebugLog($"[MyGrab] Target {target}");   

        if (target != null)
        {
            ApplyWindGust(target, windDirection, flapStrength);
        }
    }

    void UpdateWindParticles(Vector3 direction, float strength)
    {
        if (windParticles == null)
        {
            return;
        }

        if (direction.sqrMagnitude < 0.001f)
        {
            StopWindParticles();
            return;
        }

        Transform particlesTransform = windParticles.transform;
        particlesTransform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        float speed = Mathf.Max(minParticleSpeed, strength * particleSpeedMultiplier);
        ParticleSystem.MainModule main = windParticles.main;
        main.startSpeed = speed;

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
            windParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
        Rigidbody rb = target.transform.parent != null
            ? target.transform.parent.GetComponent<Rigidbody>()
            : target.GetComponent<Rigidbody>();
        
        if (rb == null) {
            Logger.Logger.ErrorLog("No Rigitbody found!");
            return;
        }

        ApplyAerodynamicAlignment(rb, windDirection, strength);
    }

    void ApplyAerodynamicAlignment(Rigidbody rb, Vector3 windDirection, float strength)
    {
        if (windDirection.sqrMagnitude < 0.01f) return;
        windDirection.Normalize();

        Vector3 objForward = rb.transform.right;
        if (objForward.sqrMagnitude < 0.01f) return;
        objForward.Normalize();

        Vector3 cross = Vector3.Cross(objForward, windDirection);

        // P term: restoring force toward wind alignment
        Vector3 restoring = cross * aerodynamicAlignmentTorque * strength;

        // D term: damp angular velocity — critical for stopping cleanly
        // Increase dampingFactor until oscillation stops (typically 0.1–0.5)
        Vector3 damping = -rb.angularVelocity * aerodynamicAlignmentTorque 
                        * dampingFactor * strength;

        rb.AddTorque(restoring + damping, ForceMode.Force);
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
            // Bei Verlassen nicht hart zurücksetzen, damit die Auto-Auswahl stabil bleibt.
            windTarget = null;
        }
    }
}