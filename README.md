## 🎥 Demo

<video src="https://github.com/user-attachments/assets/af639375-576d-4b2c-9606-df6b9f720daa" controls width="700"></video>





# Kinova 6-DoF Workspace Simulator with Closed-Loop Inverse Kinematics (CLIK)

A ROS 2-integrated simulation environment for controlling a Kinova 6-DoF robotic manipulator in Cartesian workspace space, with Unity providing real-time visualization and interaction.

---

## Overview

This project provides a simulation framework for workspace control of a Kinova robotic arm using a **Closed-Loop Inverse Kinematics (CLIK)** controller.

Rather than commanding individual joints directly, the robot is controlled through desired end-effector position and orientation targets in 3D space. The controller continuously computes joint velocities required to minimize pose error and drive the manipulator toward the commanded target.

ROS 2 is used as the communication and control middleware, while Unity renders the robot, environment, and motion behavior in real time.

---

## Core Features

- Cartesian 6-DoF end-effector control  
- Joint velocity-based actuation  
- Closed-loop inverse kinematics (CLIK)  
- Real-time pose tracking  
- ROS 2 communication interface  
- Unity-based visualization  
- Modular simulation architecture  
- Suitable for controller development and testing  

---

## Control Strategy

The simulator implements a Jacobian-based CLIK controller that transforms task-space pose error into joint-space velocity commands.

Typical formulation:

**q̇ = J⁺(q) (ẋ_d + K e)**

Where:

- **q̇** = joint velocity vector  
- **J⁺(q)** = Jacobian pseudoinverse  
- **ẋ_d** = desired Cartesian velocity  
- **e** = pose error in task space  
- **K** = feedback gain matrix  

This enables stable and smooth convergence to commanded workspace targets.

---

## System Architecture

```text
Target Pose Command
        ↓
ROS 2 Communication Layer
        ↓
CLIK Controller
        ↓
Joint Velocity Commands
        ↓
Kinova Robot Model
        ↓
Unity Visualization
