"""Unity 원본이 쓰던 Assets/New Folder/Seagull.fbx(ASCII FBX 6.1, Blender export)를
Panda3D가 못 읽는 포맷이라 직접 파싱해서 지오메트리로 재구성.

Panda3D는 FBX를 네이티브 로딩 못 하고(assimp 바인딩도 이 FBX 6.1 구버전은 미지원),
텍스처 파일(Seagull.png)도 프로젝트에 실제로는 포함돼 있지 않아 버텍스컬러만 입힘.
스키닝/애니메이션은 무시하고 바인드포즈 스태틱 메시만 추출.

날갯짓 애니메이션용으로 X좌표 기준 몸통/왼쪽날개/오른쪽날개 3그룹으로 대충 쪼갬
(정밀한 피벗 계산 안 함 - 임시 모델이라 대충만 맞춰둠, 나중에 다른 glb로 교체 예정).
"""
import re
import os

from panda3d.core import (
    Geom,
    GeomNode,
    GeomTriangles,
    GeomVertexData,
    GeomVertexFormat,
    GeomVertexWriter,
    NodePath,
)

_FBX_PATH = os.path.join(
    os.path.dirname(os.path.dirname(__file__)), "Assets", "New Folder", "Seagull.fbx"
)

BODY_HALF_WIDTH = 0.15  # 이 안쪽(|x|<=)은 몸통, 바깥쪽은 날개로 침


def _extract_numbers(block_text, key):
    m = re.search(r"(?<![A-Za-z])" + key + r":\s*([0-9,.\-\s]+)", block_text)
    if not m:
        return []
    raw = m.group(1).strip().rstrip(",")
    return [float(x) for x in raw.split(",") if x.strip() != ""]


def _load_mesh_block(fbx_text):
    start = fbx_text.index('Model: "Model::Seagull", "Mesh"')
    end = fbx_text.index('Model: "Model::Root"', start)
    return fbx_text[start:end]


def _build_geom(name, corners, pivot_x, color):
    """corners: [(vx,vy,vz, nx,ny,nz, u,v), ...] - 3개씩 묶여 이미 삼각형 단위."""
    fmt = GeomVertexFormat.get_v3n3c4t2()
    vdata = GeomVertexData(name, fmt, Geom.UH_static)
    vwriter = GeomVertexWriter(vdata, "vertex")
    nwriter = GeomVertexWriter(vdata, "normal")
    cwriter = GeomVertexWriter(vdata, "color")
    twriter = GeomVertexWriter(vdata, "texcoord")
    tris = GeomTriangles(Geom.UH_static)

    for i, (vx, vy, vz, nx, ny, nz, u, v) in enumerate(corners):
        vwriter.add_data3(vx - pivot_x, vy, vz)
        nwriter.add_data3(nx, ny, nz)
        cwriter.add_data4(*color)
        twriter.add_data2(u, v)
    for i in range(0, len(corners) - 2, 3):
        tris.add_vertices(i, i + 1, i + 2)

    geom = Geom(vdata)
    geom.add_primitive(tris)
    node = GeomNode(name)
    node.add_geom(geom)
    return NodePath(node)


def load_seagull_parts(color=(0.85, 0.85, 0.85, 1)):
    """Seagull.fbx를 파싱해 몸통/왼쪽날개/오른쪽날개 NodePath 3개를 담은 dict로 반환.
    실패시 None. 반환값: {"root": NodePath, "body": NodePath, "wing_neg": NodePath, "wing_pos": NodePath}
    (wing_neg/wing_pos = x<0 / x>0 쪽 날개, 좌우가 실제로 어느쪽인지는 씬 확인 필요)"""
    if not os.path.exists(_FBX_PATH):
        return None

    with open(_FBX_PATH, "r", encoding="latin-1") as f:
        text = f.read()

    block = _load_mesh_block(text)

    vertices = _extract_numbers(block, "Vertices")
    poly_index = [int(v) for v in _extract_numbers(block, "PolygonVertexIndex")]
    normals = _extract_numbers(block, "Normals")
    uvs = _extract_numbers(block, "UV")
    uv_index = [int(v) for v in _extract_numbers(block, "UVIndex")]

    if not vertices or not poly_index:
        return None

    # 1. 폴리곤 단위로 corner 데이터 모으고(팬 삼각분할), 폴리곤 평균 x로 그룹 배정
    groups = {"body": [], "wing_neg": [], "wing_pos": []}

    corner = 0
    poly_corners = []  # [(vx,vy,vz,nx,ny,nz,u,v), ...] 현재 폴리곤

    for raw_idx in poly_index:
        is_last = raw_idx < 0
        v_idx = (-raw_idx - 1) if is_last else raw_idx

        vx, vy, vz = vertices[v_idx * 3:v_idx * 3 + 3]
        if normals and (corner * 3 + 2) < len(normals):
            nx, ny, nz = normals[corner * 3:corner * 3 + 3]
        else:
            nx, ny, nz = 0.0, 0.0, 1.0
        if uvs and uv_index and corner < len(uv_index):
            uv_i = uv_index[corner]
            u, v = uvs[uv_i * 2], uvs[uv_i * 2 + 1]
        else:
            u, v = 0.0, 0.0

        poly_corners.append((vx, vy, vz, nx, ny, nz, u, v))
        corner += 1

        if is_last:
            avg_x = sum(c[0] for c in poly_corners) / len(poly_corners)
            if abs(avg_x) <= BODY_HALF_WIDTH:
                key = "body"
            elif avg_x < 0:
                key = "wing_neg"
            else:
                key = "wing_pos"

            # 팬 삼각분할해서 그룹의 삼각형 리스트에 추가
            for i in range(1, len(poly_corners) - 1):
                groups[key].append(poly_corners[0])
                groups[key].append(poly_corners[i])
                groups[key].append(poly_corners[i + 1])

            poly_corners = []

    # 대충: 피벗 이동 없이 원래 좌표 그대로 붙여서 몸통과 끊어지지 않게 함
    # (회전축이 몸통 중심이라 정확한 날개축 힌지는 아니지만, 이 모델은 임시라 이 정도만)
    root = NodePath("seagull_root")
    body_np = _build_geom("body", groups["body"], 0.0, color)
    body_np.reparentTo(root)

    wing_neg_np = _build_geom("wing_neg", groups["wing_neg"], 0.0, color)
    wing_neg_np.reparentTo(root)

    wing_pos_np = _build_geom("wing_pos", groups["wing_pos"], 0.0, color)
    wing_pos_np.reparentTo(root)

    return {"root": root, "body": body_np, "wing_neg": wing_neg_np, "wing_pos": wing_pos_np}


def load_seagull_geom(color=(0.85, 0.85, 0.85, 1)):
    """기존 API 호환용: 날개 구분 없이 전체를 하나의 NodePath로 반환."""
    parts = load_seagull_parts(color)
    if parts is None:
        return None
    return parts["root"]
