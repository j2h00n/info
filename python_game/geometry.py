"""외부 모델 에셋 없이 박스 지오메트리를 즉석 생성 (Unity CreatePrimitive(Cube) 대체)."""
from panda3d.core import (
    Geom,
    GeomNode,
    GeomTriangles,
    GeomVertexData,
    GeomVertexFormat,
    GeomVertexWriter,
    NodePath,
)

# 정육면체 6개 면 정의. 각 항목 = (면 법선벡터, 그 면의 꼭짓점 4개).
# 꼭짓점은 -1~1 단위 큐브 좌표이며, make_box에서 실제 크기(sx,sy,sz)/2를 곱해 스케일함.
# 꼭짓점 순서는 반시계(CCW)여야 앞면이 바깥쪽을 향함 (순서 바꾸면 면이 안 보일 수 있음).
_FACES = [
    # (normal, 4 verts CCW)
    ((0, 0, 1), [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]),      # 위(+Z)
    ((0, 0, -1), [(-1, 1, -1), (1, 1, -1), (1, -1, -1), (-1, -1, -1)]),  # 아래(-Z)
    ((0, 1, 0), [(-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1)]),       # +Y
    ((0, -1, 0), [(1, -1, -1), (1, -1, 1), (-1, -1, 1), (-1, -1, -1)]),  # -Y
    ((1, 0, 0), [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)]),       # +X
    ((-1, 0, 0), [(-1, -1, 1), (-1, 1, 1), (-1, 1, -1), (-1, -1, -1)]),  # -X
]


# 면마다 똑같이 쓰는 UV(텍스처 좌표) 4개. 면 하나에 텍스처 한 장이 꽉 차게 붙음.
# 타일처럼 반복시키고 싶으면 값을 1보다 크게(예: 2) 바꾸고 텍스처 wrap 모드를 repeat로 설정.
_FACE_UVS = [(0, 0), (1, 0), (1, 1), (0, 1)]


def make_box(name, sx, sy, sz, color=(1, 1, 1, 1)):
    """가로 sx, 세로 sy, 깊이 sz 크기의 박스 NodePath를 만들어 반환. color=(r,g,b,a) 정점색."""
    # 정점 포맷: 위치(v3) + 법선(n3) + 색(c4) + 텍스처좌표(t2)
    fmt = GeomVertexFormat.get_v3n3c4t2()
    vdata = GeomVertexData(name, fmt, Geom.UH_static)
    vertex = GeomVertexWriter(vdata, "vertex")
    normal = GeomVertexWriter(vdata, "normal")
    colorw = GeomVertexWriter(vdata, "color")
    texcoord = GeomVertexWriter(vdata, "texcoord")

    tris = GeomTriangles(Geom.UH_static)
    idx = 0  # 지금까지 추가한 정점 개수(= 다음 면 첫 정점 인덱스)
    for n, verts in _FACES:
        for (vx, vy, vz), (u, v) in zip(verts, _FACE_UVS):
            # 단위 큐브(-1~1) 좌표 * (크기/2) = 실제 크기의 박스 좌표
            vertex.add_data3(vx * sx / 2.0, vy * sy / 2.0, vz * sz / 2.0)
            normal.add_data3(*n)
            colorw.add_data4(*color)
            texcoord.add_data2(u, v)
        # 사각형 면 하나 = 삼각형 2개
        tris.add_vertices(idx, idx + 1, idx + 2)
        tris.add_vertices(idx, idx + 2, idx + 3)
        idx += 4

    geom = Geom(vdata)
    geom.add_primitive(tris)
    node = GeomNode(name)
    node.add_geom(geom)
    return NodePath(node)
