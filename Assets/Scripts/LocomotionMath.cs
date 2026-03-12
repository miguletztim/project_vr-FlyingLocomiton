using System;
using UnityEngine;

/// <summary>
/// Contains pure mathematical locomotion logic for VR flapping/gliding movement.
/// <para>
/// This class has NO dependency on Unity Time, Physics (except raycasts and sphere casts),
/// Transforms, or Input. All values are passed explicitly, making the logic fully testable
/// and decoupled from MonoBehaviour lifecycle methods.
/// </para>
/// </summary>
public static class LocomotionMath
{
    // ─── Constants ────────────────────────────────────────────────────────────

    /// <summary>Terminal falling velocity in m/s (≈ real-world ~274 km/h).</summary>
    private const float TerminalFallVelocity = -76f;

    /// <summary>Drag coefficient applied while the player is airborne.</summary>
    private const float AirDragFactor = 0.1f;

    /// <summary>Drag coefficient applied while the player is on the ground.</summary>
    private const float GroundDragFactor = 2f;

    /// <summary>Minimum controller speed (m/s) required to register a flap input.</summary>
    private const float MinimumFlapStrength = 20f;

    /// <summary>
    /// Arm angle (degrees) above which the player is considered to be banking/rotating.
    /// Also used as the upper bound for the glide angle check.
    /// </summary>
    private const float MinAngleToRotateDegree = 10f;

    /// <summary>Maximum yaw rotation speed in degrees per second.</summary>
    private const float MaxDegreePerSecond = 90f;

    /// <summary>
    /// Fraction of <see cref="maxControllerDistance"/> the current distance
    /// must exceed for a glide to be valid.
    /// </summary>
    private const float GlideDistanceThresholdFactor = 0.9f;

    // ─── Gravity & Drag ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the gravitational acceleration vector to add to velocity this frame.
    /// </summary>
    /// <param name="gravity">
    /// Gravitational acceleration in m/s². Typically a negative value (e.g. <c>-9.81f</c>)
    /// so the result points downward.
    /// </param>
    /// <param name="deltaTime">Elapsed time since the last frame in seconds.</param>
    /// <returns>A <see cref="Vector3"/> with only a Y component representing the gravity delta.</returns>
    public static Vector3 Gravity(float gravity, float deltaTime)
    {
        return new Vector3(0f, gravity * deltaTime, 0f);
    }

    /// <summary>
    /// Clamps horizontal speed to <paramref name="maxVelocity"/> and vertical speed
    /// to <see cref="TerminalFallVelocity"/>, leaving upward velocity uncapped.
    /// </summary>
    /// <param name="velocity">Current velocity vector in m/s.</param>
    /// <param name="maxVelocity">Maximum allowed horizontal speed in m/s.</param>
    /// <returns>The velocity vector with both horizontal and vertical components clamped.</returns>
    public static Vector3 ClampSpeed(Vector3 velocity, float maxVelocity)
    {
        float clampedVertical = Mathf.Clamp(Mathf.Max(velocity.y, TerminalFallVelocity), TerminalFallVelocity, maxVelocity);;

        Vector3 horizontal = new(velocity.x, 0f, velocity.z);
        Vector3 clampedHorizontal = horizontal.normalized * Mathf.Min(horizontal.magnitude, maxVelocity);

        return new Vector3(clampedHorizontal.x, clampedVertical, clampedHorizontal.z);
    }

    /// <summary>
    /// Reduces the velocity magnitude using a simple linear drag model.
    /// Horizontal and vertical components are scaled uniformly.
    /// </summary>
    /// <param name="velocity">Current velocity vector in m/s.</param>
    /// <param name="onGround">
    /// <c>true</c> to apply the higher ground drag; <c>false</c> for lower air drag.
    /// </param>
    /// <param name="deltaTime">Elapsed time since the last frame in seconds.</param>
    /// <returns>
    /// The velocity vector after drag has been applied. Never inverts direction;
    /// magnitude is floored at zero.
    /// </returns>
    public static Vector3 VelocityAfterDrag(Vector3 velocity, bool onGround, float deltaTime)
    {
        float drag = onGround ? GroundDragFactor : AirDragFactor;
        float newMagnitude = velocity.magnitude * (1f - drag * deltaTime);

        return velocity.normalized * Mathf.Max(0f, newMagnitude);
    }

    // ─── Flapping & Gliding ───────────────────────────────────────────────────

