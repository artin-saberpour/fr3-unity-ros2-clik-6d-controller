using UnityEngine;


/// <summary>
/// Cancels Orient Anything (OA) angles (theta, phi, delta) in the object's local frame.
/// After cancellation: object.forward ≈ -camera.forward, and camera roll (delta) is removed.
/// 
/// References:
///   - OA defines relative polar θ and azimuth φ between camera position and object's orientation axis,
///     plus camera rotation δ about the viewing ray. (See Fig. 3.c and Step 3 in the paper.)
/// </summary>
public class OrientAnythingApplier : MonoBehaviour
{
    public GameObject target_go;
    public Transform target;
    public Camera refCam;
    void Start()
    {
        target = target_go.transform;
    }

    static Matrix4x4 F_Z(float theta, float psi)
    {
        float ct = Mathf.Cos(theta), st = Mathf.Sin(theta);
        float cp = Mathf.Cos(psi), sp = Mathf.Sin(psi);
        // columns: e_theta, e_psi, e_r
        var c0 = new Vector3(ct * cp, ct * sp, -st);
        var c1 = new Vector3(-sp, cp, 0f);
        var c2 = new Vector3(st * cp, st * sp, ct);
        var M = Matrix4x4.identity;
        M.SetColumn(0, new Vector4(c0.x, c0.y, c0.z, 0));
        M.SetColumn(1, new Vector4(c1.x, c1.y, c1.z, 0));
        M.SetColumn(2, new Vector4(c2.x, c2.y, c2.z, 0));
        return M;
    }

static Matrix4x4 F_U(float theta, float psi) {
    float ct = Mathf.Cos(theta), st = Mathf.Sin(theta);
    float cp = Mathf.Cos(psi),   sp = Mathf.Sin(psi);
    var c0 = new Vector3(ct*sp, -st,  ct*cp);
    var c1 = new Vector3(   cp,  0f,    -sp);
    var c2 = new Vector3(st*sp,  ct,  st*cp);
    var M = Matrix4x4.identity;
    M.SetColumn(0, new Vector4(c0.x, c0.y, c0.z, 0));
    M.SetColumn(1, new Vector4(c1.x, c1.y, c1.z, 0));
    M.SetColumn(2, new Vector4(c2.x, c2.y, c2.z, 0));
    return M;
}

    static Quaternion FromColumns(Vector3 x, Vector3 y, Vector3 z) {
        // Use LookRotation since Unity defines axes consistently with forward/up. 
        // Z = forward, Y = up. (docs) 
        return Quaternion.LookRotation(z, y); // :contentReference[oaicite:1]{index=1}
    }

// Quaternion LocalToWorldRotation(Vector3 A_world, 
//                                 float azimLocal, float polarLocal, float rollLocalCW) 
public Quaternion LocalToWorldRotation(float azimLocal, float polarLocal, float rollLocalCW) 
{
    Vector3 A_world = target.InverseTransformPoint(refCam.transform.position - target.position);
    // 1) Unity spherical angles of A_world
        float r = A_world.magnitude;
    if (r == 0f) throw new System.ArgumentException("A_world is zero.");
    float thetaW = Mathf.Acos(Mathf.Clamp(A_world.y / r, -1f, 1f)); // down from +Y
    float psiW   = Mathf.Atan2(A_world.x, A_world.z);               // about +Y

    // 2) Fix local signs (local azim/roll were clockwise-positive)
    float thetaL = polarLocal * Mathf.Deg2Rad;
    float psiLccw = -azimLocal * Mathf.Deg2Rad;     // flip if your azimuth was CW-positive
    float rho = -rollLocalCW * Mathf.Deg2Rad;       // CW -> CCW

    // 3) Compose
    Matrix4x4 FU = F_U(thetaW, psiW);
    Matrix4x4 FL = F_Z(thetaL, psiLccw);
    float c = Mathf.Cos(rho), s = Mathf.Sin(rho);

    // R_roll in the spherical basis (acts on columns 0..1)
    var Rroll = Matrix4x4.identity;
    Rroll.m00 =  c; Rroll.m01 = -s;
    Rroll.m10 =  s; Rroll.m11 =  c;

    Matrix4x4 R = FU * Rroll * FL.transpose;

    // Turn matrix into a Quaternion for Unity (Z=forward, Y=up)
    Vector3 xW = new Vector3(R.m00, R.m10, R.m20);
    Vector3 yW = new Vector3(R.m01, R.m11, R.m21);
    Vector3 zW = new Vector3(R.m02, R.m12, R.m22);
    return FromColumns(xW, yW, zW);
}

}










