using System;
using UnityEngine;

public class MyGrab : MonoBehaviour
{
    public OVRInput.Controller controller;
    private GameObject windTarget;
    public SelectionTaskMeasure selectionTaskMeasure;
    private Transform trackingReference;

    public float dampingFactor = 0.8f;
    public float directionDotDeadzone = 0.05f;

    [Header("Wind Settings")]
    /// <summary>
    /// Rechter Controller dreht gegen den Uhrzeigersinn (negatives Y-Drehmoment),
    /// linker Controller dreht im Uhrzeigersinn.
    /// </summary>
    public float autoTargetMaxDistance = 10f;
    public float windCastRadius = 5f;

    void Update()
    {
        // Step 1: Read controller velocity in local tracking space (relative to headset rig origin).
        Vector3 controllerVelocityLocal = OVRInput.GetLocalControllerVelocity(controller);

        Logger.Logger.DebugLog("[MyGrab] is called");

        // Step 2: Ensure we have a valid HMD camera before using view-dependent logic.
        if (Camera.main == null)
        {
            Logger.Logger.ErrorLog("[MyGrab] Camera was not found!");
            return;
        }

        // Step 3: Convert controller velocity into world space using current HMD orientation.
        Vector3 controllerVelocityWorld = TransformControllerVelocityToWorld(controllerVelocityLocal);

        // Step 4: Ignore very small movements to reduce noise and accidental triggers.
        float flapStrength = controllerVelocityWorld.magnitude;
        if (flapStrength < 1f)
        {
            return;
        }
        Logger.Logger.DebugLog($"[MyGrab] Flap Strength: {flapStrength}");

        // Step 5: Reject strokes that violate left/right cross-body policy.
        bool isInvalidStroke = IsInvalidStroke(controllerVelocityWorld);
        if (isInvalidStroke)
        {
            Logger.Logger.WarningLog("[MyGrab] WRONG DIRECTION");
            return;
        }

        // Step 6: Reject strokes moving opposite to where the player is looking.
        bool isBackwards = IsBackwards(controllerVelocityWorld);
        if (isBackwards)
        {
            Logger.Logger.WarningLog("[MyGrab] BACKWARDS");
            return;
        }

        // Step 7: Use the normalized stroke direction as wind direction.
        Vector3 windDirection = controllerVelocityWorld.normalized;
        Logger.Logger.DebugLog($"[MyGrab] WindDirection: {windDirection}");

        // Step 8: Find a valid target in the wind direction and apply the gust effect.
        GameObject target = ResolveWindTarget(windDirection);
        if (target != null)
        {
            Logger.Logger.DebugLog($"[MyGrab] Target {target}");
            ApplyWindGust(target, windDirection, flapStrength);
        }
        else {
            Logger.Logger.WarningLog("NO TARGET FOUND!");
        }
    }

    private bool IsBackwards(Vector3 controllerVelocityWorld)
    {
        // Use horizontal view direction to avoid false negatives when looking up/down.
        Vector3 viewForward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(controllerVelocityWorld, Vector3.up);
        Logger.Logger.DebugLog($"[MyGrab] View Forward: {viewForward}");

        // Dot below deadzone means velocity points opposite to the viewing direction.
        bool isMovingBackward = Vector3.Dot(planarVelocity, viewForward) < -directionDotDeadzone;
        
        Logger.Logger.DebugLog($"[MyGrab] IsMovingBackwards: {isMovingBackward}");
        return isMovingBackward;
    }

    private bool IsInvalidStroke(Vector3 controllerVelocityWorld)
    {
        // Use horizontal right direction to classify lateral strokes robustly.
        Vector3 viewRight = Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(controllerVelocityWorld, Vector3.up);

        bool isLeftController = IsLeftController(controller);
        bool isRightController = IsRightController(controller);

        // Cross-body policy: left must move right, right must move left.
        bool isInvalidStroke = false;
        if (isLeftController || isRightController)
        {
            // Dot with viewRight tells us if the stroke has rightward (+) or leftward (-) component.
            float lateralDot = Vector3.Dot(planarVelocity, viewRight);

            // Positive dot = moving rightward, negative = moving leftward
            bool isMovingRight = lateralDot > directionDotDeadzone;
            bool isMovingLeft = lateralDot < -directionDotDeadzone;

            // Left hand should stroke rightward; right hand should stroke leftward.
            isInvalidStroke = (isLeftController && !isMovingRight)
                            || (isRightController && !isMovingLeft);
        }

        return isInvalidStroke;
    }