    /// <summary>
    /// Converts raw controller velocity into a flap impulse, attenuated by altitude.
    /// </summary>
    /// <param name="controllerVelocity">
    /// Combined velocity of both VR controllers in world space (m/s).
    /// </param>
    /// <param name="height">Current height of the player above the ground in metres.</param>
    /// <param name="maxHeight">
    /// The height ceiling above which flapping produces no vertical lift.
    /// </param>
    /// <param name="isFlapping">
    /// <c>true</c> when <paramref name="controllerVelocity"/> exceeds
    /// <see cref="MinimumFlapStrength"/> and a flap impulse is applied.
    /// </param>
    /// <returns>
    /// The scaled flap impulse vector. Returns <see cref="Vector3.zero"/> when
    /// <paramref name="isFlapping"/> is <c>false</c>.
    /// </returns>
    public static (Vector3 flapStrength, bool isFlapping) CalculateFlapStrength(
        Vector3 controllerVelocity,
        float height)
    {
        const float MaxHeight = 30f;
        bool isFlapping = IsFlapping(controllerVelocity);

        // Reduce vertical lift linearly as the player approaches maxHeight.
        float heightFactor = Mathf.Clamp01(1f - height / MaxHeight);
        controllerVelocity.y *= heightFactor;

        // Damp the speed
        controllerVelocity *= 0.5f;

        return (controllerVelocity, isFlapping);
    }

    /// <summary>
    /// Evaluates which flap condition was met. Useful for diagnostics and tests.
    /// </summary>
    public static bool IsFlapping(Vector3 controllerVelocity)
    {
        bool byVertical = controllerVelocity.y > 3f;
        bool byHorizontal = new Vector2(controllerVelocity.x, controllerVelocity.z).magnitude > MinimumFlapStrength;

        return byVertical || byHorizontal;
    }

    /// <summary>
    /// Smoothly clamps the vertical fall speed towards <paramref name="glideLimit"/>
    /// while the player is gliding, preventing an abrupt speed cap.
    /// </summary>
    /// <param name="verticalVelocity">Current vertical velocity component in m/s.</param>
    /// <param name="glideLimit">
    /// The maximum (least-negative) fall speed allowed during a glide, in m/s.
    /// Must be ≤ 0 to be meaningful (e.g. <c>-2f</c>).
    /// </param>
    /// <param name="deltaTime">Elapsed time since the last frame in seconds.</param>
    /// <returns>
    /// The adjusted vertical velocity, never slower than <paramref name="glideLimit"/>.
    /// </returns>
    public static float GlideFallSpeed(
        float verticalVelocity,
        float deltaTime)
    {
        const float GlideLimit = -4f;

        // Apply a strong drag to rapidly approach the glide limit, but never exceed it.
        if(verticalVelocity > 0f)
        {
            verticalVelocity -= deltaTime * verticalVelocity;
        }

        return Mathf.Max(verticalVelocity, GlideLimit);
    }

    /// <summary>
    /// Returns the angle (in degrees) between the line connecting both controllers
    /// and the horizontal plane. Positive values indicate the left controller is
    /// higher than the right.
    /// </summary>
    /// <param name="leftPos">World-space position of the left VR controller.</param>
    /// <param name="rightPos">World-space position of the right VR controller.</param>
    /// <returns>Arm tilt angle in degrees, in the range [−90, 90].</returns>
    public static float CalculateArmAngleDegree(Vector3 leftPos, Vector3 rightPos)
    {
        Vector3 diff = rightPos - leftPos;
        float horizontalMagnitude = Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z);
        return Mathf.Atan2(diff.y, horizontalMagnitude) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Derives the player's intended forward direction from the orientation of their arms.
    /// The result is projected onto the horizontal plane so vertical tilting does
    /// not affect the heading.
    /// </summary>
    /// <param name="leftPos">World-space position of the left VR controller.</param>
    /// <param name="rightPos">World-space position of the right VR controller.</param>
    /// <returns>Normalised horizontal forward vector.</returns>
    public static Vector3 CalculateForwardDirection(Vector3 leftPos, Vector3 rightPos)
    {
        Vector3 rightVector = (rightPos - leftPos).normalized;
        rightVector.y = 0f; // Project onto the horizontal plane.

        return Vector3.Cross(rightVector, Vector3.up).normalized;
    }

