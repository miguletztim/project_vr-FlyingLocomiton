using UnityEngine;

public class MyGrab : MonoBehaviour
{
    public OVRInput.Controller controller;
    public GameObject windTarget;
    public SelectionTaskMeasure selectionTaskMeasure;

    [Header("Wind Settings")]
    /// <summary>
    /// Rechter Controller dreht gegen den Uhrzeigersinn (negatives Y-Drehmoment),
    /// linker Controller dreht im Uhrzeigersinn.
    /// </summary>
    private bool isRightController
    {
        get
        {
            return controller.Equals(OVRInput.GetActiveControllerForHand(OVRInput.Handedness.RightHanded));
        }
    }
    public float windRotationTorque = 5f;
    public float aerodynamicAlignmentTorque = 3f;
    public float autoTargetMaxDistance = 10f;
    public float windCastRadius = 0.35f;

    void Update()
    {
        Vector3 controllerVelocity = OVRInput.GetLocalControllerVelocity(controller);
        float flapStrength = controllerVelocity.magnitude;

        Logger.Logger.DebugLog($"[MyGrab] Flap Strength: {flapStrength}");

        if(flapStrength <= 5f) {
            return;
        }

        Vector3 windDirection = controllerVelocity.normalized;
        Logger.Logger.DebugLog($"[MyGrab] WindDirection: {windDirection}");
        
        GameObject target = ResolveWindTarget(windDirection);
        Logger.Logger.DebugLog($"[MyGrab] Target {target}");

        if (target != null)
        {
            ApplyWindGust(0.5f, target, windDirection);
        }
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

        if (best == null)
        {
            best = FindObjectTNearby(origin, direction);
        }

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

    void ApplyWindGust(float strength, GameObject target, Vector3 windDirection)
    {
        Rigidbody rb = target.transform.parent != null
            ? target.transform.parent.GetComponent<Rigidbody>()
            : target.GetComponent<Rigidbody>();
        
        if (rb == null) {
            Logger.Logger.ErrorLog("No Rigitbody found!");
            return;
        }

        // Drehrichtung: rechts = gegen den Uhrzeigersinn (−Y), links = im Uhrzeigersinn (+Y)
        float rotSign = isRightController ? -1f : 1f;
        rb.AddTorque(rb.transform.up * rotSign * windRotationTorque * strength, ForceMode.Impulse);

        // Objekt leicht in Windrichtung verschieben
        rb.AddForce(windDirection * strength, ForceMode.Impulse);

        // Aerodynamische Ausrichtung: T-Form richtet sich windschnittig aus (Weather-Vane-Effekt)
        ApplyAerodynamicAlignment(rb, windDirection, strength);
    }

    void ApplyAerodynamicAlignment(Rigidbody rb, Vector3 windDirection, float strength)
    {
        // Nur horizontale Komponente berücksichtigen für saubere Ausrichtung
        Vector3 windHorizontal = new Vector3(windDirection.x, 0f, windDirection.z);
        if (windHorizontal.sqrMagnitude < 0.01f) return;
        windHorizontal.Normalize();

        // Längsachse des T (Stiel) zeigt in Windrichtung (windschnittig)
        Vector3 objForward = new Vector3(rb.transform.forward.x, 0f, rb.transform.forward.z);
        if (objForward.sqrMagnitude < 0.01f) return;
        objForward.Normalize();

        // Kreuzprodukt liefert die Rotationsachse; Fehlausrichtung (1 − dot) skaliert das Drehmoment
        Vector3 cross = Vector3.Cross(objForward, windHorizontal);
        float misalignment = 1f - Vector3.Dot(objForward, windHorizontal);

        if (cross.magnitude > 0.01f)
        {
            rb.AddTorque(cross.normalized * aerodynamicAlignmentTorque * misalignment * strength, ForceMode.Force);
        }
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