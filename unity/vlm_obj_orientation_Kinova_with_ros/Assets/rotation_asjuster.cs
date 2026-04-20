using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Meta.XR.MRUtilityKit.SceneDecorator;
using UnityEngine;
using System;
using UnityEngine.UIElements;

public class rotation_asjuster : MonoBehaviour
{
    // Start is called before the first frame update
    public Camera referenceCamera;
    // public GameObject target;

    [Header("Inspector Inputs (degrees)")]
    public float phiDeg;
    public float thetaDeg = 90f;
    public float deltaDeg;
    [Range(0,1)] public float hasFrontConfidence = 1f;
    public OrientAnythingApplier applier;
    // public OAUndoLocal applier1;
    public int i;
    public GameObject local_marker;
    public OAInverseRotation inv_rot;
    public UnityEngine.Quaternion rot;
    



    void Start()
    {
        // applier.referenceCamera = referenceCamera;// Camera.main; // or assign in Inspector
        // applier.ApplyOrientAnything(phiDeg, thetaDeg, deltaDeg, hasFrontConfidence);
        // inv_rot = new OAInverseRotation();
        inv_rot = local_marker.transform.GetComponent<OAInverseRotation>();
        i = 0;
        Debug.LogError(transform.forward);

    }

    // Update is called once per frame
    void Update()
    {
        Debug.LogError("it is running");
        // applier.ApplyOrientAnything(phiDeg, thetaDeg, deltaDeg, hasFrontConfidence);        
        // applier.ApplyOrientAnything_CancelToCamera(phiDeg, thetaDeg, deltaDeg, hasFrontConfidence);
        // applier.ApplyOrientAnything(phiDeg, thetaDeg, deltaDeg, hasFrontConfidence);
        if (i == 10)
        {
            local_marker.transform.position = new UnityEngine.Vector3(Mathf.Cos(phiDeg * Mathf.Deg2Rad) * Mathf.Sin(thetaDeg * Mathf.Deg2Rad),
                                                                                           Mathf.Sin(phiDeg * Mathf.Deg2Rad) * Mathf.Sin(thetaDeg * Mathf.Deg2Rad),
                                                                                           Mathf.Cos(thetaDeg));
            // applier.UndoOA_InCameraOnce(phiDeg, thetaDeg, deltaDeg, hasFrontConfidence);
            // OAUndoLocal.Undo(referenceCamera, transform,phiDeg, thetaDeg, deltaDeg, hasFrontConfidence);
            rot = inv_rot.get_rot_mat(phiDeg, thetaDeg, deltaDeg);

            transform.rotation = rot * transform.rotation;
            /*
            // UnityEngine.Quaternion rot = applier.LocalToWorldRotation(phiDeg, thetaDeg, deltaDeg);
            // local_marker.transform.position = transform.position + rot * local_marker.transform.position;
            */
            
        }
        i++;
    }
}
