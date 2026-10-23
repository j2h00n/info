"""BirdController.cs 대체: 중력/점프/좌우이동 물리 + 점수 계산 (Panda3D 비의존 순수 로직)."""
import math

from . import prefs


class Bird:
    # ===== [튜닝 상수] 게임 느낌을 바꾸고 싶으면 여기 숫자를 수정 =====
    FORWARD_SPEED = 15.0   # 전진 속도(Z축, 단위/초). 클수록 게임이 빨라짐
    FLAP_FORCE = 7.0       # 파닥임 1회당 주는 추력(속도 단위). 클수록 높이 뜸
    SIDE_SPEED = 10.0      # (현재 물리 로직에서 직접 쓰이진 않음) 원본 Unity의 좌우 속도값
    TILT_ANGLE = 35.0      # 좌우 입력 최대일 때 몸이 기우는 각도(도)
    TILT_SPEED = 7.0       # 기울기 보간 속도. 클수록 목표 각도에 빨리 도달
    GRAVITY = -9.81  # ProjectSettings/DynamicsManager.asset 확인값, Unity 기본 그대로
    SIDE_DAMPING = 0.1875  # 파닥임으로 생긴 좌우속도가 시간지나며 줄어드는 비율(관성 완화)

    def __init__(self, easy=False):
        # 실험 모드(easy=True): 로직/공식은 동일, 체감 난이도만 낮춤(전진/중력 완화)
        self.easy = easy
        # easy일 때 곱해지는 배율(0.6, 0.75)을 바꾸면 쉬운 모드 난이도가 달라짐
        self.forward_speed = self.FORWARD_SPEED * (0.6 if easy else 1.0)
        self.gravity = self.GRAVITY * (0.6 if easy else 1.0)
        self.flap_force = self.FLAP_FORCE * (0.75 if easy else 1.0)

        # 위치/속도는 [x, y, z] 리스트. x=좌우, y=높이, z=전진 방향
        self.position = [0.0, 0.0, 0.0]  # x, y(높이), z(전진)
        self.velocity = [0.0, 0.0, 0.0]
        self.roll = 0.0  # 좌우 기울기(도)

        # 게임 상태 플래그 (main.py가 이 값을 보고 UI/입력 처리를 분기함)
        self.is_game_started = False
        self.is_game_over = False
        self.start_time = 0.0   # 게임 시작 시각(_elapsed 기준) - 시작 직후 충돌 무시용
        self._elapsed = 0.0     # 프로그램 시작 후 누적 시간(초)

        # prefs.py(save.json)에 저장된 값 불러오기: 모션 모드 on/off, 최고 기록
        self.use_motion_control = prefs.get_int("MotionMode", 0) == 1

        self.best_score = prefs.get_int("BestScore", 0)
        self.best_wall = prefs.get_int("BestWall", 0)
        self.score = 0          # 현재 점수 = 전진 거리(정수)
        self.passed_walls = 0   # 통과한 벽(기둥 줄) 개수

    def start_game(self):
        """Space 등으로 게임 시작. start_time을 기록해 시작 직후 1초는 충돌 무시."""
        self.is_game_started = True
        self.start_time = self._elapsed

    def restart(self):
        """게임오버 후 재시작: 위치/속도/점수 초기화 (최고기록 best_*는 유지)."""
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
        # 시작 전/게임오버 상태에서는 점프 무시 (False 반환 -> main.py가 효과음 안 재생)
        if not self.is_game_started or self.is_game_over:
            return False
        roll_rad = math.radians(self.roll)
        # (dir_x, dir_y) = 추력 방향 단위벡터. roll=0이면 (0,1)=수직 위쪽
        dir_x, dir_y = math.sin(roll_rad), math.cos(roll_rad)

        # 기존 Unity 코드가 점프 직전 y속도를 0으로 리셋하던 것과 같은 취지로,
        # 이 방향(날개축) 성분만 지우고 그 방향으로 새로 추력을 줌
        # (내적으로 현재 속도의 추력방향 성분 'along'을 구해 제거 -> 연타해도 속도가 무한히 쌓이지 않음)
        along = self.velocity[0] * dir_x + self.velocity[1] * dir_y
        self.velocity[0] -= along * dir_x
        self.velocity[1] -= along * dir_y
        self.velocity[0] += dir_x * self.flap_force
        self.velocity[1] += dir_y * self.flap_force
        return True

    def update(self, dt, move_x):
        """매 프레임 호출. dt=지난 프레임 시간(초), move_x=-1~1 좌우 입력(키보드/모션)."""
        self._elapsed += dt
        if not self.is_game_started or self.is_game_over:
            return

        # 입력값을 -1~1 범위로 제한
        move_x = max(-1.0, min(1.0, move_x))

        # FixedUpdate 대응: 물리 속도 갱신 (좌우입력은 이제 위치가 아니라 각도만 조절,
        # 실제 좌우/상하 이동은 jump()가 그 각도 방향으로 주는 추력 + 중력으로만 발생)
        self.velocity[1] += self.gravity * dt                  # 중력: 매 프레임 아래로 가속
        self.velocity[2] = self.forward_speed                  # 전진은 항상 일정 속도
        self.velocity[0] *= max(0.0, 1.0 - self.SIDE_DAMPING * dt)  # 좌우 속도 서서히 감쇠

        # 위치 적분: 위치 += 속도 * 시간
        self.position[0] += self.velocity[0] * dt
        self.position[1] += self.velocity[1] * dt
        self.position[2] += self.velocity[2] * dt

        # Update 대응: 기울기 보간 + 점수 계산
        # 입력 방향과 roll 부호가 반대(-)인 점 주의 -> python_game/CONVENTIONS.md 참고
        target_roll = -move_x * self.TILT_ANGLE
        # 현재 roll을 목표 roll로 부드럽게 접근시킴 (지수 보간)
        self.roll += (target_roll - self.roll) * min(1.0, dt * self.TILT_SPEED)

        # 점수 = 전진 거리. 통과 벽 수 = (z-5)/15 (첫 벽 z=20, 벽 간격 15 기준 -> wall.py와 맞춰야 함)
        self.score = max(0, int(self.position[2]))
        self.passed_walls = max(0, math.floor((self.position[2] - 5.0) / 15.0))

        # 최고 기록 갱신 시 prefs에 반영 (디스크 저장은 prefs.save() 호출 시점에 됨)
        if self.score > self.best_score:
            self.best_score = self.score
            prefs.set_int("BestScore", self.best_score)
        if self.passed_walls > self.best_wall:
            self.best_wall = self.passed_walls
            prefs.set_int("BestWall", self.best_wall)

    def on_collision(self, is_wall_cube):
        """충돌 처리. is_wall_cube=True(기둥/바닥 등 치명적 물체)면 게임오버."""
        if not self.is_game_started:
            return
        # 시작 후 1초간은 무적 (시작 위치에서 억울하게 죽는 것 방지)
        if self._elapsed - self.start_time < 1.0:
            return
        if is_wall_cube:
            self.is_game_over = True