    /// <summary>
    /// Returns the Euclidean distance between the two VR controllers in metres.
    /// </summary>
    /// <param name="leftPos">World-space position of the left VR controller.</param>
    /// <param name="rightPos">World-space position of the right VR controller.</param>
    /// <returns>Controller separation distance in metres.</returns>
    public static float CalculateArmDistance(Vector3 leftPos, Vector3 rightPos)
    {
        return Vector3.Distance(leftPos, rightPos);
    }

    /// <summary>
    /// Combines the frame-over-frame movement of both controllers into a single
    /// velocity vector, representing the force input from a flapping motion.
    /// The result is negated so that pushing the controllers downward produces
    /// an upward impulse.
    /// </summary>
    /// <param name="leftPos">Current world-space position of the left controller.</param>
    /// <param name="rightPos">Current world-space position of the right controller.</param>
    /// <param name="previousLeftPos">Left controller position in the previous frame.</param>
    /// <param name="previousRightPos">Right controller position in the previous frame.</param>
    /// <param name="deltaTime">Elapsed time since the last frame in seconds.</param>
    /// <returns>Combined controller velocity in m/s.</returns>
    public static Vector3 CalculateCombinedControllerVelocity(
        Vector3 leftPos,
        Vector3 rightPos,
        Vector3 previousLeftPos,
        Vector3 previousRightPos,
        float deltaTime)
    {
        Vector3 leftDelta  = leftPos  - previousLeftPos;
        Vector3 rightDelta = rightPos - previousRightPos;

        return -(leftDelta + rightDelta) / deltaTime;
    }

    // ─── Rotation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the arm tilt exceeds the minimum threshold required
    /// to trigger yaw rotation (<see cref="MinAngleToRotateDegree"/>).
    /// </summary>
    /// <param name="armAngleDegree">
    /// Arm tilt angle in degrees as returned by <see cref="CalculateArmAngleDegree"/>.
    /// </param>
    public static bool ShouldRotate(float armAngleDegree)
    {
        return Mathf.Abs(armAngleDegree) >= MinAngleToRotateDegree;
    }

    /// <summary>
    /// Maps the arm tilt angle linearly to a rotation percentage in [−1, 1],
    /// where ±1 corresponds to a perfectly vertical arm (90°).
    /// </summary>
    /// <param name="armAngleDegree">Arm tilt angle in degrees.</param>
    /// <returns>Normalised rotation percentage in the range [−1, 1].</returns>
    public static float CalculateRotationPercentage(float armAngleDegree)
    {
        const float VerticalAngle = 90f;
        return Mathf.Clamp(armAngleDegree / VerticalAngle, -1f, 1f);
    }

    /// <summary>
    /// Computes a roll quaternion around a forward axis based on arm tilt.
    /// Intended for cosmetic camera or avatar lean, not physics.
    /// </summary>
    /// <param name="armAngleDegree">Arm tilt angle in degrees.</param>
    /// <param name="forward">World-space forward axis (for example from <see cref="CalculateForwardDirection"/>).</param>
    /// <returns>A <see cref="Quaternion"/> representing the roll for this frame.</returns>
    public static Quaternion CalculateRoll(float armAngleDegree, Vector3 forward)
    {
        Vector3 axis = forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;
        return Quaternion.AngleAxis(armAngleDegree, axis);
    }

    // ─── Physics Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the horizontal speed as a fraction of <paramref name="maxVelocity"/>.
    /// Vertical velocity is ignored so aerial descents do not affect the result.
    /// </summary>
    /// <param name="velocity">Current velocity vector in m/s.</param>
    /// <param name="maxVelocity">Maximum expected horizontal speed in m/s. Must be &gt; 0.</param>
    /// <returns>Horizontal speed percentage clamped to [0, 1].</returns>
    public static float CalculatePercentageOfMaxSpeed(Vector3 velocity, float maxVelocity)
    {
        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        return Mathf.Clamp01(Mathf.Abs(horizontalSpeed / maxVelocity));
    }

    /// <summary>
    /// Performs a downward raycast to determine whether the player is standing on a surface.
    /// </summary>
    /// <param name="position">World-space origin of the raycast (typically the player's feet).</param>
    /// <param name="thresholdDistance">
    /// Maximum distance to the ground that still counts as grounded (default: 0.3 m).
    /// </param>
    /// <returns><c>true</c> when a surface is detected within <paramref name="thresholdDistance"/>.</returns>
    public static bool IsGrounded(Vector3 position, float thresholdDistance = 0.3f)
    {
        return Physics.Raycast(position, Vector3.down, thresholdDistance);
    }

