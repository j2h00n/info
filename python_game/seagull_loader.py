"""Unity 원본이 쓰던 Assets/New Folder/Seagull.fbx(ASCII FBX 6.1, Blender export)를
Panda3D가 못 읽는 포맷이라 직접 파싱해서 지오메트리로 재구성.

Panda3D는 FBX를 네이티브 로딩 못 하고(assimp 바인딩도 이 FBX 6.1 구버전은 미지원),
텍스처 파일(Seagull.png)도 프로젝트에 실제로는 포함돼 있지 않아 버텍스컬러만 입힘.
스키닝/애니메이션은 무시하고 바인드포즈 스태틱 메시만 추출.
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


def load_seagull_geom(color=(0.85, 0.85, 0.85, 1)):
    """Seagull.fbx를 파싱해 Panda3D NodePath로 반환. 실패시 None."""
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

    fmt = GeomVertexFormat.get_v3n3c4t2()
    vdata = GeomVertexData("seagull", fmt, Geom.UH_static)
    vwriter = GeomVertexWriter(vdata, "vertex")
    nwriter = GeomVertexWriter(vdata, "normal")
    cwriter = GeomVertexWriter(vdata, "color")
    twriter = GeomVertexWriter(vdata, "texcoord")

    tris = GeomTriangles(Geom.UH_static)

    corner = 0
    row = 0
    poly_start_row = 0
    poly_corner_count = 0

    for raw_idx in poly_index:
        is_last = raw_idx < 0
        v_idx = (-raw_idx - 1) if is_last else raw_idx

        vx = vertices[v_idx * 3]
        vy = vertices[v_idx * 3 + 1]
        vz = vertices[v_idx * 3 + 2]
        vwriter.add_data3(vx, vy, vz)

        if normals and (corner * 3 + 2) < len(normals):
            nwriter.add_data3(normals[corner * 3], normals[corner * 3 + 1], normals[corner * 3 + 2])
        else:
            nwriter.add_data3(0, 0, 1)

        if uvs and uv_index and corner < len(uv_index):
            uv_i = uv_index[corner]
            twriter.add_data2(uvs[uv_i * 2], uvs[uv_i * 2 + 1])
        else:
            twriter.add_data2(0, 0)

        cwriter.add_data4(*color)

        poly_corner_count += 1
        corner += 1
        row += 1

        if is_last:
            # 팬 삼각분할: (row0, row0+i, row0+i+1)
            for i in range(1, poly_corner_count - 1):
                tris.add_vertices(poly_start_row, poly_start_row + i, poly_start_row + i + 1)
            poly_start_row = row
            poly_corner_count = 0

    geom = Geom(vdata)
    geom.add_primitive(tris)
    node = GeomNode("seagull")
    node.add_geom(geom)
    return NodePath(node)
