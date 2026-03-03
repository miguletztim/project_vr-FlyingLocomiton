using System;
using UnityEngine;

/// <summary>
/// Contains pure mathematical locomotion logic.
/// This class has NO dependency on Unity Time, Physics, Transforms or Input.
/// All values are passed explicitly, making the code fully testable.
/// </summary>
public static class LocomotionMath
{
    // --------------------------------------------------
    // GRAVITY & MOVEMENT
    // --------------------------------------------------

    /// <summary>
    /// Calculates gravity force applied for the current frame.
    /// </summary>
    public static Vector3 ApplyGravity(float gravity, float deltaTime)
    {
        return new Vector3(0f, gravity * deltaTime, 0f);
    }

    /// <summary>
    /// Clamps all velocity components to the given maximum speed.
    /// </summary>
    public static Vector3 ClampSpeed(Vector3 velocity, float maxVelocity)
    {
        velocity.x = Mathf.Clamp(velocity.x, -maxVelocity, maxVelocity);
        velocity.y = Mathf.Clamp(velocity.y, Mathf.NegativeInfinity, maxVelocity);
        velocity.z = Mathf.Clamp(velocity.z, -maxVelocity, maxVelocity);
        return velocity;
    }

    /// <summary>
    /// Applies air or ground drag to horizontal movement.
    /// </summary>
    public static Vector3 ApplyDrag(Vector3 movement, bool onGround, float deltaTime)
    {
        float drag = onGround ? 2f * deltaTime : 0.1f * deltaTime;

        movement.x -= drag * movement.x;
        movement.z -= drag * movement.z;

        return movement;
    }

    /// <summary>
    /// Converts velocity per second into frame movement using rotation and deltaTime.
    /// </summary>
    public static Vector3 CalculateMovement(
        Quaternion rotation,
        Vector3 velocityPerSecond,
        float deltaTime)
    {
        return rotation * velocityPerSecond * deltaTime;
    }

    // --------------------------------------------------
    // FLIGHT MECHANICS
    // --------------------------------------------------

    /// <summary>
    /// Calculates flap strength based on controller velocity and current height.
    /// Also outputs whether the movement counts as a flap.
    /// </summary>
    public static Vector3 CalculateFlapStrength(
        Vector3 controllerVelocity,
        float height,
        float maxHeight,
        out bool isFlapping)
    {
        // Flap thresholds
        isFlapping =
            controllerVelocity.y > 3f ||
            Mathf.Abs(controllerVelocity.x) > 10f ||
            -controllerVelocity.z > 10f;

        // Reduce lift with height
        float heightFactor = Mathf.Clamp01(1f - height / maxHeight);
        controllerVelocity.y *= heightFactor;

        return controllerVelocity;
    }

    /// <summary>
    /// Smoothly limits falling speed while gliding.
    /// </summary>
    public static float CalculateGlideFallSpeed(
        float verticalVelocity,
        float glideLimit,
        float deltaTime)
    {
        if (verticalVelocity > glideLimit)
            return verticalVelocity;

        verticalVelocity -= deltaTime * verticalVelocity;
        return Mathf.Max(verticalVelocity, glideLimit);
    }

    // --------------------------------------------------
    // ROTATION
    // --------------------------------------------------

    /// <summary>
    /// Calculates arm angle in radians based on controller positions.
    /// </summary>
    public static float CalculateArmAngle(Vector3 leftPos, Vector3 rightPos)
    {
        Vector3 diff = leftPos - rightPos;
        return Mathf.Atan2(diff.y, Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z));
    }

    /// <summary>
    /// Calculates forward direction using the cross product of right and up vectors.
    /// </summary>
    public static Vector3 CalculateForwardDirection(Vector3 leftPos, Vector3 rightPos)
    {
        Vector3 rightVector = (rightPos - leftPos).normalized;
        rightVector.y = 0f;

        return Vector3.Cross(rightVector, Vector3.up).normalized;
    }

    /// <summary>
    /// Calculates yaw rotation based on arm angle and current velocity.
    /// </summary>
    public static float CalculateYaw(
        float currentYaw,
        float armAngleRad,
        Vector3 velocity,
        float maxVelocity,
        float deltaTime)
    {
        const float minAngleToRotate = 5f;

        float angleDeg = armAngleRad * Mathf.Rad2Deg;
        if (Mathf.Abs(angleDeg) <= minAngleToRotate)
            return currentYaw;

        float maxRotationSpeed = 110f * deltaTime;
        float minRotationSpeed = 20f * deltaTime;

        float rotationSpeed =
            armAngleRad * maxRotationSpeed *
            (Mathf.Abs(velocity.x) + Mathf.Abs(velocity.z)) / maxVelocity;

        rotationSpeed = Mathf.Clamp(rotationSpeed, -maxRotationSpeed, maxRotationSpeed);

        if (Mathf.Abs(rotationSpeed) < minRotationSpeed)
            rotationSpeed = Mathf.Sign(rotationSpeed) * minRotationSpeed;

        return currentYaw + rotationSpeed;
    }

    /// <summary>
    /// Calculates roll and pitch tilt based on arm angle and forward direction.
    /// </summary>
    public static Quaternion CalculateRoll(
        float armAngleRad,
        Vector3 forwardDirection)
    {
        float angleDeg = armAngleRad * Mathf.Rad2Deg;
        Vector2 horizontalDir = new Vector2(forwardDirection.x, forwardDirection.z).normalized;

        return Quaternion.Euler(
            horizontalDir.y * angleDeg,
            0f,
            -horizontalDir.x * angleDeg);
    }

    // --------------------------------------------------
    // COLLISION RESPONSE (PURE)
    // --------------------------------------------------

    /// <summary>
    /// Resolves collision response by cancelling vertical velocity and
    /// reflecting horizontal movement if not grounded.
    /// </summary>
    public static Vector3 ResolveCollision(Vector3 velocity, bool grounded)
    {
        velocity.y = 0f;

        if (!grounded)
        {
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            velocity = -horizontal.normalized;
        }

        return velocity;
    }

    /// <summary>
    /// Determines if a surface normal counts as ground.
    /// </summary>
    public static bool IsGrounded(Vector3 hitNormal, float maxSlopeAngle)
    {
        return Vector3.Angle(Vector3.up, hitNormal) <= maxSlopeAngle;
    }

    /// <summary>
    /// Calculates the maximum arm length for gliding and determines if the player is currently gliding.
    /// </summary>
    public static bool CalculateIfGliding(Vector3 leftPos, Vector3 rightPos, ref float maxHorizontalArmLength)
    {
        float currentArmLength = Vector3.Distance(leftPos, rightPos);

        bool isGliding = currentArmLength > maxHorizontalArmLength * 0.9f;

        if (currentArmLength > maxHorizontalArmLength)
        {
            maxHorizontalArmLength = currentArmLength;
        }

        return isGliding;
    }
}