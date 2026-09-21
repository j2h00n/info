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
    SIDE_DAMPING = 0.1875  # 파닥임으로 생긴 좌우속도가 시간지나며 줄어드는 비율(관성 완화)

    def __init__(self, easy=False):
        # 실험 모드(easy=True): 로직/공식은 동일, 체감 난이도만 낮춤(전진/중력 완화)
        self.easy = easy
        self.forward_speed = self.FORWARD_SPEED * (0.6 if easy else 1.0)
        self.gravity = self.GRAVITY * (0.6 if easy else 1.0)
        self.flap_force = self.FLAP_FORCE * (0.75 if easy else 1.0)

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
        """파닥임: 좌우입력이 만든 현재 각도(roll)의 수직(날개) 방향으로 추력을 줌.
        예를 들어 롤이 오른쪽으로 기울어져 있으면, 위로만이 아니라 왼쪽 위 대각선으로 뜬다."""
        if not self.is_game_started or self.is_game_over:
            return False
        roll_rad = math.radians(self.roll)
        dir_x, dir_y = math.sin(roll_rad), math.cos(roll_rad)

        # 기존 Unity 코드가 점프 직전 y속도를 0으로 리셋하던 것과 같은 취지로,
        # 이 방향(날개축) 성분만 지우고 그 방향으로 새로 추력을 줌
        along = self.velocity[0] * dir_x + self.velocity[1] * dir_y
        self.velocity[0] -= along * dir_x
        self.velocity[1] -= along * dir_y
        self.velocity[0] += dir_x * self.flap_force
        self.velocity[1] += dir_y * self.flap_force
        return True

    def update(self, dt, move_x):
        self._elapsed += dt
        if not self.is_game_started or self.is_game_over:
            return

        move_x = max(-1.0, min(1.0, move_x))

        # FixedUpdate 대응: 물리 속도 갱신 (좌우입력은 이제 위치가 아니라 각도만 조절,
        # 실제 좌우/상하 이동은 jump()가 그 각도 방향으로 주는 추력 + 중력으로만 발생)
        self.velocity[1] += self.gravity * dt
        self.velocity[2] = self.forward_speed
        self.velocity[0] *= max(0.0, 1.0 - self.SIDE_DAMPING * dt)

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