// using System.Threading;
// using UnityEngine;

// public class OrientAnythingApplier : MonoBehaviour
// {
//     [Header("Must be the camera that took the photo")]
//     public Camera referenceCamera;

//     [Header("Angle inputs (degrees)")]
//     public float phiDeg;    // azimuth (OA)
//     public float thetaDeg;  // polar (OA)
//     public float deltaDeg;  // roll (OA)
//     [Range(0, 1)] public float hasFrontConfidence = 1f;

//     public float phi_old, theta_old, delta_old;

//     [Header("Conversion toggles")]
//     // OA uses clockwise-positive for φ and δ; Unity uses CCW-positive → flip by default.
//     public bool invertPhi = true;   // φUnity = -φOA
//     public bool invertDelta = true;   // δUnity = -δOA
//     // Elevation handling: θ is polar angle; pitch = 90° − θ (keep this true unless you verify otherwise)
//     public bool usePitchAs90MinusTheta = true;
//     public GameObject target;

//     [ContextMenu("Apply From Inspector")]
//     public void ApplyFromInspector()
//     {
//         ApplyOrientAnything(phiDeg, thetaDeg, deltaDeg, hasFrontConfidence);
//     }

//     public void ApplyOrientAnything_CancelAsDelta(float phi, float theta, float delta, float conf = 1f)
//     {
//         if (phi == phi_old && theta == theta_old && delta == delta_old)
//         {
//             return;
//         }
//         phi_old = phi;
//         theta_old = theta;
//         delta_old = delta;
//         if (!referenceCamera) { Debug.LogError("referenceCamera is null."); return; }
//         if (conf < 0.5f) { Debug.Log("low confidence; skipping."); return; }

//         // --- OA → your chosen convention (same as your code) ---
//         float phiU = invertPhi ? -phi : phi;              // yaw in camera space
//         float deltaU = invertDelta ? -delta : delta;            // roll about view axis
//         float pitch = usePitchAs90MinusTheta ? (90f - theta)    // pitch in camera space
//                                             : (theta - 90f);

//         // --- Build forward/up in CAMERA space (same construction as before) ---
//         Vector3 fwdCam =
//             Quaternion.AngleAxis(phiU, Vector3.up) *
//             Quaternion.AngleAxis(pitch, Vector3.right) *
//             (-Vector3.forward);
//         if (fwdCam.sqrMagnitude < 1e-6f) fwdCam = -Vector3.forward;
//         fwdCam.Normalize();

//         Vector3 upCam = Quaternion.AngleAxis(deltaU, fwdCam) * Vector3.up;
//         // Fallback if up ~ parallel to forward
//         if (Mathf.Abs(Vector3.Dot(upCam.normalized, fwdCam)) > 0.98f)
//             upCam = Quaternion.AngleAxis(deltaU, fwdCam) * Vector3.right;

//         Quaternion rotInCamera = Quaternion.LookRotation(fwdCam, upCam);

//         // --- The key: cancel in camera, then lift to world as a DELTA ---
//         Quaternion cancelInCamera = Quaternion.Inverse(rotInCamera);
//         Quaternion deltaWorld =
//             referenceCamera.transform.rotation * cancelInCamera * Quaternion.Inverse(referenceCamera.transform.rotation);

//         // Apply as a world-space delta to the CURRENT rotation.
//         // If OA=identity, deltaWorld=identity → no change.
//         target.transform.rotation = deltaWorld * target.transform.rotation;
//     }

//     public void ApplyOA_UndoDelta(float phi, float theta, float delta, float conf = 1f)
//     {
//         if (phi == phi_old && theta == theta_old && delta == delta_old)
//         {
//             return;
//         }
//         phi_old = phi;
//         theta_old = theta;
//         delta_old = delta;
//         if (!referenceCamera) { Debug.LogError("referenceCamera is null."); return; }
//         if (conf < 0.5f) return;

//         Quaternion R_cam = BuildRotInCamera(phi, theta, delta); // OA pose in CAMERA coords

//         // World-space delta that equals Inverse(R_cam) when expressed in camera coords
//         Quaternion deltaWorld =
//             referenceCamera.transform.rotation *
//             Quaternion.Inverse(R_cam) *
//             Quaternion.Inverse(referenceCamera.transform.rotation);

//         // Apply as a delta to the CURRENT pose (this is the key)
//         target.transform.rotation = deltaWorld * target.transform.rotation;
//     }


