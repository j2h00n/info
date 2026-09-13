"""MotionController.cs 대체: UDP(5052)로 head 위치 받아 좌우 이동/점프 트리거 계산."""
import socket
import threading


class MotionController:
    def __init__(self, on_jump=None, port=5052, learning_rate=1.5):
        self.head_x = 0.5
        self.head_y = 0.5
        self._baseline_x = 0.5
        self._baseline_y = 0.5
        self._last_head_y = 0.5
        self._jump_threshold = 0.02
        self.learning_rate = learning_rate
        self._on_jump = on_jump
        self._port = port
        self._running = False
        self._sock = None
        self._thread = None

    def start(self):
        self._running = True
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.bind(("0.0.0.0", self._port))
        self._thread = threading.Thread(target=self._receive_loop, daemon=True)
        self._thread.start()

    def _receive_loop(self):
        while self._running:
            try:
                data, _ = self._sock.recvfrom(1024)
                text = data.decode("utf-8")
                x_str, y_str = text.split(",")
                self.head_x = float(x_str)
                self.head_y = float(y_str)
            except Exception:
                if not self._running:
                    break

    def update(self, dt):
        """매 프레임 호출: baseline 보정 + 점프 감지."""
        self._baseline_x += (self.head_x - self._baseline_x) * min(1.0, dt * self.learning_rate)
        self._baseline_y += (self.head_y - self._baseline_y) * min(1.0, dt * self.learning_rate)

        head_velocity_y = self.head_y - self._last_head_y
        self._last_head_y = self.head_y

        if head_velocity_y > self._jump_threshold:
            if self._on_jump:
                self._on_jump()
            self._jump_threshold += (head_velocity_y * 0.7 - self._jump_threshold) * 0.1
            self._jump_threshold = max(0.015, self._jump_threshold)

    def get_motion_move_x(self):
        move_x = (self.head_x - self._baseline_x) * -8.0
        return max(-1.0, min(1.0, move_x))

    def stop(self):
        self._running = False
        if self._sock:
            try:
                self._sock.close()
            except Exception:
                pass
