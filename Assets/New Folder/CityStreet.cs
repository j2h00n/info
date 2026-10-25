using UnityEngine;
using System.Collections.Generic;

// 🏙️ 도시 배경: 새가 날아가는 큰길 (넓은 4차선 차도 + 차선 + 인도 + 가로등) + 양옆 빌딩숲
// 빌딩: 층마다 띠(벽돌) 또는 세로 줄(유리 빌딩) + 꼭대기 장식띠 + 높으면 계단식 + 1층 상가(진열창·색 차양·간판) + 옥상(실외기·물탱크·안테나·네온 광고판)
// IceCaveTunnel처럼 세상에 고정된 조각(60m)들을 이어 붙이고, 새 뒤로 한참 지나간 조각은 앞쪽으로 옮겨서 끝없이 이어지게 함
// 새가 다니는 공간(x ±34.5, y -5 ~ 17)은 비워 둠: 차도 표면 roadY(-6.5), 빌딩은 x ±42 바깥. 충돌 없음 (보기만)
public class CityStreet : MonoBehaviour
{
    public Transform bird;
    public Material roadMat;        // 아스팔트
    public Material lineMat;        // 흰 차선
    public Material centerLineMat;  // 노란 중앙선
    public Material sidewalkMat;    // 인도
    public Material[] buildingMats; // 빌딩 외벽 (몇 가지 색)
    public Material windowMat;      // 꺼진 창문 (어두운 유리)
    public Material litWindowMat;   // 불 켜진 창문 (발광)
    public Material roofMat;        // 옥상 설비
    public Material lampMat;        // 가로등 불빛 (발광)
    public Material trimMat;        // 층 띠·장식띠·세로 줄 (밝은 콘크리트)
    public Material storefrontMat;  // 1층 상가 진열창 (따뜻한 발광)
    public Material[] awningMats;   // 가게 차양 색
    public Material[] neonMats;     // 간판·광고판 (발광)
    public Material tankMat;        // 옥상 물탱크 (나무색)

    public float segmentLength = 60f;
    public int segmentCount = 11;          // 뒤 120m + 앞 540m 정도
    public float behind = 120f;
    public float roadY = -6.5f;
    public float roadHalfWidth = 36f;
    public float buildingX = 42f;

    // 차선 사이 경계 (흰 점선): 4차선, 차선 가운데는 ±8.5, ±25.5 (한 차선 17m)
    public static readonly float[] LaneLines = { -17f, 17f };

    private readonly List<Transform> segments = new List<Transform>();

    // 조각을 만드는 동안 쓰는 메쉬 묶음 (머티리얼마다 하나)
    private MeshKit[] walls, awnings, neons;
    private MeshKit dark, lit, roof, trim, store, tank;

    void Start()
    {
        if (bird == null) bird = GameObject.Find("Cube").transform;
        float startZ = Mathf.Floor((bird.position.z - behind) / segmentLength) * segmentLength;
        for (int i = 0; i < segmentCount; i++)
        {
            Transform seg = BuildSegment(i);
            seg.position = new Vector3(0f, 0f, startZ + i * segmentLength);
            segments.Add(seg);
        }
    }

    void LateUpdate()
    {
        if (bird == null) return;
        // 맨 뒤 조각이 새 뒤로 behind보다 멀어지면 맨 앞으로
        foreach (Transform seg in segments)
        {
            if (seg.position.z + segmentLength < bird.position.z - behind)
            {
                float front = float.MinValue;
                foreach (Transform s in segments) front = Mathf.Max(front, s.position.z);
                seg.position = new Vector3(0f, 0f, front + segmentLength);
            }
        }
    }

    // ───────────────────────── 조각 만들기 ─────────────────────────

