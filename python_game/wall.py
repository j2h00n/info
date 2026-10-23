"""NewMonoBehaviourScript.cs 1:1 이식. 9개 기둥(컬럼), 컬럼마다 위/아래 파이프 +
랜덤 틈새 높이. chunkSpacing마다 한 줄(chunk) 생성, 지나간 chunk는 제거."""
import random


class Pipe:
    """파이프(기둥) 하나. (x,y,z)=중심 좌표, (sx,sy,sz)=가로/세로/깊이 크기."""

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
    # ===== [튜닝 상수] 난이도/레이아웃을 바꾸려면 여기를 수정 =====
    COLUMN_COUNT = 3        # 한 줄(chunk)에 놓이는 기둥 개수. 바꾸면 boundary.py LIMIT_X도 맞춰야 함
    PILLAR_WIDTH = 7.0      # 기둥 가로 폭
    GAP_THICKNESS = 1.0     # 기둥과 기둥 사이 가로 간격
    _SPACING_X = PILLAR_WIDTH + GAP_THICKNESS  # 인접 기둥 중심 간 거리
    _HORIZONTAL_SPAN = (COLUMN_COUNT - 1) * _SPACING_X + PILLAR_WIDTH  # 3개 기준 약 23

    # 원본(9기둥 기준) 세로 전체폭이 약 74였음 - 가로 23의 1.6배(~36.8)에 맞춰 스케일.
    # 저공비행(파이프 밑으로 그냥 통과) 방지는 파이프를 무한정 늘리는 대신
    # main.py에서 바닥 높이(FLOOR_Y) 충돌판정으로 따로 처리함.
    VERTICAL_SCALE = 0.25   # 세로 관련 값 전체 배율
    GAP_SCALE = 0.7  # 틈 크기는 따로 크게 - 구멍 너무 작다는 피드백 반영
    PILLAR_HEIGHT = 30.0 * VERTICAL_SCALE   # 기둥 세로 길이
    GAP_POS_MIN = 2.0 * VERTICAL_SCALE      # 틈 중심 높이의 랜덤 범위 최소
    GAP_POS_MAX = 9.0 * VERTICAL_SCALE      # 틈 중심 높이의 랜덤 범위 최대
    BASE_GAP_SIZE = 3.5 * GAP_SCALE         # 틈 크기(중심에서 위/아래 파이프까지 거리). 클수록 쉬움

    CHUNK_SPACING = 15.0    # 기둥 줄 사이 전진(Z) 간격. bird.py의 passed_walls 계산(15)과 맞춰야 함
    LOOKAHEAD = 60.0        # 플레이어 앞 이 거리까지 미리 생성
    DESPAWN_BEHIND = 15.0   # 플레이어 뒤로 이 거리 이상 지나면 제거

    def __init__(self, seed=None, easy=False):
        # 실험 모드(easy=True): 컬럼 배치 알고리즘은 동일, 간격/틈만 넉넉하게
        self.easy = easy
        # easy일 때 배율(1.5, 1.7)을 바꾸면 쉬운 모드 난이도 조절 가능
        self.chunk_spacing = self.CHUNK_SPACING * (1.5 if easy else 1.0)
        self.gap_size = self.BASE_GAP_SIZE * (1.7 if easy else 1.0)

        self._rng = random.Random(seed)   # seed를 주면 같은 맵이 재현됨(디버깅용)
        self.next_chunk_z = 20.0          # 첫 기둥 줄의 Z 위치 (처음 만나는 벽까지의 거리)
        self.chunks = []  # list of list[Pipe]

    def update(self, player_z):
        """매 프레임 호출: 앞쪽에 새 줄을 생성하고, 뒤로 지나간 줄은 제거."""
        # 플레이어 앞 LOOKAHEAD 범위에 아직 안 만든 줄이 있으면 한 줄 생성
        if player_z + self.LOOKAHEAD > self.next_chunk_z:
            self.chunks.append(self._spawn_pipes(self.next_chunk_z))
            self.next_chunk_z += self.chunk_spacing

        # 가장 오래된 줄이 충분히 뒤로 지나갔으면 삭제
        if self.chunks:
            oldest = self.chunks[0]
            if oldest and oldest[0].z < player_z - self.DESPAWN_BEHIND:
                self.chunks.pop(0)

    def _spawn_pipes(self, z_pos):
        """z_pos 위치에 기둥 한 줄(컬럼마다 위/아래 파이프 2개)을 만들어 리스트로 반환."""
        chunk = []
        spacing_x = self.PILLAR_WIDTH + self.GAP_THICKNESS
        # 컬럼들을 x=0 기준 좌우 대칭으로 배치하기 위한 시작 x
        start_x = -((self.COLUMN_COUNT - 1) / 2.0) * spacing_x
        half_h = self.PILLAR_HEIGHT / 2.0

        for i in range(self.COLUMN_COUNT):
            x_pos = start_x + i * spacing_x
            # 컬럼마다 틈 중심 높이를 랜덤으로 결정
            gap_position = self._rng.uniform(self.GAP_POS_MIN, self.GAP_POS_MAX)
            gap_size = self.gap_size

            # 아래 파이프: 틈 아래쪽 끝(gap_position - gap_size)에서 아래로 뻗음
            chunk.append(
                Pipe(x_pos, gap_position - gap_size - half_h, z_pos, self.PILLAR_WIDTH, self.PILLAR_HEIGHT, 1.0)
            )
            # 위 파이프: 틈 위쪽 끝(gap_position + gap_size)에서 위로 뻗음
            chunk.append(
                Pipe(x_pos, gap_position + gap_size + half_h, z_pos, self.PILLAR_WIDTH, self.PILLAR_HEIGHT, 1.0)
            )

        return chunk

    def all_pipes(self):
        """현재 존재하는 모든 파이프를 하나씩 반환(제너레이터). main.py가 렌더링/충돌에 사용."""
        for chunk in self.chunks:
            for pipe in chunk:
                yield pipe

    def check_collision(self, bx, by, bz, bird_half=0.5):
        """새(중심 bx,by,bz, 반크기 bird_half)가 어떤 파이프와 겹치는지 AABB(축정렬 박스)로 검사.
        bird_half를 키우면 충돌 판정이 더 빡빡해짐(새 히트박스가 커짐)."""
        for pipe in self.all_pipes():
            # 세 축 중 하나라도 겹치지 않으면 이 파이프와는 충돌 아님 -> 다음 파이프로
            if abs(bx - pipe.x) > pipe.sx / 2.0 + bird_half:
                continue
            if abs(by - pipe.y) > pipe.sy / 2.0 + bird_half:
                continue
            if abs(bz - pipe.z) > pipe.sz / 2.0 + bird_half:
                continue
            return True
        return False
