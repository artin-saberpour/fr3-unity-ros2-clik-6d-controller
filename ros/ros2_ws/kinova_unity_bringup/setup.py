
from setuptools import setup
import os
from glob import glob

package_name = 'kinova_unity_bringup'

setup(
    name=package_name,
    version='0.0.0',
    packages=[package_name],
    data_files=[
        ('share/ament_index/resource_index/packages',
            ['resource/' + package_name]),
        ('share/' + package_name, ['package.xml']),

        # 👇 THIS IS THE IMPORTANT PART
        (os.path.join('share', package_name, 'launch'), glob('launch/*.py')),
        (os.path.join('share', package_name, 'config'), glob('config/*.yaml')),
        (os.path.join('share', package_name, 'urdf'), glob('urdf/*.urdf')),
    ],
    install_requires=['setuptools'],
    zip_safe=True,
    maintainer='rtn',
    maintainer_email='saberpour@cs.uni-saarland.de',
    description='kinova Unity ROS2 bringup',
    license='Apache License 2.0',
    tests_require=['pytest'],
    entry_points={
    'console_scripts': [
        'ee_to_joint_inverse_kinematics = kinova_unity_bringup.ee_to_joint_inverse_kinematics:main',
        'ee_to_joint_inverse_kinematics_obstacle_avoidance = kinova_unity_bringup.ee_to_joint_inverse_kinematics_obstacle_avoidance:main',
        ],
    },
)
