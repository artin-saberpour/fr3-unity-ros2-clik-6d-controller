# #!/usr/bin/env python3

# import rclpy
# from rclpy.node import Node
# from sensor_msgs.msg import JointState
# import math
# import time


# class EEToJointPlaceholder(Node):
#     def __init__(self):
#         super().__init__('ee_to_joint_placeholder')

#         self.pub = self.create_publisher(JointState, '/joint_states', 10)

#         self.timer = self.create_timer(0.02, self.timer_cb)  # 50 Hz
#         self.t = 0.0

#         self.joint_names = [
#             'fr3_joint1',
#             'fr3_joint2',
#             'fr3_joint3',
#             'fr3_joint4',
#             'fr3_joint5',
#             'fr3_joint6',
#             'fr3_joint7',
#         ]

#         self.joint_axes = [
#             PyKDL.Vector(0, 0, 1),  # joint1
#             PyKDL.Vector(0, 0, 1),  # joint2
#             PyKDL.Vector(0, 0, 1),  # joint3
#             PyKDL.Vector(0, 0, 1),  # joint4
#             PyKDL.Vector(0, 0, 1),  # joint5
#             PyKDL.Vector(0, 0, 1),  # joint6
#             PyKDL.Vector(0, 0, 1),  # joint7
#         ]

#         self.link_offsets = [
#             PyKDL.Frame(PyKDL.Rotation.Identity(), PyKDL.Vector(0, 0, 0.333)),
#             PyKDL.Frame(PyKDL.Rotation.RPY(-1.5708, 0, 0), PyKDL.Vector(0, 0, 0)),
#             PyKDL.Frame(PyKDL.Rotation.RPY(1.5708, 0, 0), PyKDL.Vector(0, -0.316, 0)),
#             PyKDL.Frame(PyKDL.Rotation.RPY(1.5708, 0, 0), PyKDL.Vector(0.0825, 0, 0)),
#             PyKDL.Frame(PyKDL.Rotation.RPY(-1.5708, 0, 0), PyKDL.Vector(-0.0825, 0.384, 0)),
#             PyKDL.Frame(PyKDL.Rotation.RPY(1.5708, 0, 0), PyKDL.Vector(0, 0, 0)),
#             PyKDL.Frame(PyKDL.Rotation.RPY(1.5708, 0, 0), PyKDL.Vector(0.088, 0, 0)),
#         ]


#         self.get_logger().info('EE → Joint placeholder running')

#     def timer_cb(self):
#         msg = JointState()
#         msg.header.stamp = self.get_clock().now().to_msg()
#         msg.name = self.joint_names

#         # simple motion so Unity + RSP stay alive
#         msg.position = [
#             -0.6 * math.sin(self.t + 1.0),
#             -0.6 * math.sin(self.t + 1.0),
#             -0.6 * math.sin(self.t + 1.0),
#             # -0.2 * math.sin(self.t + 0.5),
#             # -0.2 * math.sin(self.t + 1.0),
#             0.0,
#             0.0,
#             0.0,
#             0.0,#-0.6 * math.sin(self.t + 1.0),
#         ]

#         self.pub.publish(msg)
#         self.t += 0.02


# def main():
#     rclpy.init()
#     node = EEToJointPlaceholder()
#     rclpy.spin(node)
#     node.destroy_node()
#     rclpy.shutdown()


# if __name__ == '__main__':
#     main()



# #!/usr/bin/env python3

# import rclpy
# from rclpy.node import Node
# from sensor_msgs.msg import JointState
# import math
# import time


# class EEToJointPlaceholder(Node):
#     def __init__(self):
#         super().__init__('ee_to_joint_placeholder')

#         self.pub = self.create_publisher(JointState, '/joint_states', 10)

#         self.timer = self.create_timer(0.02, self.timer_cb)  # 50 Hz
#         self.t = 0.0

#         self.joint_names = [
#             'fr3_joint1',
#             'fr3_joint2',
#             'fr3_joint3',
#             'fr3_joint4',
#             'fr3_joint5',
#             'fr3_joint6',
#             'fr3_joint7',
#         ]

#         self.JOINT_AXES = [
#             PyKDL.Vector(0, 0, 1),  # joint1
#             PyKDL.Vector(0, 1, 0),  # joint2
#             PyKDL.Vector(0, 0, 1),  # joint3
#             PyKDL.Vector(0, 1, 0),  # joint4
#             PyKDL.Vector(0, 0, 1),  # joint5
#             PyKDL.Vector(0, 1, 0),  # joint6
#             PyKDL.Vector(0, 0, 1),  # joint7
#         ]


