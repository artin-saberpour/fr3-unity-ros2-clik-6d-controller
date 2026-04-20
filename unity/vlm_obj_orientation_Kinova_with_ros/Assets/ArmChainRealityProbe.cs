using UnityEngine;

public class ArmChainRealityProbe : MonoBehaviour
{
    public string shoulderName = "mixamorig4:RightShoulder";
    public string armName = "mixamorig4:RightArm";
    public string foreArmName = "mixamorig4:RightForeArm";
    public string handName = "mixamorig4:RightHand";

    [ContextMenu("Probe Arm Chain")]
    public void ProbeArmChain()
    {
        Transform shoulder = GameObject.Find(shoulderName)?.transform;
        Transform arm = GameObject.Find(armName)?.transform;
        Transform foreArm = GameObject.Find(foreArmName)?.transform;
        Transform hand = GameObject.Find(handName)?.transform;

        Debug.Log($"shoulder: {Describe(shoulder)}");
        Debug.Log($"arm: {Describe(arm)}");
        Debug.Log($"foreArm: {Describe(foreArm)}");
        Debug.Log($"hand: {Describe(hand)}");

        Debug.Log($"arm under shoulder: {IsDescendantOf(arm, shoulder)}");
        Debug.Log($"foreArm under arm: {IsDescendantOf(foreArm, arm)}");
        Debug.Log($"hand under foreArm: {IsDescendantOf(hand, foreArm)}");
        Debug.Log($"hand under arm: {IsDescendantOf(hand, arm)}");
        Debug.Log($"hand under shoulder: {IsDescendantOf(hand, shoulder)}");
    }

    [ContextMenu("Test Rotate ForeArm 10 Deg")]
    public void TestRotateForeArm()
    {
        Transform foreArm = GameObject.Find(armName)?.transform;
        Transform hand = GameObject.Find(handName)?.transform;

        if (foreArm == null || hand == null)
        {
            Debug.LogError("Missing foreArm or hand.");
            return;
        }

        Vector3 before = hand.position;
        foreArm.rotation = Quaternion.AngleAxis(10f, foreArm.up) * foreArm.rotation;
        Vector3 after = hand.position;

        Debug.Log($"Hand position before: {before}");
        Debug.Log($"Hand position after:  {after}");
        Debug.Log($"Hand moved distance:  {Vector3.Distance(before, after):G6}");
    }

    private static string Describe(Transform t)
    {
        if (t == null) return "<null>";
        return $"{t.name}, parent={(t.parent ? t.parent.name : "<root>")}, worldPos={t.position}";
    }

    private static bool IsDescendantOf(Transform node, Transform ancestor)
    {
        if (node == null || ancestor == null) return false;

        Transform cur = node;
        while (cur != null)
        {
            if (cur == ancestor) return true;
            cur = cur.parent;
        }
        return false;
    }
}