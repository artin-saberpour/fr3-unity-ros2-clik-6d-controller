## 🎥 Demo

<video src="https://github.com/user-attachments/assets/af639375-576d-4b2c-9606-df6b9f720daa" controls width="700"></video>







# Kinova 6-DoF Workspace Simulator with Closed-Loop Inverse Kinematics (CLIK)

A ROS 2-controlled simulator for a Kinova 6-DoF robotic manipulator, with Unity used for real-time visualization and interaction.

---

## Overview

This project simulates a Kinova robotic arm controlled in Cartesian workspace space using a **Closed-Loop Inverse Kinematics (CLIK)** controller.

Instead of commanding individual joint positions, the user sends a desired **6-DoF end-effector pose**:

- Position: `x, y, z`
- Orientation: quaternion or roll-pitch-yaw

The controller computes the required **joint velocities** in real time so the robot converges smoothly to the target pose.

ROS 2 is used for communication and control, while Unity visualizes the robot motion, target poses, and environment.

---

## Key Features

- 6-DoF end-effector workspace control  
- Joint velocity-based control  
- Closed-Loop Inverse Kinematics (CLIK)  
- ROS 2 integration  
- Real-time Unity visualization  
- Pose tracking and convergence testing  
- Target pose publishing through ROS 2 topics  
- Simulation before real hardware deployment  

---

## Control Method

The simulator uses a standard CLIK formulation:

q_dot = J⁺(q) (x_dot_d + K e)

Where:

- `q_dot` = joint velocity vector  
- `J⁺` = Jacobian pseudoinverse  
- `x_dot_d` = desired Cartesian velocity  
- `e` = task-space pose error  
- `K` = controller gain  

This allows smooth closed-loop tracking of position and orientation targets.

---

## System Architecture

ROS 2 Node  
→ publishes desired end-effector pose  
→ CLIK Controller  
→ computes joint velocities  
→ updates robot joints  
→ Unity visualizes robot motion

---

## Example ROS 2 Command

```bash
ros2 topic pub -1 /ee_target_pose geometry_msgs/msg/PoseStamped "
header:
  frame_id: 'world'
pose:
  position:
    x: 0.30
    y: 0.25
    z: 0.30
  orientation:
    x: 0
    y: 0
    z: 0
    w: 1
"
