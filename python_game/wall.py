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
    CHUNK_SPACING = 15.0
    LOOKAHEAD = 60.0
    DESPAWN_BEHIND = 15.0

    COLUMN_COUNT = 9
    PILLAR_WIDTH = 7.0
    GAP_THICKNESS = 1.0

    def __init__(self, seed=None):
        self._rng = random.Random(seed)
        self.next_chunk_z = 20.0
        self.chunks = []  # list of list[Pipe]

    def update(self, player_z):
        if player_z + self.LOOKAHEAD > self.next_chunk_z:
            self.chunks.append(self._spawn_pipes(self.next_chunk_z))
            self.next_chunk_z += self.CHUNK_SPACING

        if self.chunks:
            oldest = self.chunks[0]
            if oldest and oldest[0].z < player_z - self.DESPAWN_BEHIND:
                self.chunks.pop(0)

    def _spawn_pipes(self, z_pos):
        chunk = []
        spacing_x = self.PILLAR_WIDTH + self.GAP_THICKNESS
        start_x = -((self.COLUMN_COUNT - 1) / 2.0) * spacing_x

        for i in range(self.COLUMN_COUNT):
            x_pos = start_x + i * spacing_x
            gap_position = self._rng.uniform(2.0, 9.0)
            gap_size = 3.5

            chunk.append(
                Pipe(x_pos, gap_position - gap_size - 15.0, z_pos, self.PILLAR_WIDTH, 30.0, 2.0)
            )
            chunk.append(
                Pipe(x_pos, gap_position + gap_size + 15.0, z_pos, self.PILLAR_WIDTH, 30.0, 2.0)
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
