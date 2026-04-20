using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class hands_motion_controller : MonoBehaviour
{
    public Transform left_hand_target, right_hand_target;
    public Transform left_hand, right_hand;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        left_hand.position = left_hand_target.position;
        left_hand.eulerAngles = left_hand_target.eulerAngles;
        

        right_hand.position = right_hand_target.position;
        right_hand.eulerAngles = right_hand_target.eulerAngles;


    }
}