    Transform BuildSegment(int seed)
    {
        Random.State saved = Random.state;
        Random.InitState(9100 + seed * 37);
        Transform seg = new GameObject("CitySegment").transform;
        seg.SetParent(transform, false);
        float L = segmentLength;

        var road = new MeshKit();
        var lines = new MeshKit();
        var center = new MeshKit();
        var walk = new MeshKit();
        var lamp = new MeshKit();
        roof = new MeshKit();
        dark = new MeshKit();
        lit = new MeshKit();
        trim = new MeshKit();
        store = new MeshKit();
        tank = new MeshKit();
        walls = Kits(buildingMats);
        awnings = Kits(awningMats);
        neons = Kits(neonMats);

        // 차도 + 흰 점선 차선 + 노란 겹 중앙선
        Quad(road, new Vector3(-roadHalfWidth, roadY, 0f), new Vector3(roadHalfWidth, roadY, 0f), new Vector3(roadHalfWidth, roadY, L), new Vector3(-roadHalfWidth, roadY, L), Vector3.up);
        // 넓은 차선에 맞게 굵고 긴 선
        foreach (float x in LaneLines)
            for (float z = 1.5f; z < L; z += 15f)
                FlatRect(lines, x - 0.3f, x + 0.3f, z, z + 7f, roadY + 0.02f);
        FlatRect(center, -0.8f, -0.35f, 0f, L, roadY + 0.02f);
        FlatRect(center, 0.35f, 0.8f, 0f, L, roadY + 0.02f);

        for (int side = -1; side <= 1; side += 2)
        {
            // 인도 (연석 높이 0.4)
            float w0 = side < 0 ? -buildingX : roadHalfWidth, w1 = side < 0 ? -roadHalfWidth : buildingX;
            Box(walk, new Vector3(w0, roadY - 0.5f, 0f), new Vector3(w1, roadY + 0.4f, L));

            // 가로등: 인도 가장자리에서 차도 쪽으로 팔을 뻗음 (새가 못 가는 x ±37)
            for (float z = 10f; z < L; z += 30f)
            {
                float px = side * (roadHalfWidth + 1.2f);
                Box(roof, new Vector3(px - 0.2f, roadY + 0.4f, z - 0.2f), new Vector3(px + 0.2f, roadY + 10f, z + 0.2f));
                Box(roof, new Vector3(Mathf.Min(px, px - side * 2.6f), roadY + 9.8f, z - 0.15f), new Vector3(Mathf.Max(px, px - side * 2.6f), roadY + 10.1f, z + 0.15f));
                float hx = px - side * 2.6f;
                Box(lamp, new Vector3(hx - 0.5f, roadY + 9.5f, z - 0.3f), new Vector3(hx + 0.5f, roadY + 9.8f, z + 0.3f));
            }

            // 길가 빌딩: 앞뒤로 다른 너비, 사이사이 좁은 골목
            float zc = Random.Range(0f, 3f);
            while (zc < L - 6f)
            {
                float width = Mathf.Min(Random.Range(10f, 22f), L - zc - 1f);
                if (width < 6f) break;
                float depth = Random.Range(14f, 26f);
                float height = Random.value < 0.15f ? Random.Range(110f, 160f) : Random.Range(28f, 95f);
                float inner = buildingX + Random.Range(0f, 3f);
                Building(side, inner, inner + depth, zc, zc + width, roadY + 0.4f, roadY + height, true);
                zc += width + Random.Range(2f, 5f);
            }

            // 뒷줄 고층 빌딩: 지평선 스카이라인 (창문은 앞면만)
            zc = Random.Range(0f, 8f);
            while (zc < L - 8f)
            {
                float width = Mathf.Min(Random.Range(14f, 26f), L - zc - 1f);
                if (width < 8f) break;
                float inner = buildingX + 34f + Random.Range(0f, 10f);
                float height = Random.Range(90f, 210f);
                Building(side, inner, inner + Random.Range(18f, 30f), zc, zc + width, roadY, roadY + height, false);
                zc += width + Random.Range(6f, 14f);
            }
        }

        AddMesh(seg, "Road", road, roadMat);
        AddMesh(seg, "Lines", lines, lineMat);
        AddMesh(seg, "CenterLine", center, centerLineMat);
        AddMesh(seg, "Sidewalk", walk, sidewalkMat);
        AddMeshes(seg, "Buildings", walls, buildingMats);
        AddMesh(seg, "Windows", dark, windowMat);
        AddMesh(seg, "LitWindows", lit, litWindowMat);
        AddMesh(seg, "Roof", roof, roofMat);
        AddMesh(seg, "Lamps", lamp, lampMat);
        AddMesh(seg, "Trim", trim, trimMat != null ? trimMat : roofMat);
        AddMesh(seg, "Storefronts", store, storefrontMat != null ? storefrontMat : litWindowMat);
        AddMesh(seg, "WaterTanks", tank, tankMat != null ? tankMat : roofMat);
        AddMeshes(seg, "Awnings", awnings, awningMats);
        AddMeshes(seg, "Neon", neons, neonMats);
        Random.state = saved;
        return seg;
    }