    Vector3 TransformControllerVelocityToWorld(Vector3 localVelocity)
    {
        EnsureTrackingReference();
        return trackingReference.TransformDirection(localVelocity);
    }

    void EnsureTrackingReference()
    {
        if (trackingReference != null)
        {
            return;
        }

        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.trackingSpace != null)
        {
            trackingReference = cameraRig.trackingSpace;
            return;
        }

        // Fallback: use camera transform if no OVRCameraRig is present.
        trackingReference = Camera.main.transform;
    }

    GameObject ResolveWindTarget(Vector3 windDirection)
    {
        // Reuse current target while it is still valid to avoid unnecessary physics queries.
        if (IsValidObjectT(windTarget))
        {
            return windTarget;
        }

        // Avoid casting with near-zero direction vectors.
        if (windDirection.sqrMagnitude < 0.001f)
        {
            return null;
        }

        Vector3 origin = transform.position;
        Vector3 direction = windDirection.normalized;

        // First prefer forward sphere cast results; if none found, try nearby overlap fallback.
        windTarget = FindObjectTBySphereCast(origin, direction) ?? FindObjectTNearby(origin, direction);
        
        return windTarget;
    }

    static bool IsValidObjectT(GameObject candidate)
    {
        return candidate != null && candidate.activeInHierarchy && candidate.CompareTag("objectT");
    }

    static bool IsLeftController(OVRInput.Controller c)
    {
        return c == OVRInput.Controller.LTouch || c == OVRInput.Controller.LHand;
    }

    static bool IsRightController(OVRInput.Controller c)
    {
        return c == OVRInput.Controller.RTouch || c == OVRInput.Controller.RHand;
    }

    GameObject FindObjectTBySphereCast(Vector3 origin, Vector3 direction)
    {
        // Probe forward volume to find candidate interactable targets.
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
                // Keep closest valid hit so the most immediate target is selected.
                bestDistance = hit.distance;
                best = candidate;
            }
        }

        return best;
    }

    GameObject FindObjectTNearby(Vector3 origin, Vector3 direction)
    {
        // Fallback: search in a sphere slightly ahead of the hand when cast misses.
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
        // Prefer parent rigidbody (if object is child mesh), otherwise use target rigidbody.
        Rigidbody rb = target.transform.parent != null
            ? target.transform.parent.GetComponent<Rigidbody>()
            : target.GetComponent<Rigidbody>();

        if (rb == null)
        {
            Logger.Logger.ErrorLog("No Rigitbody found!");
            return;
        }

        // Compute horizontal direction from target to player for player-facing reference frame.
        Vector3 toPlayer = Camera.main.transform.position - rb.transform.position;
        toPlayer.y = 0f;
        toPlayer.Normalize();

        // Basis-Rotation: Z (Blau) zeigt zum Spieler
        Quaternion lookAtPlayer = Quaternion.LookRotation(toPlayer, Vector3.up);

        // Convert world wind direction into local frame where forward points toward the player.
        Vector3 localWind = Quaternion.Inverse(lookAtPlayer) * windDirection;

        // Build target orientation opposite local wind and rotate toward it based on strength.
        Quaternion targetRotation = Quaternion.LookRotation(localWind.normalized, Vector3.up);
        rb.rotation = Quaternion.RotateTowards(rb.rotation, targetRotation, 350f);
    }

    void OnTriggerEnter(Collider other)
    {
        // Cache objectT while inside trigger so it can be reused as active target.
        if (other.gameObject.CompareTag("objectT"))
        {
            windTarget = other.gameObject;
        }
        else if (other.gameObject.CompareTag("selectionTaskStart"))
        {
            // Start selection task only when countdown gate allows it.
            if (!selectionTaskMeasure.isCountdown)
            {
                selectionTaskMeasure.isTaskStart = true;
                selectionTaskMeasure.StartOneTask();
            }
        }
        else if (other.gameObject.CompareTag("done"))
        {
            // End active selection task when hitting done marker.
            selectionTaskMeasure.isTaskStart = false;
            selectionTaskMeasure.EndOneTask();
        }
    }

    void OnTriggerExit(Collider other)
    {
        // Clear cached target once we leave its trigger volume.
        if (other.gameObject.CompareTag("objectT") && windTarget == other.gameObject)
        {
            windTarget = null;
        }
    }
}