//     Quaternion BuildRotInCamera(float phi, float theta, float delta)
//     {
//         float phiU = invertPhi ? -phi : phi;             // yaw about cam.up
//         float deltaU = invertDelta ? -delta : delta;           // roll about view axis
//         float pitch = usePitchAs90MinusTheta ? (90f - theta) : (theta - 90f);

//         Vector3 fwdCam =
//             Quaternion.AngleAxis(phiU, Vector3.up) *
//             Quaternion.AngleAxis(pitch, Vector3.right) *
//             (-Vector3.forward);

//         if (fwdCam.sqrMagnitude < 1e-6f) fwdCam = -Vector3.forward;
//         fwdCam.Normalize();

//         Vector3 upCam = Quaternion.AngleAxis(deltaU, fwdCam) * Vector3.up;
//         if (Mathf.Abs(Vector3.Dot(upCam.normalized, fwdCam)) > 0.98f)
//             upCam = Quaternion.AngleAxis(deltaU, fwdCam) * Vector3.right;

//         return Quaternion.LookRotation(fwdCam, upCam);
//     }






//     public void ApplyOrientAnything_CancelToCamera(float phi, float theta, float delta, float conf = 1f)
//     {
//         if (!referenceCamera) { Debug.LogError("referenceCamera is null."); return; }
//         if (conf < 0.5f) { Debug.Log("low confidence; skipping."); return; }

//         float phiU = invertPhi ? -phi : phi;
//         float deltaU = invertDelta ? -delta : delta;
//         float pitch = usePitchAs90MinusTheta ? (90f - theta) : (theta - 90f);

//         // --- Build camera-space R_cam exactly as you did ---
//         Vector3 fwdCam =
//             Quaternion.AngleAxis(phiU, Vector3.up) *
//             Quaternion.AngleAxis(pitch, Vector3.right) *
//             (-Vector3.forward);
//         if (fwdCam.sqrMagnitude < 1e-6f) fwdCam = -Vector3.forward;
//         fwdCam.Normalize();

//         Vector3 upCam = Quaternion.AngleAxis(deltaU, fwdCam) * Vector3.up;
//         if (Mathf.Abs(Vector3.Dot(upCam.normalized, fwdCam)) > 0.98f)
//             upCam = Quaternion.AngleAxis(deltaU, fwdCam) * Vector3.right;

//         Quaternion R_cam = Quaternion.LookRotation(fwdCam, upCam);

//         // --- Choose your canonical camera-space pose ---
//         Quaternion R0_cam = Quaternion.LookRotation(-Vector3.forward, Vector3.up);

//         // --- Absolute: cancel OA to canonical in camera space, then lift to world ---
//         Quaternion deltaCam = R0_cam * Quaternion.Inverse(R_cam);
//         target.transform.rotation = referenceCamera.transform.rotation * deltaCam;
//     }





//     public void ApplyOrientAnything(float phi, float theta, float delta, float conf = 1f)
//     {
//         if (!referenceCamera)
//         {
//             Debug.LogError("OrientAnythingApplier: referenceCamera is null.");
//             return;
//         }
//         if (conf < 0.5f)
//         {
//             Debug.Log("OrientAnythingApplier: low confidence; skipping.");
//             return;
//         }

//         // --- Convert OA → Unity conventions ---
//         float phiU = invertPhi ? -phi : phi;   // CW→CCW
//         float deltaU = invertDelta ? -delta : delta; // CW→CCW
//         float pitch = usePitchAs90MinusTheta ? (90f - theta) : (theta - 90f);

//         // --- Build forward/up in CAMERA space ---
//         // Start with forward-to-camera (-Z), then yaw by φ, then pitch by (90-θ).
//         Vector3 fwdCam =
//             Quaternion.AngleAxis(phiU, Vector3.up) *
//             Quaternion.AngleAxis(pitch, Vector3.right) *
//             (-Vector3.forward);
//         if (fwdCam.sqrMagnitude < 1e-6f) fwdCam = -Vector3.forward;
//         fwdCam.Normalize();

//         // Roll around the view axis
//         Vector3 upCam = Quaternion.AngleAxis(deltaU, fwdCam) * Vector3.up;
//         if (Mathf.Abs(Vector3.Dot(upCam.normalized, fwdCam)) > 0.98f)
//             upCam = Quaternion.AngleAxis(deltaU, fwdCam) * Vector3.right;

//         Quaternion rotInCamera = Quaternion.LookRotation(fwdCam, upCam);

