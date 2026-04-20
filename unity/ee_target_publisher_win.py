


import socket
import json


class UnitySocketClient:
    def __init__(self, host="localhost", port=9998):
        self.host = host
        self.port = port
        self.sock = None
        # self.connect()

        self.ee_target = [0.6, 0.4, 0.02]
        self.ee_orientation = [0.0, 0.0, 0.0, 1.0]
        self.segments = []

    def connect(self):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.connect((self.host, self.port))
        print("Socket to ros2 is stablished on the windows side")

    def set_ee_target(self, x, y, z):
        self.ee_target = [float(x), float(y), float(z)]

    def set_ee_orientation(self, x, y, z, w):
        self.ee_orientation = [float(x), float(y), float(z), float(w)]

    def add_segment(self, p1, p2):
        """
        p1 and p2 should be iterables of length 3
        Example: add_segment([0,0,0], [1,1,1])
        """
        self.segments.append([list(map(float, p1)), list(map(float, p2))])

    def clear_segments(self):
        self.segments.clear()

    def build_message(self):
        return {
            "ee_target": self.ee_target,
            "ee_orientation": self.ee_orientation,
            "obstacles": self.segments
        }

    def send(self):
        # if not self.sock:
        #     raise RuntimeError("ros2 socket on windows side not connected")
        # msg = self.build_message()
        # try:
        #     self.sock.sendall(json.dumps(msg).encode())
        # except Exception as e:
        #     print("Connection error:", e)
        
        # print("Sent:", msg)


        msg = self.build_message()
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
                s.connect((self.host, self.port))
                s.sendall(json.dumps(msg).encode())
            print("Sent:", msg)
        except Exception as e:
            print("Connection error:", e)




# publisher_win = UnitySocketClient()


# while True:
#     cmd = input("> ").strip().split()

#     if not cmd:
#         continue

#     if cmd[0] == "ee":
#         ee_target = list(map(float, cmd[1:4]))
#         publisher_win.set_ee_target(ee_target[0], ee_target[1], ee_target[2])

#     elif cmd[0] == "seg":
#         p1 = list(map(float, cmd[1:4]))
#         p2 = list(map(float, cmd[4:7]))
#         segments.append([p1, p2])
#         print("Segment added")

#     elif cmd[0] == "send":
#         # msg = {
#         #     "ee_target": ee_target,
#         #     "ee_orientation": ee_orientation,
#         #     "obstacles": segments
#         # }
#         publisher_win.build_message()
#         publisher_win.send()
#         # 
#         # print("Sent", msg)
#         # send_message(msg)
#         # send()

#     elif cmd[0] == "clear":
#         segments.clear()
#         print("Segments cleared")




















































# import socket
# import json

# HOST = "localhost"
# PORT = 9999

# def send_message(msg_dict):
#     try:
#         s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
#         s.connect((HOST, PORT))
#         s.sendall(json.dumps(msg_dict).encode())
#         s.close()
#     except Exception as e:
#         print("Connection error:", e)


# print("Commands:")
# print("  ee x y z")
# print("  seg x1 y1 z1 x2 y2 z2")
# print("  send")

# ee_target = [0.3, 0.0, 0.5]
# ee_orientation = [0.0, 0.0, 0.0, 1.0]
# segments = []

# while True:
#     cmd = input("> ").strip().split()

#     if not cmd:
#         continue

#     if cmd[0] == "ee":
#         ee_target = list(map(float, cmd[1:4]))

#     elif cmd[0] == "seg":
#         p1 = list(map(float, cmd[1:4]))
#         p2 = list(map(float, cmd[4:7]))
#         segments.append([p1, p2])
#         print("Segment added")

#     elif cmd[0] == "send":
#         msg = {
#             "ee_target": ee_target,
#             "ee_orientation": ee_orientation,
#             "obstacles": segments
#         }
#         send_message(msg)
#         print("Sent", msg)

#     elif cmd[0] == "clear":
#         segments.clear()
#         print("Segments cleared")




