using UnityEngine;

public static class OAUndoLocal
{
    /// Cancels OA's (φ,θ,δ) ONCE "in the camera's view", with δ applied about the correct axis.
    /// Call with angles from the CURRENT frame (do not re-apply without re-estimating).
    public static void Undo(Camera cam, Transform target,
                            float phi, float theta, float delta, float confidence = 1f,
                            bool rollAboutCameraZ = false) // set true if your δ is explicitly image-plane roll
    {
        if (!cam || !target || confidence < 0.5f) return;

        // --- OA(clockwise) → Unity(right-hand) mapping ---
        float yawDeg   = -phi;         // yaw about +cam.up
        float pitchDeg =  90f - theta; // pitch about +cam.right
        float rollDeg  = -delta;       // roll sign mapping

        Quaternion C = cam.transform.rotation;

        // === 1) Undo yaw & pitch in CAMERA frame ===
        // Build R_yawpitch in camera coords (rightmost acts first: yaw then pitch)
        Quaternion Ry      = Quaternion.AngleAxis(yawDeg,   Vector3.up);
        Quaternion Rx      = Quaternion.AngleAxis(pitchDeg, Vector3.right);
        Quaternion R_ypCam = Rx * Ry;

        // Δ_world that cancels yaw+pitch:  C * (R_ypCam)^-1 * C^-1
        Quaternion deltaWorldYP = C * Quaternion.Inverse(R_ypCam) * Quaternion.Inverse(C);
        target.rotation = deltaWorldYP * target.rotation;

        // After this, target.forward ≈ -cam.forward in world

        // === 2) Undo roll δ ===
        if (rollAboutCameraZ)
        {
            // Treat δ as image-plane roll: axis = +cam.forward
            Quaternion RzCam = Quaternion.AngleAxis(rollDeg, Vector3.forward);
            Quaternion deltaWorldRoll = C * Quaternion.Inverse(RzCam) * Quaternion.Inverse(C);
            target.rotation = deltaWorldRoll * target.rotation;
        }
        else
        {
            // Equivalent using the object's current forward (≈ -cam.forward):
            // roll about +cam.Z == roll about -target.forward → negate the angle or flip the axis.
            Vector3 axisWorld = -target.forward;                // aligns with +cam.Z after step 1
            Quaternion deltaWorldRoll = Quaternion.AngleAxis(-rollDeg, axisWorld);
            target.rotation = deltaWorldRoll * target.rotation;
        }

#if UNITY_EDITOR
        // Diagnostics: in camera space, +Z of object should face viewer (≈ -Z), +Y upright.
        Quaternion camSpace = Quaternion.Inverse(C) * target.rotation;
        float fErr = Vector3.Angle(camSpace * Vector3.forward, -Vector3.forward); // want ~0°
        float uErr = Vector3.Angle(camSpace * Vector3.up,        Vector3.up);      // want ~0°
        Debug.Log($"OA Undo: fwdErr={fErr:F2}°, upErr={uErr:F2}° (0/0 ideal)");
#endif
    }
}
