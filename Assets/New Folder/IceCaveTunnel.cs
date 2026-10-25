using UnityEngine;
using System.Collections.Generic;

// 🧊 얼음 동굴 배경: 새가 날아가는 길을 감싸는 터널 (바닥·천장·양쪽 벽 + 매달린 고드름 + 빛나는 얼음 결정)
// 세상에 고정된 조각(40m)들을 이어 붙이고, 새 뒤로 한참 지나간 조각은 앞쪽으로 옮겨서 끝없이 이어지게 함
// → 배경이 새를 따라다니지 않아 날아가는 속도감이 남. 충돌 없음 (보기만)
// 새가 다니는 공간(x ±34.5, y -5 ~ 17)은 비워 둠: 천장 고드름 끝은 20 위, 바닥 결정 꼭대기는 -6 아래
public class IceCaveTunnel : MonoBehaviour
{
    public Transform bird;
    public Material rockMat;      // 동굴 벽 (짙은 푸른 얼음 바위)
    public Material iceMat;       // 고드름·얼음 덩어리 (밝고 매끈)
    public Material crystalMat;   // 빛나는 결정 (발광)

    public float segmentLength = 40f;
    public int segmentCount = 14;          // 뒤 160m + 앞 400m 정도
    public float behind = 160f;
    public float floorY = -14f, ceilingY = 34f, wallX = 46f;

