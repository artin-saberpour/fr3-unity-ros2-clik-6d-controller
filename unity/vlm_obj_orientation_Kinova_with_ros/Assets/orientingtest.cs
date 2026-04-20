using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class orientingtest : MonoBehaviour
{
    // Start is called before the first frame update

    public Quaternion knife_orientation;// = new Quaternion(-0.1680983f, -0.02303879f, 0.2465608f, 0.9541592f);
    public Quaternion blade_local_rotation, handle_local_rotation;// = new Quaternion(0.17877231781895978f, 0.025932081084757516f, -0.2460888071923991f, 0.9522648184863666f);    void Start()
    public Quaternion result;
    void Start()
    {
        //transform.rotation = new Quaternion(-0.08321197f,  0.85088979f,  0.27734485f,  0.43834025f) * transform.rotation; //(-0.4549f, -0.3423f, 0.3296f, 0.7532f);
        //transform.rotation *= transform.rotation;
        Vector3 originalVector = new Vector3(-0.00689584f, -0.00339573f, 0.00066437f); //(-0.87797515f, 0.0914135f, -0.02549064f);

        //Vector3 originalVector = Vector3.forward;   // Initial direction
        Vector3 targetVector = new Vector3(-2.44000006f, 0.439999998f, -2.06999993f); //(-2.99000001f, 0.786479831f, -3.30999994f); // Target direction
        targetVector.Normalize();                   // Always normalize directions

        // Quaternion rotation = Quaternion.FromToRotation(originalVector, targetVector);
        Quaternion rotation = new Quaternion(0.1994397f, -0.44982723f, -0.22906894f, 0.83988493f); //(-0.03304313f, -0.32581143f, -0.03030515f, 0.94437104f);
        Vector3 rotatedVector = rotation * originalVector;
        // transform.rotation = rotation * transform.rotation;


        // transform.rotation =   new Quaternion( 0.46517501f, -0.11872432f, -0.16573817f, 0.86142185f) * transform.rotation; //(0.29761647f, -0.10156771f, 0.9114091f, 0.265f); 
        // transform.rotation = transform.rotation * new Quaternion(-0.6015319f,  -0.08071386f,  0.79228795f,  0.06264543f);
        Quaternion optimized_blade = new Quaternion(0.91524597f, 0.0331259f, 0.30655955f, -0.25932359f);
        Quaternion optimized_handle = new Quaternion(-0.1933264f, 0.86497754f, 0.45606862f, 0.08025064f);
        // transform.rotation = optimized_handle * transform.rotation;



        knife_orientation = new Quaternion(-0.1680983f, -0.02303879f, 0.2465608f, 0.9541592f);
        blade_local_rotation = new Quaternion(0.17877231781895978f, 0.025932081084757516f, -0.2460888071923991f, 0.9522648184863666f);
        handle_local_rotation = new Quaternion(0.164396236232614f, 0.022212871721848493f, -0.24644800047845208f, 0.9548527891264664f);
        targetVector = handle_local_rotation * knife_orientation * Vector3.right;
        result = knife_orientation * handle_local_rotation;
        Debug.LogError(result);
        // targetVector = ApplyQuaternionToVector(blade_local_rotation * knife_orientation, Vector3.right);
        Vector3 handle_global = new Vector3(-0.4411751230557759f, 0.010387882590293884f, 0.025754325091838837f);


        // Debug.DrawRay(transform.position, new Vector3(3.289794944127401f, -0.3485865020751953f, 2.0445093584060667f) * 100, Color.green, 500f);
        // Debug.DrawRay(transform.position, new Vector3(-1.562024933497111f, 0.3485865020751953f, -2.0445093584060667f) * 100, Color.red, 500f);
        // Debug.DrawRay(transform.position, new Vector3(0.99976714f, 0.00807205f, 0.02001277f) * 100, Color.blue, 500f);
        // Debug.DrawRay(transform.position, new Vector3(-0.99802475f,  0.02349943f,  0.05826134f) * 100, Color.magenta, 500f);

        Debug.DrawRay(transform.position, new Vector3(3.849794944127401f, -0.9085865020751953f, 1.974509358406067f) * 100, Color.green, 500f);
        Debug.DrawRay(transform.position, new Vector3(-2.1220249334971113f, 0.9085865020751953f, -1.974509358406067f) * 100, Color.red, 500f);
        Debug.DrawRay(transform.position, new Vector3(0.99976714f, 0.00807205f, 0.02001277f) * 100, Color.blue, 500f);
        Debug.DrawRay(transform.position, new Vector3(-0.99802475f, 0.02349943f, 0.05826134f) * 100, Color.magenta, 500f);

        // transform.rotation = new Quaternion(0.36241106f, -0.32381681f, -0.02218867f, 0.87367532f) * transform.rotation;
        /////////////////////////////////////////// transform.rotation = new Quaternion( 0.89750809f, -0.03914427f,  0.30262044f, -0.31838315f) * transform.rotation;




        // Debug.DrawRay(transform.position, rotatedVector * 100, Color.red, 500f);
        // Debug.DrawRay(transform.position, handle_global * 1000, Color.blue, 500f);
        // Debug.DrawRay(transform.position, new Quaternion(  0.90251172f, 0.03709374f, 0.34451606f, -0.25574467f) * handle_global * 1000, Color.magenta, 500f);

        // transform.rotation =  new Quaternion(0.90251172f, 0.03709374f, 0.34451606f, -0.25574467f);








        // // Get direction vector from this object to target
        // Vector3 direction = new Vector3(-0.66f,  0.1736f, -0.7308f);// target.position - transform.position;

        // // Create rotation that looks in that direction
        // Quaternion rotation = Quaternion.LookRotation(direction);

        // // Apply rotation
        // transform.rotation = rotation;
    }
    Vector3 ApplyQuaternionToVector(Quaternion q, Vector3 v)
    {
        return q * v; // Quaternion rotation applied directly
    }
    // Update is called once per frame
    void Update()
    {
        result = blade_local_rotation * knife_orientation;
        Debug.LogError(result);
        
    }
}
