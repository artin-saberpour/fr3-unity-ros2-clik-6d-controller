using UnityEngine;

public class OAInverseRotation : MonoBehaviour
{
    public struct Basis
    {
        public Vector3 z;      // object -> camera (unit) f
        public Vector3 x;      // camera up after roll (unit, ⟂ f) u
        public Vector3 y;      // right so (r,u,f) is right-handed (unit) r
        public Vector3 eTheta; // raw spherical tangent basis at (theta,phi)
        public Vector3 ePhi;   // raw spherical tangent basis at (theta,phi)
    }

    public Basis FromRadians(float phi, float theta, float delta)
    {
        theta -= Mathf.PI * 0.5f; 
        // --- Step 1: forward direction f (object -> camera)
        float st = Mathf.Sin(theta);
        float ct = Mathf.Cos(theta);
        float sp = Mathf.Sin(phi);
        float cp = Mathf.Cos(phi);

        Vector3 z = new Vector3(
            st * cp,
            -st * sp,
            ct
        ); // already unit

        // --- Step 2: exact spherical tangent basis at (theta,phi)
        Vector3 eTheta = new Vector3(
            ct * cp,
            -ct * sp,
            -st
        ); // unit, ⟂ f

        Vector3 ePhi = new Vector3(
            -sp,
            -cp,
            0f
        ); // unit, ⟂ f and ⟂ eTheta

        // --- Step 3: apply roll δ in the image plane (perp to f)
        float sd = Mathf.Sin(delta);
        float cd = Mathf.Cos(delta);
        Vector3 x = cd * eTheta + sd * ePhi; // unit, ⟂ f

        // --- Step 4: complete right-handed triad
        Vector3 y = Vector3.Cross(x, z); // (r,u,f) right-handed
        y.Normalize();

        return new Basis { z = z, x = x, y = y, eTheta = eTheta, ePhi = ePhi };
        // return new Basis { y = z, x = x, z = y, eTheta = eTheta, ePhi = ePhi };
    }

    public Basis FromDegrees( float phiDeg, float thetaDeg, float deltaDeg)
        => FromRadians(phiDeg * Mathf.Deg2Rad,
                       thetaDeg * Mathf.Deg2Rad,
                       deltaDeg * Mathf.Deg2Rad);

    public Quaternion rot_mat(Vector3 FrameX_new, Vector3 FrameY_new, Vector3 FrameZ_new)
    {
        Matrix4x4 R_frame = new Matrix4x4();
        R_frame.SetColumn(0, new Vector4(FrameX_new.x, FrameX_new.y, FrameX_new.z, 0));
        R_frame.SetColumn(1, new Vector4(FrameY_new.x, FrameY_new.y, FrameY_new.z, 0));
        R_frame.SetColumn(2, new Vector4(FrameZ_new.x, FrameZ_new.y, FrameZ_new.z, 0));
        R_frame.SetColumn(3, new Vector4(0, 0, 0, 1));

        Quaternion Q_inverse = R_frame.rotation; // Convert matrix to quaternion
        Q_inverse = Quaternion.Inverse(Q_inverse);
        return Q_inverse;
    }

    public Quaternion get_rot_mat(float phi, float theta, float delta)
    {
        Basis tmp = FromDegrees(phi,theta, delta);
        return rot_mat(tmp.x, tmp.y, tmp.z);
    }
}