    // 빌딩 하나. side: -1 = 왼쪽(차도 쪽 면이 +x를 봄), 1 = 오른쪽. inner/outer: 차도에서 가까운/먼 |x|. street: 길가 첫 줄 (1층 상가·광고판)
    // 높은 빌딩은 위쪽을 들여 한 번 더 쌓음(계단식). 스타일: 0 = 벽돌·콘크리트(층마다 띠), 1 = 유리 빌딩(세로 줄무늬 + 큰 유리창)
    void Building(int side, float inner, float outer, float z0, float z1, float y0, float y1, bool street)
    {
        int style = Random.value < 0.3f ? 1 : 0;
        int matCount = walls.Length;
        // 유리 빌딩은 유리 외벽(3번째 머티리얼), 나머지는 유리를 뺀 아무 색
        int mat = style == 1 && matCount > 2 ? 2 : Random.Range(0, matCount);
        if (style == 0 && matCount > 2 && mat == 2) mat = 0;
        MeshKit wall = walls[mat];

        bool setback = y1 - y0 > 70f && Random.value < 0.6f;
        float split = setback ? y0 + (y1 - y0) * Random.Range(0.55f, 0.75f) : y1;
        Block(wall, side, inner, outer, z0, z1, y0, split, style, street, !setback);
        if (setback)
        {
            float inset = Random.Range(2.5f, 4.5f);
            if (outer - inner - inset * 2f > 6f && z1 - z0 - inset * 2f > 5f)
                Block(wall, side, inner + inset, outer - inset, z0 + inset, z1 - inset, split, y1, style, false, true);
            else Roof(side, inner, outer, z0, z1, split, style, street);
        }
    }

    // 상자 한 덩어리 + 창문·띠 + 꼭대기 장식띠 (+ 1층 상가) (+ 맨 위면 옥상 설비)
    void Block(MeshKit wall, int side, float inner, float outer, float z0, float z1, float y0, float y1, int style, bool street, bool top)
    {
        float xa = side * inner, xb = side * outer;
        float x0 = Mathf.Min(xa, xb), x1 = Mathf.Max(xa, xb);
        Box(wall, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));

        bool shops = street && y0 < roadY + 1f;
        float winStart = shops ? y0 + 5f : y0 + 1.5f;
        float litChance = Random.Range(0.2f, 0.5f);
        float floorH = style == 1 ? 3.4f : 3.6f;
        float colW = style == 1 ? 3.0f : 3.2f;
        float winW = style == 1 ? 2.6f : 1.6f, winH = style == 1 ? 3.0f : 2.0f;
        if (style == 1) litChance *= 0.6f;
        Vector3 streetOut = new Vector3(-side, 0f, 0f);

        // 차도 쪽 면: 창문 + (벽돌) 층 띠 / (유리) 세로 줄
        float fx = xa - side * 0.06f;
        for (float y = winStart; y + winH < y1 - 1.5f; y += floorH)
        {
            for (float z = z0 + 1.4f; z + winW < z1 - 0.8f; z += colW)
                Quad(Random.value < litChance ? lit : dark,
                     new Vector3(fx, y, z), new Vector3(fx, y, z + winW), new Vector3(fx, y + winH, z + winW), new Vector3(fx, y + winH, z), streetOut);
            if (style == 0) Box(trim, Order(new Vector3(xa, y - 0.45f, z0), new Vector3(xa - side * 0.22f, y - 0.25f, z1)));
        }
        // 앞쪽 면 (z0, 새를 마주 봄)
        float fz = z0 - 0.06f;
        for (float y = winStart; y + winH < y1 - 1.5f; y += floorH)
        {
            for (float x = x0 + 1.4f; x + winW < x1 - 0.8f; x += colW)
                Quad(Random.value < litChance ? lit : dark,
                     new Vector3(x, y, fz), new Vector3(x + winW, y, fz), new Vector3(x + winW, y + winH, fz), new Vector3(x, y + winH, fz), Vector3.back);
            if (style == 0) Box(trim, new Vector3(x0, y - 0.45f, z0 - 0.22f), new Vector3(x1, y - 0.25f, z0));
        }
        if (style == 1)
        {
            // 유리 빌딩: 창문 사이마다 세로 줄 (꼭대기까지)
            for (float z = z0 + 1.4f - (colW - winW) * 0.5f; z < z1 - 0.5f; z += colW)
                Box(trim, Order(new Vector3(xa, winStart - 0.5f, z - 0.12f), new Vector3(xa - side * 0.16f, y1 - 0.8f, z + 0.12f)));
            for (float x = x0 + 1.4f - (colW - winW) * 0.5f; x < x1 - 0.5f; x += colW)
                Box(trim, new Vector3(x - 0.12f, winStart - 0.5f, z0 - 0.16f), new Vector3(x + 0.12f, y1 - 0.8f, z0));
        }