//         // Camera → World
//         target.transform.rotation = referenceCamera.transform.rotation * Quaternion.Inverse(rotInCamera);// referenceCamera.transform.rotation * rotInCamera;
//     }

// #if UNITY_EDITOR
//     // Optional gizmos to see what’s going on
//     void OnDrawGizmosSelected()
//     {
//         Gizmos.color = Color.blue;  // forward
//         Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);
//         Gizmos.color = Color.green; // up
//         Gizmos.DrawLine(transform.position, transform.position + transform.up * 0.5f);
//         Gizmos.color = Color.red;   // right
//         Gizmos.DrawLine(transform.position, transform.position + transform.right * 0.5f);
//     }
// #endif
// }























































// using UnityEngine;

// /// <summary>
// /// Applies Orient Anything (φ, θ, δ) to a Unity object.
// /// Assumes object's local +Z is its "front", +Y is "up".
// /// </summary>
// public class OrientAnythingApplier : MonoBehaviour
// {
//     [Tooltip("Camera that took the photo (must match that viewpoint).")]
//     public Camera referenceCamera;
//     public GameObject target;

//     /// <param name="phiDeg">Azimuth in degrees [0..360)</param>
//     /// <param name="thetaDeg">Polar in degrees [0..180]</param>
//     /// <param name="deltaDeg">Roll in degrees [0..360)</param>
//     /// <param name="hasFrontConfidence">Confidence [0..1]</param>
//     // / target
//     public void ApplyOrientAnything(float phiDeg, float thetaDeg, float deltaDeg, float hasFrontConfidence = 1f)
//     {
//         if (referenceCamera == null)
//         {
//             Debug.LogError("OrientAnythingApplier: referenceCamera is null.");
//             return;
//         }

//         if (hasFrontConfidence < 0.5f)
//         {
//             Debug.Log("OrientAnythingApplier: low orientation confidence; skipping rotation.");
//             return;
//         }

//         // --- Build the object's forward in CAMERA space ---
//         // Intuition:
//         //   - Start with object facing the camera: forward = -Z_cam.
//         //   - Yaw around camera up by φ (front/left/back/right).
//         //   - Pitch around camera right by (90° - θ):
//         //        θ=90° → same height (no vertical tilt),
//         //        θ<90° → camera above (tilt up),
//         //        θ>90° → camera below (tilt down).
//         Vector3 fwdCam =
//             Quaternion.AngleAxis(phiDeg, Vector3.up) *
//             Quaternion.AngleAxis(90f - thetaDeg, Vector3.right) *
//             (-Vector3.forward);

//         // Guard against degenerate numeric cases
//         if (fwdCam.sqrMagnitude < 1e-6f)
//             fwdCam = -Vector3.forward;
//         fwdCam.Normalize();

//         // --- Apply roll δ around the viewing axis (object's forward) ---
//         // δ is defined as camera rotation about the view ray.
//         // To mirror that in object orientation, rotate the "up" around fwdCam by +δ.
//         Vector3 upCam = Quaternion.AngleAxis(deltaDeg, fwdCam) * Vector3.up;

//         // If up is too parallel to forward (edge case), choose a safe up
//         if (Vector3.Dot(upCam, fwdCam) > 0.98f)
//             upCam = Quaternion.AngleAxis(deltaDeg, fwdCam) * Vector3.right;

//         // Rotation in *camera space*
//         Quaternion rotInCamera = Quaternion.LookRotation(fwdCam, upCam);

//         // Convert to *world space* using the camera's world rotation
//         Quaternion worldRot = referenceCamera.transform.rotation * rotInCamera;
//         Debug.LogError("running the applier" + worldRot.ToString());
//         // Finally set the object's rotation
//         target.transform.rotation = worldRot;
//     }

//     /// <summary>
//     /// If you only care about horizontal facing (ignoring tilt & roll),
//     /// use azimuth-only yaw.
//     /// </summary>
//     public void ApplyAzimuthOnly(float phiDeg)
//     {
//         Vector3 fwdCamXZ = new Vector3(
//             -Mathf.Sin(phiDeg * Mathf.Deg2Rad), // left/right
//             0f,
//             -Mathf.Cos(phiDeg * Mathf.Deg2Rad)  // front/back
//         );
//         if (fwdCamXZ.sqrMagnitude < 1e-6f) fwdCamXZ = -Vector3.forward;

//         Quaternion rotInCamera = Quaternion.LookRotation(fwdCamXZ, Vector3.up);
//         target.transform.rotation = referenceCamera.transform.rotation * rotInCamera;
//     }
// }
