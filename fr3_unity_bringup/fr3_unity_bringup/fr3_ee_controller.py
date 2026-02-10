import rclpy
from rclpy.node import Node
import numpy as np

from sensor_msgs.msg import JointState
from geometry_msgs.msg import PoseStamped

class Fr3EEController(Node):
    def __init__(self):
        super().__init__('fr3_ee_controller')

        self.joint_names = [
            'fr3_joint1', 'fr3_joint2', 'fr3_joint3',
            'fr3_joint4', 'fr3_joint5', 'fr3_joint6',
            'fr3_joint7'
        ]

        self.q = np.zeros(7)

        self.sub_ee = self.create_subscription(
            PoseStamped,
            '/fr3/ee_target',
            self.ee_callback,
            10
        )

        self.pub_js = self.create_publisher(
            JointState,
            '/joint_states',
            10
        )

        self.timer = self.create_timer(0.01, self.control_loop)

        self.x_des = None

    def ee_callback(self, msg):
        self.x_des = msg

    def control_loop(self):
        if self.x_des is None:
            return

        # TODO:
        # 1. FK(q)
        # 2. Compute pose error
        # 3. Compute Jacobian
        # 4. Solve dq
        # 5. Integrate q

        js = JointState()
        js.name = self.joint_names
        js.position = self.q.tolist()
        js.header.stamp = self.get_clock().now().to_msg()

        self.pub_js.publish(js)

def main():
    rclpy.init()
    node = Fr3EEController()
    rclpy.spin(node)
    rclpy.shutdown()
