#!/usr/bin/env python3
import math
import numpy as np

import rclpy
from rclpy.node import Node
from geometry_msgs.msg import PoseStamped
from sensor_msgs.msg import JointState

import PyKDL
from urdf_parser_py.urdf import URDF


# from robot_state_publisher.robot_state_publisher import RobotStatePublisher


def rpy_to_kdl(r, p, y):
    return PyKDL.Rotation.RPY(r, p, y)


def vec3_to_kdl(v):
    return PyKDL.Vector(v[0], v[1], v[2])


def normalize(v):
    n = math.sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2])
    if n < 1e-12:
        return (0.0, 0.0, 1.0)
    return (v[0]/n, v[1]/n, v[2]/n)


class ee_to_joint_inverse_kinematics(Node):
    def __init__(self):
        super().__init__(node_name="ee_to_joint_ik_urdf_kdl")

        # -------- Parameters --------
        self.declare_parameter("urdf_path", "/home/rtn/franka_urdf/franka_fr3.urdf")
        self.declare_parameter("base_link", "base")
        self.declare_parameter("tip_link", "fr3_hand_tcp")
        self.declare_parameter("rate_hz", 100.0)
        self.declare_parameter("gain", 1.0)
        self.declare_parameter("damping", 1e-4)  # damped least squares

        urdf_path = self.get_parameter("urdf_path").get_parameter_value().string_value
        self.base_link = self.get_parameter("base_link").get_parameter_value().string_value
        self.tip_link = self.get_parameter("tip_link").get_parameter_value().string_value
        self.rate_hz = float(self.get_parameter("rate_hz").value)
        self.gain = float(self.get_parameter("gain").value)
        self.lmbd = float(self.get_parameter("damping").value)

        if not urdf_path:
            raise RuntimeError("Set urdf_path parameter to your FR3 URDF file")

        self.robot = URDF.from_xml_file(urdf_path)

        # -------- Build chain from URDF --------
        self.joint_names, self.chain = self.build_chain_from_urdf(self.robot, self.base_link, self.tip_link)
        self.n = len(self.joint_names)

        self.get_logger().info(f"Built chain {self.base_link} -> {self.tip_link} with {self.n} joints")
        self.get_logger().info(f"Joints: {self.joint_names}")

        self.fk = PyKDL.ChainFkSolverPos_recursive(self.chain)
        self.jac_solver = PyKDL.ChainJntToJacSolver(self.chain)

        self.q = PyKDL.JntArray(self.n)
        for i in range(self.n):
            self.q[i] = 0.0

        self.target_frame = None

        self.sub = self.create_subscription(PoseStamped, "/ee_target_pose", self.cb_target, 10)
        self.pub = self.create_publisher(JointState, "/joint_states", 10)

        dt = 1.0 / self.rate_hz
        self.timer = self.create_timer(dt, self.step)

    def build_chain_from_urdf(self, robot: URDF, base: str, tip: str):
        # Build child->joint map
        child_to_joint = {j.child: j for j in robot.joints if j.child}

        # Walk tip -> base collecting joints
        joints_reversed = []
        cur = tip
        while cur != base:
            if cur not in child_to_joint:
                raise RuntimeError(f"Cannot find parent joint for link '{cur}'. Check base/tip link names.")
            j = child_to_joint[cur]
            joints_reversed.append(j)
            cur = j.parent

        joints = list(reversed(joints_reversed))

        chain = PyKDL.Chain()
        joint_names = []

        for j in joints:
            if j.type not in ["revolute", "continuous"]:
                continue

            joint_names.append(j.name)

            # --- URDF origin: parent_link -> joint frame
            xyz = (0.0, 0.0, 0.0)
            rpy = (0.0, 0.0, 0.0)
            if j.origin is not None:
                if j.origin.xyz is not None:
                    xyz = tuple(float(x) for x in j.origin.xyz)
                if j.origin.rpy is not None:
                    rpy = tuple(float(x) for x in j.origin.rpy)

            R_origin = rpy_to_kdl(rpy[0], rpy[1], rpy[2])
            p_origin = PyKDL.Vector(xyz[0], xyz[1], xyz[2])
            origin_frame = PyKDL.Frame(R_origin, p_origin)

            # 1) fixed origin segment (NO joint)
            chain.addSegment(
                PyKDL.Segment(
                    j.name + "_origin",
                    PyKDL.Joint(PyKDL.Joint()),
                    origin_frame
                )
            )

            # --- URDF axis is expressed IN JOINT FRAME (which we are now in)
            axis = (0.0, 0.0, 1.0)
            if j.axis is not None and len(j.axis) == 3:
                axis = normalize(tuple(float(a) for a in j.axis))

            axis_vec = PyKDL.Vector(axis[0], axis[1], axis[2])

            # 2) joint rotation segment (identity frame)
            kdl_joint = PyKDL.Joint(j.name, PyKDL.Vector.Zero(), axis_vec, PyKDL.Joint.RotAxis)

            chain.addSegment(
                PyKDL.Segment(
                    j.child,           # now we are at the child link frame after rotation
                    kdl_joint,
                    PyKDL.Frame.Identity()
                )
            )

        return joint_names, chain


    def cb_target(self, msg: PoseStamped):
        self.target_frame = PyKDL.Frame(
            PyKDL.Rotation.Quaternion(
                msg.pose.orientation.x,
                msg.pose.orientation.y,
                msg.pose.orientation.z,
                msg.pose.orientation.w,
            ),
            PyKDL.Vector(
                msg.pose.position.x,
                msg.pose.position.y,
                msg.pose.position.z,
            ),
        )

    def kdl_jacobian_to_numpy(self, jac):
        J = np.zeros((6, jac.columns()))
        for i in range(6):
            for j in range(jac.columns()):
                J[i, j] = jac[i, j]
        return J


    def step(self):
        if self.target_frame is None:
            return

        current = PyKDL.Frame()
        self.fk.JntToCart(self.q, current)
        print("p is: " + str(current.p))
        
        # Full 6D error (position + orientation)
        dp = self.target_frame.p - current.p

        dR = current.M.Inverse() * self.target_frame.M
        angle, axis = dR.GetRotAngle()
        print("axis: " + str(axis))
        print("angle: " + str(angle))
        if abs(angle) < 1e-6:
            dr = PyKDL.Vector.Zero()
        else:
            dr = PyKDL.Vector(axis[0]*angle, axis[1]*angle, axis[2]*angle)
        # dr = axis * angle

        # dR = self.target_frame.M * current.M.Inverse()
        # angle, axis = dR.GetRotAngle()
        # dr = PyKDL.Vector(axis[0]*angle, axis[1]*angle, axis[2]*angle)

        err = np.array([dp[0], dp[1], dp[2], dr[0], dr[1], dr[2]])

        # Jacobian
        jac = PyKDL.Jacobian(self.n)
        self.jac_solver.JntToJac(self.q, jac)
        # J = np.array(jac.data)
        J = self.kdl_jacobian_to_numpy(jac)
        # print("Jacobian shape:", J.shape)


        # Damped least squares: dq = J^T (J J^T + λ^2 I)^-1 err
        JJt = J @ J.T
        dq = J.T @ np.linalg.inv(JJt + (self.lmbd**2) * np.eye(6)) @ err

        # Integrate
        for i in range(self.n):
            self.q[i] += float(self.gain * dq[i]) * (1.0 / self.rate_hz)
            # Wrap to [-pi, pi] to prevent windup
            self.q[i] = (self.q[i] + math.pi) % (2 * math.pi) - math.pi
        # self.q[1] += 0.005

        # Publish
        js = JointState()
        js.header.stamp = self.get_clock().now().to_msg()
        js.name = self.joint_names
        js.position = [self.q[i] for i in range(self.n)]
        self.pub.publish(js)


def main():
    rclpy.init()
    node = ee_to_joint_inverse_kinematics()
    rclpy.spin(node)
    rclpy.shutdown()


if __name__ == "__main__":
    main()

