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
    public readonly struct FlapDiagnostic
    {
        public readonly bool byVertical;
        public readonly bool byHorizontalX;
        public readonly bool byForwardZ;
        public readonly bool isFlapping;

        public FlapDiagnostic(bool byVertical, bool byHorizontalX, bool byForwardZ)
        {
            this.byVertical = byVertical;
            this.byHorizontalX = byHorizontalX;
            this.byForwardZ = byForwardZ;
            this.isFlapping = byVertical || byHorizontalX || byForwardZ;
        }
    }

    public readonly struct GlideDiagnostic
    {
        public readonly float thresholdDistance;
        public readonly bool validDistance;
        public readonly bool validAngle;
        public readonly bool isGliding;

        public GlideDiagnostic(float thresholdDistance, bool validDistance, bool validAngle)
        {
            this.thresholdDistance = thresholdDistance;
            this.validDistance = validDistance;
            this.validAngle = validAngle;
            this.isGliding = validDistance && validAngle;
        }
    }

    // ─── Constants ────────────────────────────────────────────────────────────

    /// <summary>Terminal falling velocity in m/s (≈ real-world ~274 km/h).</summary>
    private const float TerminalFallVelocity = -76f;

    /// <summary>Drag coefficient applied while the player is airborne.</summary>
    private const float AirDragFactor = 0.1f;

    /// <summary>Drag coefficient applied while the player is on the ground.</summary>
    private const float GroundDragFactor = 2f;

    /// <summary>Minimum controller speed (m/s) required to register a flap input.</summary>
    private const float MinimumFlapStrength = 10f;

    /// <summary>
    /// Arm angle (degrees) above which the player is considered to be banking/rotating.
    /// Also used as the upper bound for the glide angle check.
    /// </summary>
    private const float MinAngleToRotateDegree = 17f;

    /// <summary>Maximum yaw rotation speed in degrees per second.</summary>
    private const float MaxRotationSpeedPerSecond = 90f;

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
    public static Vector3 ApplyGravity(float gravity, float deltaTime)
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
        float clampedVertical = Mathf.Max(velocity.y, TerminalFallVelocity);

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
    public static Vector3 CalculateVelocityAfterDrag(Vector3 velocity, bool onGround, float deltaTime)
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
    public static Vector3 CalculateFlapStrength(
        Vector3 controllerVelocity,
        float height,
        float maxHeight,
        out bool isFlapping)
    {
        FlapDiagnostic flapDiagnostic = EvaluateFlapDiagnostic(controllerVelocity);
        isFlapping = flapDiagnostic.isFlapping;

        if (!isFlapping)
            return Vector3.zero;

        // Reduce vertical lift linearly as the player approaches maxHeight.
        float heightFactor = Mathf.Clamp01(1f - height / maxHeight) * 0.9f;
        controllerVelocity.y *= heightFactor;

        return controllerVelocity * 0.5f;
    }

    /// <summary>
    /// Evaluates which flap condition was met. Useful for diagnostics and tests.
    /// </summary>
    public static FlapDiagnostic EvaluateFlapDiagnostic(Vector3 controllerVelocity)
    {
        bool byVertical = controllerVelocity.y > 3f;
        bool byHorizontalX = Mathf.Abs(controllerVelocity.x) > 10f;
        bool byForwardZ = -controllerVelocity.z > 10f;
        return new FlapDiagnostic(byVertical, byHorizontalX, byForwardZ);
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
    public static float CalculateGlideFallSpeed(
        float verticalVelocity,
        float glideLimit,
        float deltaTime)
    {
        if (verticalVelocity >= glideLimit)
            return verticalVelocity;

        verticalVelocity -= deltaTime * verticalVelocity;
        return Mathf.Max(verticalVelocity, glideLimit);
    }

    /// <summary>
    /// Determines whether the player is currently gliding based on arm angle and
    /// how far the controllers are spread relative to the session maximum.
    /// </summary>
    /// <param name="armAngleDegree">
    /// Angle of the arm relative to the horizontal plane in degrees.
    /// Computed via <see cref="CalculateArmAngleDegree"/>.
    /// </param>
    /// <param name="currentControllerDistance">
    /// Current distance between the two VR controllers in metres.
    /// </param>
    /// <param name="maxControllerDistance">
    /// Running maximum controller distance observed this session (metres).
    /// Updated in-place when <paramref name="currentControllerDistance"/> exceeds it.
    /// </param>
    /// <returns>
    /// <c>true</c> when both the arm spread and arm angle satisfy the glide criteria.
    /// </returns>
    public static bool CalculateIfGliding(
        float armAngleDegree,
        float currentControllerDistance,
        ref float maxControllerDistance)
    {
        if (currentControllerDistance > maxControllerDistance)
            maxControllerDistance = currentControllerDistance;

        GlideDiagnostic diagnostic = EvaluateGlideDiagnostic(
            armAngleDegree,
            currentControllerDistance,
            maxControllerDistance);

        return diagnostic.isGliding;
    }

    /// <summary>
    /// Evaluates glide criteria against the current max controller distance.
    /// Useful for diagnostics and tests.
    /// </summary>
    public static GlideDiagnostic EvaluateGlideDiagnostic(
        float armAngleDegree,
        float currentControllerDistance,
        float maxControllerDistance)
    {
        float thresholdDistance = maxControllerDistance * GlideDistanceThresholdFactor;
        bool validDistance = currentControllerDistance > thresholdDistance;
        bool validAngle = Mathf.Abs(armAngleDegree) < MinAngleToRotateDegree;
        return new GlideDiagnostic(thresholdDistance, validDistance, validAngle);
    }

    // ─── Controller / Arm Geometry ────────────────────────────────────────────

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
        Vector3 diff = leftPos - rightPos;
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
    /// Computes the per-frame yaw rotation quaternion based on arm tilt.
    /// Returns <see cref="Quaternion.identity"/> when the tilt is below
    /// <see cref="MinAngleToRotateDegree"/>.
    /// </summary>
    /// <param name="armAngleDegree">Arm tilt angle in degrees.</param>
    /// <param name="percentageOfMaxSpeed">
    /// Current horizontal speed as a fraction of maximum speed [0, 1].
    /// Reserved for future speed-scaled rotation; currently unused.
    /// </param>
    /// <param name="deltaTime">Elapsed time since the last frame in seconds.</param>
    /// <returns>A <see cref="Quaternion"/> representing the yaw delta for this frame.</returns>
    public static Quaternion CalculateAddedYaw(
        float armAngleDegree,
        float percentageOfMaxSpeed,
        float deltaTime)
    {
        if (!ShouldRotate(armAngleDegree))
            return Quaternion.identity;

        float rotationPercentage   = CalculateRotationPercentage(armAngleDegree);
        float rotationSpeedPerFrame = -rotationPercentage * MaxRotationSpeedPerSecond * deltaTime;
        rotationSpeedPerFrame = Mathf.Clamp(rotationSpeedPerFrame,
            -MaxRotationSpeedPerSecond * deltaTime,
             MaxRotationSpeedPerSecond * deltaTime);

        return Quaternion.AngleAxis(rotationSpeedPerFrame, Vector3.up);
    }

    /// <summary>
    /// Computes the visual roll quaternion around the forward axis based on arm tilt.
    /// Intended for cosmetic camera or avatar lean, not physics.
    /// </summary>
    /// <param name="armAngleDegree">Arm tilt angle in degrees.</param>
    /// <param name="forward">The forward axis to roll around (typically the player's heading).</param>
    /// <returns>A <see cref="Quaternion"/> representing the roll for this frame.</returns>
    public static Quaternion CalculateRoll(float armAngleDegree, Vector3 forward)
    {
        float rotationPercentage = CalculateRotationPercentage(armAngleDegree);
        return Quaternion.AngleAxis(rotationPercentage * Mathf.Rad2Deg, forward);
    }

    /// <summary>
    /// Rotates the velocity vector by an arbitrary rotation quaternion.
    /// Used to steer velocity after computing a yaw or roll delta.
    /// </summary>
    /// <param name="velocity">Velocity vector to rotate (m/s).</param>
    /// <param name="rotation">The rotation to apply.</param>
    /// <returns>The rotated velocity vector.</returns>
    public static Vector3 CalculateVelocityAfterAddedRotation(Vector3 velocity, Quaternion rotation)
    {
        return rotation * velocity;
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
    public static Vector3 CalculateVelocityAfterCollision(
        Vector3 velocityPerSecond,
        Vector3 position,
        float deltaTime)
    {
        Vector3 movement = velocityPerSecond * deltaTime;

        if (Physics.SphereCast(position, 0.2f, movement.normalized, out RaycastHit hit, movement.magnitude))
        {
            velocityPerSecond = Vector3.ProjectOnPlane(velocityPerSecond, hit.normal);
        }

        return velocityPerSecond;
    }
}