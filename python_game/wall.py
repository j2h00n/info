"""NewMonoBehaviourScript.cs 1:1 이식. 9개 기둥(컬럼), 컬럼마다 위/아래 파이프 +
랜덤 틈새 높이. chunkSpacing마다 한 줄(chunk) 생성, 지나간 chunk는 제거."""
import random


class Pipe:
    def __init__(self, x, y, z, sx, sy, sz):
        self.x = x
        self.y = y
        self.z = z
        self.sx = sx
        self.sy = sy
        self.sz = sz


class WallSpawner:
    # 화면(카메라 시점)에서 좌우=X(가로)/위아래=Y(세로)로 보임 - 가로에 기둥 3개만
    # 들어오게 컬럼 수를 줄이고, 세로(파이프 높이/틈 범위)를 가로 폭의 1.5~1.7배로 맞춤.
    # 전진(Z)간격은 원본 Unity 값(15) 그대로 유지.
    COLUMN_COUNT = 3
    PILLAR_WIDTH = 7.0
    GAP_THICKNESS = 1.0
    _SPACING_X = PILLAR_WIDTH + GAP_THICKNESS
    _HORIZONTAL_SPAN = (COLUMN_COUNT - 1) * _SPACING_X + PILLAR_WIDTH  # 3개 기준 약 23

    # 원본(9기둥 기준) 세로 전체폭이 약 74였음 - 가로 23의 1.6배(~36.8)에 맞춰 스케일.
    # 저공비행(파이프 밑으로 그냥 통과) 방지는 파이프를 무한정 늘리는 대신
    # main.py에서 바닥 높이(FLOOR_Y) 충돌판정으로 따로 처리함.
    VERTICAL_SCALE = 0.25
    GAP_SCALE = 0.7  # 틈 크기는 따로 크게 - 구멍 너무 작다는 피드백 반영
    PILLAR_HEIGHT = 30.0 * VERTICAL_SCALE
    GAP_POS_MIN = 2.0 * VERTICAL_SCALE
    GAP_POS_MAX = 9.0 * VERTICAL_SCALE
    BASE_GAP_SIZE = 3.5 * GAP_SCALE

    CHUNK_SPACING = 15.0
    LOOKAHEAD = 60.0
    DESPAWN_BEHIND = 15.0

    def __init__(self, seed=None, easy=False):
        # 실험 모드(easy=True): 컬럼 배치 알고리즘은 동일, 간격/틈만 넉넉하게
        self.easy = easy
        self.chunk_spacing = self.CHUNK_SPACING * (1.5 if easy else 1.0)
        self.gap_size = self.BASE_GAP_SIZE * (1.7 if easy else 1.0)

        self._rng = random.Random(seed)
        self.next_chunk_z = 20.0
        self.chunks = []  # list of list[Pipe]

    def update(self, player_z):
        if player_z + self.LOOKAHEAD > self.next_chunk_z:
            self.chunks.append(self._spawn_pipes(self.next_chunk_z))
            self.next_chunk_z += self.chunk_spacing

        if self.chunks:
            oldest = self.chunks[0]
            if oldest and oldest[0].z < player_z - self.DESPAWN_BEHIND:
                self.chunks.pop(0)

    def _spawn_pipes(self, z_pos):
        chunk = []
        spacing_x = self.PILLAR_WIDTH + self.GAP_THICKNESS
        start_x = -((self.COLUMN_COUNT - 1) / 2.0) * spacing_x
        half_h = self.PILLAR_HEIGHT / 2.0

        for i in range(self.COLUMN_COUNT):
            x_pos = start_x + i * spacing_x
            gap_position = self._rng.uniform(self.GAP_POS_MIN, self.GAP_POS_MAX)
            gap_size = self.gap_size

            chunk.append(
                Pipe(x_pos, gap_position - gap_size - half_h, z_pos, self.PILLAR_WIDTH, self.PILLAR_HEIGHT, 1.0)
            )
            chunk.append(
                Pipe(x_pos, gap_position + gap_size + half_h, z_pos, self.PILLAR_WIDTH, self.PILLAR_HEIGHT, 1.0)
            )

        return chunk

    def all_pipes(self):
        for chunk in self.chunks:
            for pipe in chunk:
                yield pipe

    def check_collision(self, bx, by, bz, bird_half=0.5):
        for pipe in self.all_pipes():
            if abs(bx - pipe.x) > pipe.sx / 2.0 + bird_half:
                continue
            if abs(by - pipe.y) > pipe.sy / 2.0 + bird_half:
                continue
            if abs(bz - pipe.z) > pipe.sz / 2.0 + bird_half:
                continue
            return True
        return False
