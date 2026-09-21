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

_ASSET_DIR = os.path.join(
    os.path.dirname(os.path.dirname(__file__)), "Assets", "CC0Nature"
)

GRASS_FILES = ["grass_1.glb", "grass_2.glb", "grass_3.glb", "grass_4.glb"]
CLOUD_FILES = ["cloud_1.glb", "cloud_2.glb", "cloud_3.glb"]
TREE_FILES = ["common_tree_1.glb", "common_tree_2.glb", "pine_tree_1.glb", "pine_tree_2.glb"]
BUSH_FILES = ["bush_1.glb", "bush_2.glb"]

_model_cache = {}


def _load_cached(filename):
    """glb를 한 번만 로드해서 캐시해두고, 이후엔 instanceTo로 값싸게 복제."""
    if filename in _model_cache:
        return _model_cache[filename]
    path = os.path.join(_ASSET_DIR, filename)
    if not os.path.exists(path):
        _model_cache[filename] = None
        return None
    try:
        import gltf
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
    base.instanceTo(np)
    return np


class SceneryItem:
    def __init__(self, kind, filename, x, y, z, scale, heading):
        self.kind = kind
        self.filename = filename
        self.x = x
        self.y = y
        self.z = z
        self.scale = scale
        self.heading = heading


class SceneryManager:
    CHUNK_SPACING = 20.0
    LOOKAHEAD = 80.0
    DESPAWN_BEHIND = 30.0

    GRASS_PER_CHUNK = 6
    TREE_PER_CHUNK = 2
    CLOUD_PER_CHUNK = 2

    GROUND_X_RANGE = (-45.0, 45.0)
    TREE_EXCLUDE_X = 14.0  # 비행경로(±LIMIT_X≈11.5) 안쪽엔 나무 안 나오게
    CLOUD_X_RANGE = (-60.0, 60.0)
    GROUND_Y = -7.1  # main.py의 Game.FLOOR_Y(-7.6) 바로 위
    CLOUD_Y_RANGE = (35.0, 55.0)

    def __init__(self, seed=None):
        self._rng = random.Random(seed)
        self.next_chunk_z = 0.0
        self.chunks = []

    def update(self, player_z):
        if player_z + self.LOOKAHEAD > self.next_chunk_z:
            self.chunks.append(self._spawn_chunk(self.next_chunk_z))
            self.next_chunk_z += self.CHUNK_SPACING

        if self.chunks:
            oldest = self.chunks[0]
            if oldest and oldest[0].z < player_z - self.DESPAWN_BEHIND:
                self.chunks.pop(0)

    def _rand_z(self, z_pos):
        return z_pos + self._rng.uniform(-self.CHUNK_SPACING / 2, self.CHUNK_SPACING / 2)

    def _spawn_chunk(self, z_pos):
        items = []
        for _ in range(self.GRASS_PER_CHUNK):
            x = self._rng.uniform(*self.GROUND_X_RANGE)
            items.append(SceneryItem(
                "ground", self._rng.choice(GRASS_FILES + BUSH_FILES),
                x, self.GROUND_Y, self._rand_z(z_pos),
                self._rng.uniform(1.5, 3.0), self._rng.uniform(0, 360),
            ))

        for _ in range(self.TREE_PER_CHUNK):
            x = self._rng.uniform(self.TREE_EXCLUDE_X, self.GROUND_X_RANGE[1])
            if self._rng.random() < 0.5:
                x = -x
            items.append(SceneryItem(
                "ground", self._rng.choice(TREE_FILES),
                x, self.GROUND_Y, self._rand_z(z_pos),
                self._rng.uniform(1.5, 2.5), self._rng.uniform(0, 360),
            ))

        for _ in range(self.CLOUD_PER_CHUNK):
            x = self._rng.uniform(*self.CLOUD_X_RANGE)
            y = self._rng.uniform(*self.CLOUD_Y_RANGE)
            items.append(SceneryItem(
                "cloud", self._rng.choice(CLOUD_FILES),
                x, y, self._rand_z(z_pos),
                self._rng.uniform(1.0, 2.0), self._rng.uniform(0, 360),
            ))

        return items
