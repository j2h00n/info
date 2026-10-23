"""배경 장식(풀/나무/구름) 절차적 배치. 모델 자체는 CC0(퍼블릭도메인급) 오픈소스
애셋 사용 - flo-bit/low-poly-asset-packs (github, CC0 라이선스 명시) 중 nature-pack.
https://github.com/flo-bit/low-poly-asset-packs

Unity 원본이 쓰던 BOXOPHOBIC/Mellow Fox 등은 유료 애셋이라 이식 안 했지만, 이건
완전 무료(CC0, "do whatever you want")라 그대로 갖다 씀. 파일은
Assets/CC0Nature/*.glb 에 받아둠 - 로딩은 panda3d-gltf(pip install panda3d-gltf) 필요.
"""
import os
import random

from panda3d.core import NodePath

# 모델(.glb) 폴더: 레포 루트/Assets/CC0Nature
_ASSET_DIR = os.path.join(
    os.path.dirname(os.path.dirname(__file__)), "Assets", "CC0Nature"
)

# 사용할 모델 파일 목록. 종류를 추가/제거하려면 여기에 파일명을 넣고 빼면 됨
GRASS_FILES = ["grass_1.glb", "grass_2.glb", "grass_3.glb", "grass_4.glb"]
CLOUD_FILES = ["cloud_1.glb", "cloud_2.glb", "cloud_3.glb"]
TREE_FILES = ["common_tree_1.glb", "common_tree_2.glb", "pine_tree_1.glb", "pine_tree_2.glb"]
BUSH_FILES = ["bush_1.glb", "bush_2.glb"]

_model_cache = {}  # {파일명: 로드된 NodePath 또는 None(실패)}


def _load_cached(filename):
    """glb를 한 번만 로드해서 캐시해두고, 이후엔 instanceTo로 값싸게 복제."""
    if filename in _model_cache:
        return _model_cache[filename]
    path = os.path.join(_ASSET_DIR, filename)
    # 파일이 없거나 로드에 실패하면 None을 캐시 -> 해당 장식만 조용히 건너뜀(게임은 계속 진행)
    if not os.path.exists(path):
        _model_cache[filename] = None
        return None
    try:
        import gltf  # panda3d-gltf 패키지(.glb 로더)
        model = NodePath(gltf.load_model(path))
    except Exception:
        model = None
    _model_cache[filename] = model
    return model


def spawn_instance(filename, parent):
    """filename 모델의 인스턴스를 parent 밑에 만들어 반환. 로드 실패시 None."""
    base = _load_cached(filename)
    if base is None:
        return None
    np = parent.attach_new_node(filename)
    base.instanceTo(np)  # 메모리 공유 복제 - 같은 모델을 많이 깔아도 가벼움
    return np


class SceneryItem:
    """장식물 하나의 배치 정보(종류/모델/위치/크기/방향). 실제 렌더링은 main.py가 담당."""

    def __init__(self, kind, filename, x, y, z, scale, heading):
        self.kind = kind          # "ground"(땅 위 물체) 또는 "cloud"(공중 구름)
        self.filename = filename
        self.x = x
        self.y = y
        self.z = z
        self.scale = scale
        self.heading = heading    # Z축 회전각(도)


class SceneryManager:
    # ===== [튜닝 상수] 배경 밀도/범위를 바꾸려면 여기를 수정 =====
    CHUNK_SPACING = 20.0    # 장식 묶음(chunk) 사이 Z 간격
    LOOKAHEAD = 80.0        # 플레이어 앞 이 거리까지 미리 생성
    DESPAWN_BEHIND = 30.0   # 플레이어 뒤로 이 거리 지나면 제거

    GRASS_PER_CHUNK = 6     # chunk당 풀/덤불 개수 (늘리면 땅이 풍성해지지만 느려질 수 있음)
    TREE_PER_CHUNK = 2      # chunk당 나무 개수
    CLOUD_PER_CHUNK = 2     # chunk당 구름 개수

    GROUND_X_RANGE = (-45.0, 45.0)   # 풀/나무가 놓일 X 범위
    TREE_EXCLUDE_X = 14.0  # 비행경로(±LIMIT_X≈11.5) 안쪽엔 나무 안 나오게
    CLOUD_X_RANGE = (-60.0, 60.0)    # 구름 X 범위
    GROUND_Y = -7.1  # main.py의 Game.FLOOR_Y(-7.6) 바로 위
    CLOUD_Y_RANGE = (35.0, 55.0)     # 구름 높이 범위

    def __init__(self, seed=None):
        self._rng = random.Random(seed)
        self.next_chunk_z = 0.0   # 다음에 생성할 chunk의 Z
        self.chunks = []          # list of list[SceneryItem]

    def update(self, player_z):
        """매 프레임 호출: 앞쪽에 새 chunk 생성, 뒤로 지나간 chunk 제거 (wall.py와 같은 방식)."""
        if player_z + self.LOOKAHEAD > self.next_chunk_z:
            self.chunks.append(self._spawn_chunk(self.next_chunk_z))
            self.next_chunk_z += self.CHUNK_SPACING

        if self.chunks:
            oldest = self.chunks[0]
            if oldest and oldest[0].z < player_z - self.DESPAWN_BEHIND:
                self.chunks.pop(0)

    def _rand_z(self, z_pos):
        """chunk 중심 z_pos 주변 ±(간격/2) 안에서 랜덤 Z (한 줄로 늘어서 보이지 않게)."""
        return z_pos + self._rng.uniform(-self.CHUNK_SPACING / 2, self.CHUNK_SPACING / 2)

    def _spawn_chunk(self, z_pos):
        """z_pos 근처에 풀/덤불 + 나무 + 구름을 랜덤 배치해 SceneryItem 리스트로 반환."""
        items = []
        # 풀/덤불: 땅(GROUND_Y)에 X 전범위 랜덤, 크기 1.5~3.0배, 방향 랜덤
        for _ in range(self.GRASS_PER_CHUNK):
            x = self._rng.uniform(*self.GROUND_X_RANGE)
            items.append(SceneryItem(
                "ground", self._rng.choice(GRASS_FILES + BUSH_FILES),
                x, self.GROUND_Y, self._rand_z(z_pos),
                self._rng.uniform(1.5, 3.0), self._rng.uniform(0, 360),
            ))

        # 나무: 비행경로를 가리지 않도록 |x| >= TREE_EXCLUDE_X 바깥에만, 좌/우 50:50
        for _ in range(self.TREE_PER_CHUNK):
            x = self._rng.uniform(self.TREE_EXCLUDE_X, self.GROUND_X_RANGE[1])
            if self._rng.random() < 0.5:
                x = -x
            items.append(SceneryItem(
                "ground", self._rng.choice(TREE_FILES),
                x, self.GROUND_Y, self._rand_z(z_pos),
                self._rng.uniform(1.5, 2.5), self._rng.uniform(0, 360),
            ))

        # 구름: 하늘 높이(CLOUD_Y_RANGE)에 X 전범위 랜덤
        for _ in range(self.CLOUD_PER_CHUNK):
            x = self._rng.uniform(*self.CLOUD_X_RANGE)
            y = self._rng.uniform(*self.CLOUD_Y_RANGE)
            items.append(SceneryItem(
                "cloud", self._rng.choice(CLOUD_FILES),
                x, y, self._rand_z(z_pos),
                self._rng.uniform(1.0, 2.0), self._rng.uniform(0, 360),
            ))

        return items
