
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
        self.declare_parameter("urdf_path", "/home/rtn/kinova_urdf/kinova.urdf")
        self.declare_parameter("base_link", "base")
        self.declare_parameter("tip_link", "j2s6s200_end_effector")# j2s6s200_link_6
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
            raise RuntimeError("Set urdf_path parameter to your Kinova URDF file")

        # self.robot = URDF.from_xml_file(urdf_path)
        with open(urdf_path, "rb") as f:
            self.robot = URDF.from_xml_string(f.read())

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

        self.obstacle_segments = [
            (
                np.array([-0.169220358,0.256246626,0.0820400491]),
                np.array([-0.101997532,0.281471103,0.281898797])
            )
        ]
        self.d_safe = 0.2
        self.k_avoid=1.0#10.0

        # self.monitored_segments = np.array([1,2,3,4,5,6])

        self.link_segments = []

        for i in range(self.chain.getNrOfSegments()):
            seg = self.chain.getSegment(i)
            joint = seg.getJoint()

            # Fixed joints have zero DOF
            if joint.getType() != 0:
                self.link_segments.append(i)



    # def build_chain_from_urdf(self, robot: URDF, base: str, tip: str):
    #     import PyKDL as kdl

    #     def xyzrpy_to_frame(xyz, rpy):
    #         return kdl.Frame(
    #             kdl.Rotation.RPY(rpy[0], rpy[1], rpy[2]),
    #             kdl.Vector(xyz[0], xyz[1], xyz[2])
    #         )

    #     def axis_to_vector(axis):
    #         if axis is None:
    #             return kdl.Vector(0.0, 0.0, 1.0)
    #         return kdl.Vector(axis[0], axis[1], axis[2])

    #     # Map child link → joint
    #     child_to_joint = {j.child: j for j in robot.joints}

    #     # Walk from tip to base
    #     joints_path = []
    #     cur = tip
    #     while cur != base:
    #         if cur not in child_to_joint:
    #             raise RuntimeError(f"No joint found connecting into {cur}")
    #         j = child_to_joint[cur]
    #         joints_path.append(j)
    #         cur = j.parent

    #     joints_path.reverse()

    #     chain = kdl.Chain()
    #     joint_names = []

    #     for j in joints_path:
    #         # --- origin transform (parent link → joint frame)
    #         xyz = [0.0, 0.0, 0.0]
    #         rpy = [0.0, 0.0, 0.0]
    #         if j.origin is not None:
    #             if j.origin.xyz is not None:
    #                 xyz = list(j.origin.xyz)
    #             if j.origin.rpy is not None:
    #                 rpy = list(j.origin.rpy)

    #         origin_frame = xyzrpy_to_frame(xyz, rpy)

    #         jtype = (j.type or "").lower()

    #         if jtype == "fixed":
    #             kdl_joint = kdl.Joint(j.name, kdl.Joint.Fixed)

    #         elif jtype in ["revolute", "continuous"]:
    #             axis_vec = axis_to_vector(j.axis)
    #             kdl_joint = kdl.Joint(
    #                 j.name,
    #                 kdl.Vector(0.0, 0.0, 0.0),
    #                 axis_vec,
    #                 kdl.Joint.RotAxis
    #             )
    #             joint_names.append(j.name)

    #         elif jtype == "prismatic":
    #             axis_vec = axis_to_vector(j.axis)
    #             kdl_joint = kdl.Joint(
    #                 j.name,
    #                 kdl.Vector(0.0, 0.0, 0.0),
    #                 axis_vec,
    #                 kdl.Joint.TransAxis
    #             )
    #             joint_names.append(j.name)

    #         else:
    #             raise RuntimeError(f"Unsupported joint type {j.type}")

    #         chain.addSegment(kdl.Segment(j.child, kdl_joint, origin_frame))

    #     return joint_names, chain

        



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
            if j.type == "fixed":
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
                        j.child,
                        PyKDL.Joint(j.name, PyKDL.Joint.Fixed),
                        origin_frame
                    )
                )
                continue

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


    def segment_segment_distance(self, A, B, C, D):
        u = B - A
        v = D - C
        w = A - C

        a = np.dot(u, u)
        b = np.dot(u, v)
        c = np.dot(v, v)
        d = np.dot(u, w)
        e = np.dot(v, w)

        denom = a*c - b*b

        if denom < 1e-8:
            s = 0.0
        else:
            s = (b*e - c*d) / denom

        t = (a*e - b*d) / denom if denom > 1e-8 else 0.0

        s = np.clip(s, 0.0, 1.0)
        t = np.clip(t, 0.0, 1.0)

        P = A + s * u
        Q = C + t * v

        diff = P - Q
        dist = np.linalg.norm(diff)

        if dist < 1e-8:
            normal = np.zeros(3)
        else:
            normal = diff / dist

        return dist, normal, P


    def joint_positions(self):
        positions = []

        # base position
        base_frame = PyKDL.Frame()
        self.fk.JntToCart(self.q, base_frame, 0)
        positions.append(np.array([base_frame.p[0], base_frame.p[1], base_frame.p[2]]))

        # moving segments
        for seg_idx in self.link_segments:
            frame = PyKDL.Frame()
            self.fk.JntToCart(self.q, frame, seg_idx + 1)
            positions.append(np.array([frame.p[0], frame.p[1], frame.p[2]]))

        return positions

    # def joint_positions(self):
    #     positions = []

    #     for i in self.monitored_segments:
    #         frame = PyKDL.Frame()
    #         self.fk.JntToCart(self.q, frame, i+1)
    #         positions.append(np.array([frame.p[0], frame.p[1], frame.p[2]]))

    #     return positions

    def jacobian_to_segment(self, segment_index):
        """
        Returns full 6xN Jacobian (numpy) up to given segment index.
        segment_index corresponds to self.chain.getSegment(segment_index)
        """

        jac = PyKDL.Jacobian(self.n)

        # KDL expects number of segments to include (index + 1)
        self.jac_solver.JntToJac(self.q, jac, segment_index + 1)

        return self.kdl_jacobian_to_numpy(jac)




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

    # def step(self):
    #     if self.target_frame is None:
    #         return

    #     current = PyKDL.Frame()
    #     self.fk.JntToCart(self.q, current)

    #     # position error in base frame
    #     dp = self.target_frame.p - current.p

    #     # orientation error: current -> target
    #     R_err = current.M.Inverse() * self.target_frame.M

    #     angle, axis = R_err.GetRotAngle()
    #     dr = axis * angle   # proper rotation vector

    #     dr_vec = np.array([dr[0], dr[1], dr[2]], dtype=float)

    #     # clamp angular error
    #     max_dr = 0.2
    #     dr_norm = np.linalg.norm(dr_vec)
    #     if dr_norm > max_dr:
    #         dr_vec *= max_dr / (dr_norm + 1e-12)

    #     # clamp position error too if needed
    #     dp_vec = np.array([dp[0], dp[1], dp[2]], dtype=float)
    #     max_dp = 0.05
    #     dp_norm = np.linalg.norm(dp_vec)
    #     if dp_norm > max_dp:
    #         dp_vec *= max_dp / (dp_norm + 1e-12)

    #     Kp_pos = 1.0
    #     Kp_rot = 1.0   # start closer to position gain

    #     err = np.hstack([
    #         Kp_pos * dp_vec,
    #         Kp_rot * dr_vec
    #     ])

    #     jac = PyKDL.Jacobian(self.n)
    #     self.jac_solver.JntToJac(self.q, jac)
    #     J = self.kdl_jacobian_to_numpy(jac)

    #     # damped least squares
    #     H = J @ J.T + (self.lmbd ** 2) * np.eye(6)
    #     J_pinv = J.T @ np.linalg.inv(H)

    #     dq_task = J_pinv @ err

    #     # optional joint velocity limit
    #     dq_max = 0.5  # rad/s
    #     dq_norm = np.linalg.norm(dq_task)
    #     if dq_norm > dq_max:
    #         dq_task *= dq_max / (dq_norm + 1e-12)

    #     dt = 1.0 / self.rate_hz
    #     for i in range(self.n):
    #         self.q[i] += float(self.gain * dq_task[i]) * dt

    #     js = JointState()
    #     js.header.stamp = self.get_clock().now().to_msg()
    #     js.name = self.joint_names
    #     js.position = [self.q[i] for i in range(self.n)]
    #     self.pub.publish(js)



    def step(self):
        if self.target_frame is None:
            return

        current = PyKDL.Frame()
        self.fk.JntToCart(self.q, current)
        # print("with_avoidance EE is: " + str(current.p))
        
        # Full 6D error (position + orientation)
        dp = self.target_frame.p - current.p

        #===================================================================
        # dR = current.M.Inverse() * self.target_frame.M
        # angle, axis = dR.GetRotAngle()
        # # print("axis: " + str(axis))
        # # print("angle: " + str(angle))
        # if abs(angle) < 1e-6:
        #     dr = PyKDL.Vector.Zero()
        # else:
        #     dr = PyKDL.Vector(axis[0]*angle, axis[1]*angle, axis[2]*angle)
        # R_err = current.M.Inverse() * self.target_frame.M
        R_err = self.target_frame.M * current.M.Inverse()

        coef = 1.0
        ex = coef * (R_err[2, 1] - R_err[1, 2])
        ey = coef * (R_err[0, 2] - R_err[2, 0])
        ez = coef * (R_err[1, 0] - R_err[0, 1])

        dr = PyKDL.Vector(ex, ey, ez)
        dr_vec = np.array([dr[0], dr[1], dr[2]], dtype=float)
        dr_norm = np.linalg.norm(dr_vec)
        max_dr = 0.3  # rad (start here)

        if dr_norm > max_dr:
            dr_vec = dr_vec * (max_dr / (dr_norm + 1e-12))
            dr = PyKDL.Vector(float(dr_vec[0]), float(dr_vec[1]), float(dr_vec[2]))
        #===================================================================
        # dr = axis * angle

        # dR = self.target_frame.M * current.M.Inverse()
        # angle, axis = dR.GetRotAngle()
        # dr = PyKDL.Vector(axis[0]*angle, axis[1]*angle, axis[2]*angle)

        # err = np.array([dp[0], dp[1], dp[2], dr[0], dr[1], dr[2]])
        Kp_pos = 1.0
        Kp_rot = 0.3   # <-- key change

        err = np.array([
            Kp_pos*dp[0], Kp_pos*dp[1], Kp_pos*dp[2],
            Kp_rot*dr[0], Kp_rot*dr[1], Kp_rot*dr[2]
        ])

        # Jacobian
        jac = PyKDL.Jacobian(self.n)
        self.jac_solver.JntToJac(self.q, jac)
        # J = np.array(jac.data)
        J = self.kdl_jacobian_to_numpy(jac)
        # print("Jacobian shape:", J.shape)

        #====================================================================
        J_pinv = J.T @ np.linalg.inv(J @ J.T + (self.lmbd**2) * np.eye(6))
        # J_pinv = J.T @ np.linalg.inv(J @ J.T + (self.lmbd**2) * np.eye(6))
        N = np.eye(self.n) - J_pinv @ J

        dq_task = J_pinv @ err
        dq_avoid = np.zeros(self.n)

        joint_positions = self.joint_positions()
        # print("joint_positions is: " + str(len(joint_positions)))


        d_min = 10000000000
        # for i in range(1, len(joint_positions)):
        for link_id in range(1, len(joint_positions)):

            A = joint_positions[link_id - 1]
            B = joint_positions[link_id]

            seg_A = 0 if link_id == 1 else self.link_segments[link_id - 2]
            seg_B = self.link_segments[link_id - 1]
            


            # for obstacle in self.obstacle_segments:

            for C, D in self.obstacle_segments:
                
                C_scaled = C / 1.0
                D_scaled = D / 1.0
                # print("C_scaled: " + str(C_scaled) + "  C: " + str(C))
                # print("D_scaled: " + str(D_scaled) + "  D: " + str(D))
                # print("A: " + str(A))
                # print("B: " + str(B))


                d, normal, P = self.segment_segment_distance(A, B, C_scaled, D_scaled)
                d_min = min(d, d_min)


                # print("d is: " + str(d))
                if d < self.d_safe:
                    # keep d in range to avoid grad explosions
                    d = max(d, 0.01)

                    # compute interpolation factor s again
                    s = np.linalg.norm(P - A) / (np.linalg.norm(B - A) + 1e-8)

                    # J_A = self.jacobian_to_segment(i-1)
                    # J_B = self.jacobian_to_segment(i)
                    if link_id == 1:
                        J_A = np.zeros((6, self.n))
                    else:
                        J_A = self.jacobian_to_segment(seg_A)

                    J_B = self.jacobian_to_segment(seg_B)

                    Jv_A = J_A[0:3, :]
                    Jv_B = J_B[0:3, :]

                    J_P = (1-s)*Jv_A + s*Jv_B



                    F = self.k_avoid * (self.d_safe - d)*normal
                    # print("k_avoid is: " + str(self.k_avoid))

                    dq_avoid += J_P.T @ F
        # print("d_min to robot is: " + str(d_min))

        #=====================================================

        if self.n>6:
            # if the robot has more than 6dofs then null space could be useful
            dq = dq_task  + N @ dq_avoid
        else:
            dq = dq_task

        # Integrate
        for i in range(self.n):
            self.q[i] += float(self.gain * dq[i]) * (1.0 / self.rate_hz)
            # Wrap to [-pi, pi] to prevent windup
            # self.q[i] = (self.q[i] + math.pi) % (2 * math.pi) - math.pi
            # self.q[i] = 0.1
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