#         self.get_logger().info('EE → Joint placeholder running')

#     def timer_cb(self):
#         msg = JointState()
#         msg.header.stamp = self.get_clock().now().to_msg()
#         msg.name = self.joint_names

#         # simple motion so Unity + RSP stay alive
#         msg.position = [
#             -0.6 * math.sin(self.t + 1.0),
#             -0.6 * math.sin(self.t + 1.0),
#             -0.6 * math.sin(self.t + 1.0),
#             # -0.2 * math.sin(self.t + 0.5),
#             # -0.2 * math.sin(self.t + 1.0),
#             0.0,
#             0.0,
#             0.0,
#             0.0,#-0.6 * math.sin(self.t + 1.0),
#         ]

#         self.pub.publish(msg)
#         self.t += 0.02


# def main():
#     rclpy.init()
#     node = EEToJointPlaceholder()
#     rclpy.spin(node)
#     node.destroy_node()
#     rclpy.shutdown()


# if __name__ == '__main__':
#     main()






























































#!/usr/bin/env python3

import rclpy
from rclpy.node import Node

import PyKDL
import numpy as np

from geometry_msgs.msg import PoseStamped
from sensor_msgs.msg import JointState

# ===============================
# Robot definition (LOCKED)
# ===============================

JOINT_NAMES = [
    "fr3_joint1",
    "fr3_joint2",
    "fr3_joint3",
    "fr3_joint4",
    "fr3_joint5",
    "fr3_joint6",
    "fr3_joint7",
]

LINK_OFFSETS = [
    PyKDL.Frame(PyKDL.Rotation.Identity(), PyKDL.Vector(0, 0, 0.333)),
    PyKDL.Frame(PyKDL.Rotation.RPY(-1.5708, 0, 0), PyKDL.Vector(0, 0, 0)),
    PyKDL.Frame(PyKDL.Rotation.RPY(1.5708, 0, 0), PyKDL.Vector(0, -0.316, 0)),
    PyKDL.Frame(PyKDL.Rotation.RPY(1.5708, 0, 0), PyKDL.Vector(0.0825, 0, 0)),
    PyKDL.Frame(PyKDL.Rotation.RPY(-1.5708, 0, 0), PyKDL.Vector(-0.0825, 0.384, 0)),
    PyKDL.Frame(PyKDL.Rotation.RPY(1.5708, 0, 0), PyKDL.Vector(0, 0, 0)),
    PyKDL.Frame(PyKDL.Rotation.RPY(1.5708, 0, 0), PyKDL.Vector(0.088, 0, 0)),
]


# ===============================
# IK Node
# ===============================

class EeToJointIK(Node):
    def __init__(self):
        super().__init__("ee_to_joint_ik")

        # Build KDL chain
        self.chain = PyKDL.Chain()
        for frame in LINK_OFFSETS:
            joint = PyKDL.Joint(PyKDL.Joint.RotZ)
            segment = PyKDL.Segment(joint, frame)
            self.chain.addSegment(segment)

        self.fk = PyKDL.ChainFkSolverPos_recursive(self.chain)
        self.jac_solver = PyKDL.ChainJntToJacSolver(self.chain)

        self.q = PyKDL.JntArray(self.chain.getNrOfJoints())

        self.sub = self.create_subscription(
            PoseStamped,
            "/ee_target_pose",
            self.ee_callback,
            10
        )

        self.pub = self.create_publisher(JointState, "/joint_states", 10)

        self.timer = self.create_timer(0.01, self.step)

        self.target_frame = None

        self.get_logger().info("KDL IK node started")

    def ee_callback(self, msg):
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
            )
        )

    def step(self):
        if self.target_frame is None:
            return

        current = PyKDL.Frame()
        self.fk.JntToCart(self.q, current)

        # Position error only (orientation later)
        e = self.target_frame.p - current.p
        err = np.array([e[0], e[1], e[2]])

        if np.linalg.norm(err) < 1e-4:
            return

        jac = PyKDL.Jacobian(self.chain.getNrOfJoints())
        self.jac_solver.JntToJac(self.q, jac)

        J = np.array(jac.data[:3, :])  # position Jacobian
        dq = np.linalg.pinv(J) @ err

        for i in range(self.q.rows()):
            self.q[i] += dq[i] * 0.5

        msg = JointState()
        msg.name = JOINT_NAMES
        msg.position = [self.q[i] for i in range(self.q.rows())]
        self.pub.publish(msg)

# ===============================
# Main
# ===============================

def main():
    rclpy.init()
    node = EeToJointIK()
    rclpy.spin(node)

if __name__ == "__main__":
    main()
