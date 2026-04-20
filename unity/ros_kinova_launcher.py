import subprocess
import signal
import sys
import time

# ---------------- CONFIG ----------------

WSL_DISTRO = "Ubuntu"   # change only if you renamed it
ROS_SETUP = "source /opt/ros/humble/setup.bash && source ~/ros2_ws/install/setup.bash"

URDF_PATH = "/home/rtn/franka_urdf/franka_fr3.urdf"

# ----------------------------------------

processes = []

def start_wsl_process(cmd, name):
    print(f"[INFO] Starting {name}...")
    p = subprocess.Popen(
        [
            "wsl",
            "-d", WSL_DISTRO,
            "bash",
            "-lc",
            cmd
        ],
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP
    )
    processes.append((name, p))
    time.sleep(1)

def shutdown(signum=None, frame=None):
    print("\n[INFO] Shutting down ROS processes...")
    for name, p in processes:
        print(f"  - Stopping {name}")
        try:
            p.send_signal(signal.CTRL_BREAK_EVENT)
        except Exception:
            pass
    time.sleep(1)
    sys.exit(0)

signal.signal(signal.SIGINT, shutdown)
signal.signal(signal.SIGTERM, shutdown)

# ---------------- START ROS ----------------

# 1️ robot_state_publisher
# start_wsl_process(
#     f"{ROS_SETUP} && ros2 run robot_state_publisher robot_state_publisher {URDF_PATH}",
#     "robot_state_publisher"
# )

# # 2️ joint_state_publisher_gui
# start_wsl_process(
#     f"{ROS_SETUP} && ros2 run joint_state_publisher_gui joint_state_publisher_gui",
#     "joint_state_publisher_gui"
# )

# # 2️ joint_state_publisher_place_holder
# start_wsl_process(
#     f"{ROS_SETUP} && ros2 run fr3_unity_bringup ee_to_joint_placeholder",
#     "ee_to_joint_placeholder"
# )

# 2️ joint_state_publisher_place_holder
start_wsl_process(
    f"{ROS_SETUP} && ros2 run kinova_unity_bringup ee_to_joint_inverse_kinematics_obstacle_avoidance",
    "ee_to_joint_inverse_kinematics_obstacle_avoidance"
)


# 3️ tcp
start_wsl_process(
    f"{ROS_SETUP} && ros2 run ros_tcp_endpoint default_server_endpoint",
    "default_server_endpoint"
)

# # 3️ rosbridge
# start_wsl_process(
#     f"{ROS_SETUP} && ros2 launch rosbridge_server rosbridge_websocket_launch.xml",
#     "rosbridge"
# )


# start_wsl_process(
#     f"{ROS_SETUP} && ros2 run ros_tcp_endpoint default_server_endpoint",
#     "default_server_endpoint"
# )



start_wsl_process(
    f'{ROS_SETUP} && cd ~/ros2_ws/src/kinova_unity_bringup/kinova_unity_bringup && python3 ee_target_publisher_wsl.py',
    "ee_target_publisher_wsl"
)




start_wsl_process(
    f"""{ROS_SETUP} && ros2 topic pub -1 /ee_target_pose geometry_msgs/msg/PoseStamped "
header:
  frame_id: 'world'
pose:
  position:
    x: 0.4
    y: -0.40
    z: 0.243
  orientation:
    x: 0.0
    y: 0.0
    z: 0.0
    w: 1.0
" """,
    "publish_pose"
)

# 0.548351367813489, 0.262286534830696, -0.043214399999999986
# 0.49999997  0.50000003 -0.50000003 -0.50000003
print("\n[INFO] ROS backend is running.")
print("[INFO] Start Unity and press ▶ Play.")
print("[INFO] Press Ctrl+C here to shut everything down.")

# Keep script alive
while True:
    time.sleep(1)
