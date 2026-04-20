from launch import LaunchDescription
from launch_ros.actions import Node
from ament_index_python.packages import get_package_share_directory
import os

def generate_launch_description():
    pkg = get_package_share_directory("kinova_unity_bringup")
    # Adjust filenames if needed
    urdf_path = os.path.join(pkg, "urdf", "kinova.urdf")
    controllers_path = os.path.join(pkg, "config", "controllers.yaml")

    robot_description = {
        "robot_description": open(urdf_path).read()
    }

    robot_state_publisher = Node(
        package="robot_state_publisher",
        executable="robot_state_publisher",
        parameters=[robot_description],
        output="screen"
    )

    control_node = Node(
        package="controller_manager",
        executable="ros2_control_node",
        parameters=[robot_description, controllers_path],
        output="screen"
    )

    joint_state_broadcaster_spawner = Node(
        package="controller_manager",
        executable="spawner",
        arguments=[
            "joint_state_broadcaster",
            "--controller-manager", "/controller_manager"
        ],
        output="screen",
    )

    arm_controller_spawner = Node(
        package="controller_manager",
        executable="spawner",
        arguments=[
            "arm_controller",
            "--controller-manager", "/controller_manager"
        ],
        output="screen",
    )

    rosTcpEndpoint = Node(
        package="ros_tcp_endpoint",
        executable="default_server_endpoint",
        output="screen",
    )

    return LaunchDescription([
        robot_state_publisher,
        control_node,
        joint_state_broadcaster_spawner,
        arm_controller_spawner,
        rosTcpEndpoint
    ])

