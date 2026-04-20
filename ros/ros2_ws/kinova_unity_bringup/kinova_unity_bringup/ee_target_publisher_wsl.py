#!/usr/bin/env python3

import threading
import socket
import json
import rclpy
from rclpy.node import Node

from geometry_msgs.msg import PoseStamped, PoseArray, Pose
import numpy as np


class UnityBridge(Node):

    def __init__(self):
        super().__init__('unity_bridge')

        # EE publisher
        self.ee_pub = self.create_publisher(
            PoseStamped,
            '/ee_target_pose',
            10
        )

        # Obstacles publisher
        self.obs_pub = self.create_publisher(
            PoseArray,
            '/obstacles',
            10
        )

        self.ee_msg = PoseStamped()
        self.ee_msg.header.frame_id = "base"

        self.obs_msg = PoseArray()
        self.obs_msg.header.frame_id = "base"

        threading.Thread(target=self.socket_server, daemon=True).start()
        self.get_logger().info("Unity bridge ready on port 9999")

    # ------------------------------
    # UNITY → ROS transform
    def unity_to_ros(self, p_u):
        x_u, y_u, z_u = p_u
        return np.array([z_u, -x_u, y_u])

    # ------------------------------
    def socket_server(self):

        server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server.bind(("0.0.0.0", 9998))
        server.listen(5)

        while True:
            conn, _ = server.accept()
            data = conn.recv(4096).decode().strip()
            conn.close()

            if not data:
                continue

            try:
                msg = json.loads(data)
                self.handle_message(msg)
            except Exception as e:
                self.get_logger().error(f"Invalid JSON: {e}")

    # ------------------------------
    def handle_message(self, msg):

        now = self.get_clock().now().to_msg()

        # ---------- EE TARGET ----------
        if "ee_target" in msg:
            p_ros = self.unity_to_ros(msg["ee_target"])

            self.ee_msg.header.stamp = now
            self.ee_msg.pose.position.x = float(p_ros[0])
            self.ee_msg.pose.position.y = float(p_ros[1])
            self.ee_msg.pose.position.z = float(p_ros[2])

        if "ee_orientation" in msg:
            q = msg["ee_orientation"]
            self.ee_msg.pose.orientation.x = q[0]
            self.ee_msg.pose.orientation.y = q[1]
            self.ee_msg.pose.orientation.z = q[2]
            self.ee_msg.pose.orientation.w = q[3]

        self.ee_pub.publish(self.ee_msg)

        # ---------- OBSTACLES ----------
        if "obstacles" in msg:

            self.obs_msg.header.stamp = now
            self.obs_msg.poses.clear()

            for p_u in msg["obstacles"]:
                p_ros = self.unity_to_ros(p_u)

                pose = Pose()
                pose.position.x = float(p_ros[0])
                pose.position.y = float(p_ros[1])
                pose.position.z = float(p_ros[2])
                pose.orientation.w = 1.0

                self.obs_msg.poses.append(pose)

            self.obs_pub.publish(self.obs_msg)

        self.get_logger().info(
            f"EE: {self.ee_msg.pose.position.x:.2f}, "
            f"{self.ee_msg.pose.position.y:.2f}, "
            f"{self.ee_msg.pose.position.z:.2f} | "
            f"Obs: {len(self.obs_msg.poses)}"
        )


def main():
    rclpy.init()
    node = UnityBridge()
    rclpy.spin(node)
    rclpy.shutdown()


if __name__ == '__main__':
    main()



