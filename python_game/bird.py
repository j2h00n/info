"""BirdController.cs 대체: 중력/점프/좌우이동 물리 + 점수 계산 (Panda3D 비의존 순수 로직)."""
import math

from . import prefs


class Bird:
    FORWARD_SPEED = 15.0
    FLAP_FORCE = 7.0
    SIDE_SPEED = 10.0
    TILT_ANGLE = 35.0
    TILT_SPEED = 7.0
    GRAVITY = -9.81  # ProjectSettings/DynamicsManager.asset 확인값, Unity 기본 그대로

    def __init__(self):
        self.position = [0.0, 0.0, 0.0]  # x, y(높이), z(전진)
        self.velocity = [0.0, 0.0, 0.0]
        self.roll = 0.0  # 좌우 기울기(도)

        self.is_game_started = False
        self.is_game_over = False
        self.start_time = 0.0
        self._elapsed = 0.0

        self.use_motion_control = prefs.get_int("MotionMode", 0) == 1

        self.best_score = prefs.get_int("BestScore", 0)
        self.best_wall = prefs.get_int("BestWall", 0)
        self.score = 0
        self.passed_walls = 0

    def start_game(self):
        self.is_game_started = True
        self.start_time = self._elapsed

    def restart(self):
        self.position = [0.0, 0.0, 0.0]
        self.velocity = [0.0, 0.0, 0.0]
        self.roll = 0.0
        self.is_game_started = False
        self.is_game_over = False
        self.score = 0
        self.passed_walls = 0

    def jump(self):
        if not self.is_game_started or self.is_game_over:
            return False
        self.velocity[1] = self.FLAP_FORCE
        return True

    def update(self, dt, move_x):
        self._elapsed += dt
        if not self.is_game_started or self.is_game_over:
            return

        move_x = max(-1.0, min(1.0, move_x))

        # FixedUpdate 대응: 물리 속도 갱신
        self.velocity[1] += self.GRAVITY * dt
        self.velocity[0] = move_x * self.SIDE_SPEED
        self.velocity[2] = self.FORWARD_SPEED

        self.position[0] += self.velocity[0] * dt
        self.position[1] += self.velocity[1] * dt
        self.position[2] += self.velocity[2] * dt

        # Update 대응: 기울기 보간 + 점수 계산
        target_roll = -move_x * self.TILT_ANGLE
        self.roll += (target_roll - self.roll) * min(1.0, dt * self.TILT_SPEED)

        self.score = max(0, int(self.position[2]))
        self.passed_walls = max(0, math.floor((self.position[2] - 5.0) / 15.0))

        if self.score > self.best_score:
            self.best_score = self.score
            prefs.set_int("BestScore", self.best_score)
        if self.passed_walls > self.best_wall:
            self.best_wall = self.passed_walls
            prefs.set_int("BestWall", self.best_wall)

    def on_collision(self, is_wall_cube):
        if not self.is_game_started:
            return
        if self._elapsed - self.start_time < 1.0:
            return
        if is_wall_cube:
            self.is_game_over = True