    private readonly List<Transform> segments = new List<Transform>();
    private float endZ = float.MaxValue;   // 탈출구: 이 앞으로는 터널 없음

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
            seg.gameObject.SetActive(seg.position.z < endZ);
        }
    }

    // 탈출구 위치를 정함: 그 앞 조각은 안 보이게 (조각 경계에 맞춰서 돌려줌)
    public float SetExit(float minZ)
    {
        endZ = Mathf.Ceil(minZ / segmentLength) * segmentLength;
        return endZ;
    }

    // ───────────────────────── 조각 만들기 ─────────────────────────

    Transform BuildSegment(int seed)
    {
        Random.State saved = Random.state;
        Random.InitState(7000 + seed * 31);
        Transform seg = new GameObject("IceCaveSegment").transform;
        seg.SetParent(transform, false);

        var rock = new MeshKit();
        var ice = new MeshKit();
        var glow = new MeshKit();
        float L = segmentLength;

        // 바닥: 울퉁불퉁 (가운데는 낮게, 벽 쪽으로 올라감)
        Surface(rock, -wallX - 4f, wallX + 4f, 14, 8, (x, z) => floorY + Mathf.Pow(Mathf.Abs(x) / wallX, 3f) * 10f + Bump(x, z, 1.6f), Vector3.up);
        // 천장: 아래로 울퉁불퉁 처짐
        Surface(rock, -wallX - 4f, wallX + 4f, 14, 8, (x, z) => ceilingY - Mathf.Pow(Mathf.Abs(x) / wallX, 3f) * 8f - Bump(x, z, 2.4f, 100f), Vector3.down);
        // 양쪽 벽: 들쭉날쭉한 세로 면
        for (int side = -1; side <= 1; side += 2)
        {
            int sd = side;
            WallSurface(rock, sd, floorY - 2f, ceilingY + 2f, 8, 8, (y, z) => sd * (wallX - Bump(y * 2f, z, 3f, 50f * sd)));
        }

        // 천장 고드름: 끝이 새가 다니는 공간에 안 들어오게 (y 20 위)
        for (int i = 0; i < 14; i++)
        {
            float x = Random.Range(-wallX + 3f, wallX - 3f);
            float z = Random.Range(2f, L - 2f);
            float top = ceilingY - Mathf.Pow(Mathf.Abs(x) / wallX, 3f) * 8f + 1f;
            float len = Mathf.Min(Random.Range(3f, 11f), top - 21f);
            Cone(ice, new Vector3(x, top, z), Vector3.down, len, Random.Range(0.5f, 1.3f), 6);
            if (Random.value < 0.5f) Cone(ice, new Vector3(x + Random.Range(-1.5f, 1.5f), top, z + Random.Range(-1.5f, 1.5f)), Vector3.down, len * 0.5f, 0.4f, 5);
        }
        // 바닥 결정 덩어리 (빛남): 꼭대기가 -6 아래
        for (int i = 0; i < 7; i++)
        {
            float x = Random.Range(-wallX + 4f, wallX - 4f);
            float z = Random.Range(3f, L - 3f);
            float baseY = floorY + Mathf.Pow(Mathf.Abs(x) / wallX, 3f) * 10f - 0.5f;
            int n = Random.Range(3, 6);
            for (int k = 0; k < n; k++)
            {
                Vector3 dir = Quaternion.Euler(Random.Range(-35f, 35f), Random.Range(0f, 360f), 0f) * Vector3.up;
                float len = Mathf.Min(Random.Range(2f, 6f), (-6.5f - baseY) / Mathf.Max(0.3f, dir.y));
                Crystal(k == 0 ? glow : (Random.value < 0.5f ? glow : ice), new Vector3(x + Random.Range(-1f, 1f), baseY, z + Random.Range(-1f, 1f)), dir, len, Random.Range(0.4f, 0.9f));
            }
        }
        // 벽에서 안쪽으로 튀어나온 결정
        for (int i = 0; i < 6; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float y = Random.Range(floorY + 4f, ceilingY - 6f);
            float z = Random.Range(3f, L - 3f);
            Vector3 dir = Quaternion.Euler(Random.Range(-30f, 30f), Random.Range(-30f, 30f), 0f) * new Vector3(-side, 0.3f, 0f);
            // 뿌리를 벽 속 깊이 박아서 울퉁불퉁한 벽면 어디서든 벽에서 자라난 것처럼 보이게
            float len = Mathf.Min(Random.Range(2f, 5f), wallX - 38f) + 4f;
            Crystal(Random.value < 0.6f ? glow : ice, new Vector3(side * (wallX + 3f), y, z), dir.normalized, len, Random.Range(0.4f, 0.8f));
        }

        AddMesh(seg, "Rock", rock, rockMat);
        AddMesh(seg, "Ice", ice, iceMat);
        AddMesh(seg, "Glow", glow, crystalMat);
        Random.state = saved;
        return seg;
    }

    // 조각 끝(z = 0, L)에서는 높이 흔들림이 0이 되게 → 어느 조각끼리 붙여도 이음새가 맞음
    // z: 조각 안 앞뒤 위치(0 ~ L), phase: 면마다 무늬를 다르게
    float Bump(float a, float z, float amp, float phase = 0f)
    {
        float edge = Mathf.Clamp01(Mathf.Min(z, segmentLength - z) / 6f);
        float zz = z + phase;
        float n = Mathf.Sin(a * 0.21f + zz * 0.37f) * 0.5f + Mathf.Sin(a * 0.53f - zz * 0.71f) * 0.3f + (Mathf.PerlinNoise(a * 0.11f + 3.3f, zz * 0.13f) - 0.5f);
        return n * amp * Mathf.SmoothStep(0f, 1f, edge);
    }

    // 가로(x) × 앞뒤(z) 격자 면. height(x, z)
    void Surface(MeshKit kit, float x0, float x1, int nx, int nz, System.Func<float, float, float> height, Vector3 face)
    {
        float L = segmentLength;
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                float ax = Mathf.Lerp(x0, x1, ix / (float)nx), bx = Mathf.Lerp(x0, x1, (ix + 1) / (float)nx);
                float az = L * iz / nz, bz = L * (iz + 1) / nz;
                Vector3 a = new Vector3(ax, height(ax, az), az), b = new Vector3(bx, height(bx, az), az);
                Vector3 c = new Vector3(ax, height(ax, bz), bz), d = new Vector3(bx, height(bx, bz), bz);
                kit.Tri(a, c, b, face); kit.Tri(b, c, d, face);
            }
    }

    // 세로(y) × 앞뒤(z) 격자 면. x(y, z). 안쪽(새 쪽)을 봄
    void WallSurface(MeshKit kit, int side, float y0, float y1, int ny, int nz, System.Func<float, float, float> xAt)
    {
        float L = segmentLength;
        Vector3 face = new Vector3(-side, 0f, 0f);
        for (int iy = 0; iy < ny; iy++)
            for (int iz = 0; iz < nz; iz++)
            {
                float ay = Mathf.Lerp(y0, y1, iy / (float)ny), by = Mathf.Lerp(y0, y1, (iy + 1) / (float)ny);
                float az = L * iz / nz, bz = L * (iz + 1) / nz;
                Vector3 a = new Vector3(xAt(ay, az), ay, az), b = new Vector3(xAt(by, az), by, az);
                Vector3 c = new Vector3(xAt(ay, bz), ay, bz), d = new Vector3(xAt(by, bz), by, bz);
                kit.Tri(a, c, b, face); kit.Tri(b, c, d, face);
            }
    }

    // 뿔 (고드름): baseCenter에서 dir 방향으로 뾰족하게
    public static void Cone(MeshKit kit, Vector3 baseCenter, Vector3 dir, float length, float radius, int sides)
    {
        Vector3 right = Vector3.Cross(dir, Vector3.forward);
        if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(dir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, dir).normalized;
        Vector3 tip = baseCenter + dir * length;
        float twist = Random.Range(0f, Mathf.PI);
        for (int k = 0; k < sides; k++)
        {
            float a0 = twist + Mathf.PI * 2f * k / sides, a1 = twist + Mathf.PI * 2f * (k + 1) / sides;
            Vector3 o0 = baseCenter + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
            Vector3 o1 = baseCenter + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
            kit.Tri(o0, o1, tip, (o0 + o1) * 0.5f - baseCenter);
            kit.Tri(o0, o1, baseCenter, -dir);
        }
    }

    // 육각 결정: 기둥 + 뾰족한 끝
    public static void Crystal(MeshKit kit, Vector3 basePos, Vector3 dir, float length, float radius)
    {
        Vector3 right = Vector3.Cross(dir, Vector3.forward);
        if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(dir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, dir).normalized;
        const int sides = 6;
        Vector3 top = basePos + dir * length * 0.75f;
        Vector3 tip = basePos + dir * length;
        for (int k = 0; k < sides; k++)
        {
            float a0 = Mathf.PI * 2f * k / sides, a1 = Mathf.PI * 2f * (k + 1) / sides;
            Vector3 o0 = (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
            Vector3 o1 = (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
            Vector3 outDir = (o0 + o1) * 0.5f;
            kit.Tri(basePos + o0, basePos + o1, top + o1, outDir);
            kit.Tri(basePos + o0, top + o1, top + o0, outDir);
            kit.Tri(top + o0, top + o1, tip, outDir + dir * radius);
        }
    }

    static void AddMesh(Transform parent, string name, MeshKit kit, Material mat)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("IceCave" + name);
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