        // 꼭대기 장식띠 (바깥으로 살짝 튀어나온 테두리)
        Box(trim, new Vector3(x0 - 0.35f, y1 - 0.7f, z0 - 0.35f), new Vector3(x1 + 0.35f, y1 + 0.5f, z1 + 0.35f));

        if (shops) Storefronts(side, xa, z0, z1, y0);
        if (top) Roof(side, inner, outer, z0, z1, y1 + 0.5f, style, street);
    }

    // 1층 상가: 차도 쪽에 밝은 유리 진열창 + 가게마다 색이 다른 비스듬한 차양 + 간판 띠
    void Storefronts(int side, float xa, float z0, float z1, float y0)
    {
        float fx = xa - side * 0.08f;
        Quad(store, new Vector3(fx, y0 + 0.4f, z0 + 0.5f), new Vector3(fx, y0 + 0.4f, z1 - 0.5f), new Vector3(fx, y0 + 3.3f, z1 - 0.5f), new Vector3(fx, y0 + 3.3f, z0 + 0.5f), new Vector3(-side, 0f, 0f));
        float shopW = Random.Range(5.5f, 8f);
        for (float z = z0 + 0.6f; z + 3f < z1 - 0.4f; z += shopW)
        {
            float zEnd = Mathf.Min(z + shopW - 0.6f, z1 - 0.6f);
            // 차양 단면 (x, y): 벽에서 차도 쪽으로 1.6m 나오며 0.7m 내려감
            float top = y0 + 4.1f, outX = xa - side * 1.6f;
            Vector3[] poly =
            {
                new Vector3(xa, top, 0f), new Vector3(outX, top - 0.7f, 0f), new Vector3(outX, top - 1.0f, 0f), new Vector3(xa, top - 0.25f, 0f),
            };
            for (int i = 0; i < poly.Length; i++) poly[i].z = (z + zEnd) * 0.5f;
            awnings[Random.Range(0, awnings.Length)].Prism(poly, Vector3.forward, zEnd - z);
            // 간판 띠 (차양 위)
            Box(neons[Random.Range(0, neons.Length)], Order(new Vector3(xa, top + 0.2f, z + 0.6f), new Vector3(xa - side * 0.15f, top + 0.9f, zEnd - 0.6f)));
        }
    }

    // 옥상: 실외기 상자 + (가끔) 안테나 / 물탱크 / 길가 빌딩은 새 쪽을 보는 네온 광고판
    void Roof(int side, float inner, float outer, float z0, float z1, float y, int style, bool street)
    {
        float x0 = Mathf.Min(side * inner, side * outer), x1 = Mathf.Max(side * inner, side * outer);
        int units = Random.Range(1, 4);
        for (int i = 0; i < units; i++)
        {
            float ux = Random.Range(x0 + 2f, x1 - 4f), uz = Random.Range(z0 + 2f, z1 - 4f);
            Box(roof, new Vector3(ux, y, uz), new Vector3(ux + Random.Range(1.5f, 3.5f), y + Random.Range(1f, 3f), uz + Random.Range(1.5f, 3.5f)));
        }
        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
        float r = Random.value;
        if (street && r < 0.3f)
        {
            // 네온 광고판: 기둥 두 개 + 앞(새 쪽)을 보는 빛나는 판 + 테두리
            float bw = Mathf.Min(x1 - x0 - 2f, 12f), bz = z0 + 2f;
            for (int s = -1; s <= 1; s += 2) Box(roof, new Vector3(cx + s * bw * 0.4f - 0.2f, y, bz - 0.2f), new Vector3(cx + s * bw * 0.4f + 0.2f, y + 3f, bz + 0.2f));
            Box(roof, new Vector3(cx - bw * 0.5f - 0.3f, y + 2.7f, bz - 0.1f), new Vector3(cx + bw * 0.5f + 0.3f, y + 7.8f, bz + 0.3f));
            Box(neons[Random.Range(0, neons.Length)], new Vector3(cx - bw * 0.5f, y + 3f, bz - 0.2f), new Vector3(cx + bw * 0.5f, y + 7.5f, bz));
        }
        else if (style == 0 && r < 0.65f)
        {
            // 물탱크: 다리 4개 + 팔각 통 + 뾰족 지붕
            float tx = Random.Range(x0 + 4f, x1 - 4f), tz = Random.Range(z0 + 4f, z1 - 4f);
            for (int a = 0; a < 4; a++)
            {
                float lx = tx + (a % 2 == 0 ? -1.1f : 1.1f), lz = tz + (a < 2 ? -1.1f : 1.1f);
                Box(roof, new Vector3(lx - 0.12f, y, lz - 0.12f), new Vector3(lx + 0.12f, y + 3f, lz + 0.12f));
            }
            var oct = new Vector3[8];
            for (int k = 0; k < 8; k++) oct[k] = new Vector3(tx + Mathf.Cos(k * Mathf.PI / 4f) * 1.7f, y + 4.6f, tz + Mathf.Sin(k * Mathf.PI / 4f) * 1.7f);
            tank.Prism(oct, Vector3.up, 3.2f);
            IceCaveTunnel.Cone(tank, new Vector3(tx, y + 6.2f, tz), Vector3.up, 1.3f, 1.85f, 8);
        }
        else if (r < 0.85f)
        {
            // 안테나
            Box(roof, new Vector3(cx - 0.15f, y, cz - 0.15f), new Vector3(cx + 0.15f, y + Random.Range(6f, 14f), cz + 0.15f));
        }
    }

    // 두 꼭짓점을 min/max 순서로 (왼쪽·오른쪽 빌딩에서 차도 쪽으로 내미는 방향이 달라서)
    static Vector3[] Order(Vector3 a, Vector3 b)
    {
        return new[] { Vector3.Min(a, b), Vector3.Max(a, b) };
    }

    static void Box(MeshKit kit, Vector3[] minMax) { Box(kit, minMax[0], minMax[1]); }

    // ───────────────────────── 도우미 ─────────────────────────

    static void Quad(MeshKit kit, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        kit.Tri(a, b, c, outward);
        kit.Tri(a, c, d, outward);
    }

    static void FlatRect(MeshKit kit, float x0, float x1, float z0, float z1, float y)
    {
        Quad(kit, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.up);
    }

    // 상자 (밑면 없음)
    public static void Box(MeshKit kit, Vector3 min, Vector3 max)
    {
        Vector3 a = new Vector3(min.x, min.y, min.z), b = new Vector3(max.x, min.y, min.z), c = new Vector3(max.x, min.y, max.z), d = new Vector3(min.x, min.y, max.z);
        Vector3 e = new Vector3(min.x, max.y, min.z), f = new Vector3(max.x, max.y, min.z), g = new Vector3(max.x, max.y, max.z), h = new Vector3(min.x, max.y, max.z);
        Quad(kit, e, f, g, h, Vector3.up);
        Quad(kit, a, b, f, e, Vector3.back);
        Quad(kit, c, d, h, g, Vector3.forward);
        Quad(kit, d, a, e, h, Vector3.left);
        Quad(kit, b, c, g, f, Vector3.right);
    }

    // 머티리얼 수만큼 빈 메쉬 묶음 (머티리얼이 없으면 하나)
    static MeshKit[] Kits(Material[] mats)
    {
        int n = mats != null && mats.Length > 0 ? mats.Length : 1;
        var kits = new MeshKit[n];
        for (int i = 0; i < n; i++) kits[i] = new MeshKit();
        return kits;
    }

    void AddMeshes(Transform parent, string name, MeshKit[] kits, Material[] mats)
    {
        for (int i = 0; i < kits.Length; i++) AddMesh(parent, name + i, kits[i], mats != null && mats.Length > i ? mats[i] : roofMat);
    }

    static void AddMesh(Transform parent, string name, MeshKit kit, Material mat)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        Mesh mesh = kit.ToMesh("City" + name);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
