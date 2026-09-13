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

_FACES = [
    # (normal, 4 verts CCW)
    ((0, 0, 1), [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]),
    ((0, 0, -1), [(-1, 1, -1), (1, 1, -1), (1, -1, -1), (-1, -1, -1)]),
    ((0, 1, 0), [(-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1)]),
    ((0, -1, 0), [(1, -1, -1), (1, -1, 1), (-1, -1, 1), (-1, -1, -1)]),
    ((1, 0, 0), [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)]),
    ((-1, 0, 0), [(-1, -1, 1), (-1, 1, 1), (-1, 1, -1), (-1, -1, -1)]),
]


_FACE_UVS = [(0, 0), (1, 0), (1, 1), (0, 1)]


def make_box(name, sx, sy, sz, color=(1, 1, 1, 1)):
    fmt = GeomVertexFormat.get_v3n3c4t2()
    vdata = GeomVertexData(name, fmt, Geom.UH_static)
    vertex = GeomVertexWriter(vdata, "vertex")
    normal = GeomVertexWriter(vdata, "normal")
    colorw = GeomVertexWriter(vdata, "color")
    texcoord = GeomVertexWriter(vdata, "texcoord")

    tris = GeomTriangles(Geom.UH_static)
    idx = 0
    for n, verts in _FACES:
        for (vx, vy, vz), (u, v) in zip(verts, _FACE_UVS):
            vertex.add_data3(vx * sx / 2.0, vy * sy / 2.0, vz * sz / 2.0)
            normal.add_data3(*n)
            colorw.add_data4(*color)
            texcoord.add_data2(u, v)
        tris.add_vertices(idx, idx + 1, idx + 2)
        tris.add_vertices(idx, idx + 2, idx + 3)
        idx += 4

    geom = Geom(vdata)
    geom.add_primitive(tris)
    node = GeomNode(name)
    node.add_geom(geom)
    return NodePath(node)