    /// <summary>
    /// Adjusts velocity to slide along any surface hit this frame, preventing the
    /// player from tunnelling through geometry.
    /// </summary>
    /// <param name="velocityPerSecond">Current velocity in m/s.</param>
    /// <param name="position">World-space origin of the sphere cast.</param>
    /// <param name="deltaTime">Elapsed time since the last frame in seconds.</param>
    /// <returns>
    /// The velocity projected onto the hit surface normal, or the original
    /// velocity when no collision is detected.
    /// </returns>
    public static Vector3 VelocityAfterCollision(
        Vector3 velocityPerSecond,
        Vector3 position,
        float deltaTime)
    {
        Vector3 movement = velocityPerSecond * deltaTime;

        if (Physics.SphereCast(position, 0.2f, movement.normalized, out RaycastHit hit, movement.magnitude))
        {
            bool isObjectT = hit.collider.gameObject.CompareTag("objectT");
            bool isSelectionTaskStart = hit.collider.gameObject.CompareTag("selectionTaskStart");
            bool isObjectInteractionTask = hit.collider.gameObject.CompareTag("objectInteractionTask");
            bool isDone = hit.collider.gameObject.CompareTag("done");

            if(!(isObjectT || isSelectionTaskStart || isObjectInteractionTask || isDone))
            {
                velocityPerSecond = Vector3.ProjectOnPlane(velocityPerSecond, hit.normal);
            }
        }

        return velocityPerSecond;
    }    

    public static bool IsGliding(float currentDistance, float maxDistance, float armAngleDegree)
    {
        float thresholdDistance = maxDistance * GlideDistanceThresholdFactor;
        
        bool validDistance = currentDistance > thresholdDistance;
        bool validAngle = Mathf.Abs(armAngleDegree) < MinAngleToRotateDegree;

        return validDistance && validAngle;
    }

    /// <summary>
    /// Returns <c>true</c> when movement points opposite to <paramref name="forward"/>.
    /// Uses a strict backward check via dot product (<c>dot &lt; 0</c>).
    /// </summary>
    /// <param name="velocity">Current movement vector in world space.</param>
    /// <param name="forward">Reference forward direction in world space.</param>
    /// <returns>
    /// <c>true</c> when the movement is backward relative to <paramref name="forward"/>;
    /// otherwise <c>false</c>. Returns <c>false</c> for near-zero input vectors.
    /// </returns>
    public static bool IsMovingBackwards(Vector3 velocity, Vector3 forward)
    {
        const float MinVectorSqrMagnitude = 0.0001f;

        // Without a stable direction reference we do not classify movement as backward.
        if (velocity.sqrMagnitude < MinVectorSqrMagnitude || forward.sqrMagnitude < MinVectorSqrMagnitude)
        {
            return false;
        }

        float dot = Vector3.Dot(velocity.normalized, forward.normalized);
        return dot < 0f;
    }

    public static (Quaternion addedYaw, Vector3 newMovementPerSecond) VelocityWithRotation(Vector3 newMovementPerSecond, Vector3 forward, float armAngleDegree, float deltaTime)
    {
        if (!ShouldRotate(armAngleDegree)) {
            return (Quaternion.identity, newMovementPerSecond);
        }

        
        bool isMovingBackwards = IsMovingBackwards(newMovementPerSecond, forward);
        float direction = isMovingBackwards ? 1f : -1f;

        float rotationPercentage = direction * CalculateRotationPercentage(armAngleDegree);
        float rotationSpeedPerFrame = rotationPercentage * MaxDegreePerSecond * deltaTime;
        Quaternion addedYaw = Quaternion.AngleAxis(rotationSpeedPerFrame, Vector3.up);

        Vector3 moveDir = newMovementPerSecond.normalized;
        float rightDot = Vector3.Dot(moveDir, Vector3.Cross(Vector3.up, forward).normalized);

        // 1. Erst bremsen
        float brakeAmount = Mathf.Clamp01(rotationPercentage * rightDot);
        float brakeFactor = 1f - brakeAmount;

        Vector3 forwardComponent = forward.normalized * Vector3.Dot(newMovementPerSecond, forward.normalized);
        Vector3 lateralComponent = newMovementPerSecond - forwardComponent;

        Vector3 brakingLateral = lateralComponent * brakeFactor;
        Vector3 brakingMovement = forwardComponent + brakingLateral;

        // 2. Dann rotieren
        return (addedYaw, addedYaw * brakingMovement);
    }

    
}