using UnityEngine;
using System.Collections.Generic;

// 🦅 사막 보스 독수리: 로우폴리 모양을 코드로 만들고, 뼈대(빈 오브젝트)를 돌려서 자연스럽게 움직임
// 앞(부리) = +z, 위 = +y, 오른쪽 날개 = +x
//
// 움직임은 EagleChase가 아래 값들만 바꾸면 알아서 섞여서 표현됨
//   flapRate/flapAmp : 날갯짓 빠르기/크기 (내려치기는 빠르고 올리기는 느림, 올릴 땐 팔꿈치를 접음)
//   glide  : 0 날갯짓 ↔ 1 날개 펴고 활공
//   tuck   : 1이면 날개를 뒤로 접고 급강하 자세
//   spread : 1이면 날개를 위로 활짝 펴고 위협 (울부짖기)
//   talons : 1이면 발톱을 앞으로 내밂
//   beakOpen, lookTarget(머리가 바라볼 곳), tailFan
public class EagleModel : MonoBehaviour
{
    public float flapRate = 1.6f;
    public float flapAmp = 38f;
    [Range(0f, 1f)] public float glide;
    [Range(0f, 1f)] public float tuck;
    [Range(0f, 1f)] public float spread;
    [Range(0f, 1f)] public float talons;
    [Range(0f, 1f)] public float beakOpen;
    [Range(0f, 1f)] public float tailFan = 0.5f;
    [System.NonSerialized] public Vector3? lookTarget; // 실행 중에 EagleChase가 정함

    private Transform body, head, jaw, tail;
    private Transform[] shoulder = new Transform[2], elbow = new Transform[2];
    private readonly List<Transform>[] primaries = { new List<Transform>(), new List<Transform>() };
    private readonly List<Transform> tailFeathers = new List<Transform>();
    private Transform[] legs = new Transform[2];
    private float phase;
    private float burst;          // 한 번 세게 내려치기 (깃털 발사 등)
    private Quaternion headRot = Quaternion.identity;

    const int PrimaryCount = 5;
    const int TailCount = 7;

    // ───────────────────────── 만들기 ─────────────────────────

    public void Build(Material bodyMat, Material wingMat, Material covertMat, Material primaryMat, Material whiteMat,
                      Material beakMat, Material eyeMat, Material pupilMat, Material clawMat)
    {
        body = Part("Body", transform, Vector3.zero, BodyMesh(), bodyMat);

        // 머리 (흰 머리 + 노란 갈고리 부리 + 날카로운 눈매)
        head = new GameObject("Head").transform;
        head.SetParent(transform, false);
        head.localPosition = new Vector3(0f, 0.28f, 1.05f);
        Part("HeadMesh", head, Vector3.zero, HeadMesh(), whiteMat);
        Part("Beak", head, Vector3.zero, BeakMesh(), beakMat);
        jaw = new GameObject("Jaw").transform;
        jaw.SetParent(head, false);
        jaw.localPosition = new Vector3(0f, 0.02f, 0.78f);
        Part("JawMesh", jaw, Vector3.zero, JawMesh(), beakMat);
        for (int s = -1; s <= 1; s += 2)
        {
            Part("Eye", head, new Vector3(s * 0.2f, 0.2f, 0.62f), Ellipsoid(new Vector3(0.075f, 0.075f, 0.06f), 7, 4), eyeMat);
            Part("Pupil", head, new Vector3(s * 0.25f, 0.2f, 0.65f), Ellipsoid(new Vector3(0.03f, 0.045f, 0.03f), 5, 3), pupilMat);
            // 눈썹뼈: 눈 위로 튀어나와 화난 눈매
            Transform brow = Part("Brow", head, new Vector3(s * 0.2f, 0.29f, 0.64f), BoxMesh(new Vector3(0.16f, 0.05f, 0.2f)), whiteMat);
            brow.localRotation = Quaternion.Euler(10f, s * -12f, s * 18f);
        }

        // 날개: 0 = 오른쪽(+x), 1 = 왼쪽(-x, 오른쪽을 거울로)
        for (int side = 0; side < 2; side++)
        {
            float sx = side == 0 ? 1f : -1f;
            bool mirror = side == 1;
            shoulder[side] = new GameObject(side == 0 ? "Shoulder_R" : "Shoulder_L").transform;
            shoulder[side].SetParent(transform, false);
            shoulder[side].localPosition = new Vector3(sx * 0.4f, 0.22f, 0.35f);
            Part("Arm", shoulder[side], Vector3.zero, Mirror(ArmMesh(), mirror), wingMat);
            Part("Coverts", shoulder[side], Vector3.zero, Mirror(CovertMesh(), mirror), covertMat);

            elbow[side] = new GameObject("Elbow").transform;
            elbow[side].SetParent(shoulder[side], false);
            elbow[side].localPosition = new Vector3(sx * 2.2f, 0f, 0.05f);
            Part("Hand", elbow[side], Vector3.zero, Mirror(HandMesh(), mirror), wingMat);

            // 날개 끝 손가락 깃털 (독수리 특유의 벌어진 날개 끝)
            Mesh feather = Mirror(FeatherMesh(1.35f, 0.26f), mirror);
            for (int i = 0; i < PrimaryCount; i++)
            {
                float k = i / (float)(PrimaryCount - 1);
                Transform p = new GameObject("Primary" + i).transform;
                p.SetParent(elbow[side], false);
                p.localPosition = new Vector3(sx * Mathf.Lerp(1.75f, 1.35f, k), 0f, Mathf.Lerp(0.05f, -0.75f, k));
                Part("Feather", p, Vector3.zero, feather, primaryMat);
                primaries[side].Add(p);
            }
        }

        // 꼬리: 흰 깃털 부채
        tail = new GameObject("Tail").transform;
        tail.SetParent(transform, false);
        tail.localPosition = new Vector3(0f, 0.05f, -1.15f);
        Mesh tailFeather = TailFeatherMesh();
        for (int i = 0; i < TailCount; i++)
        {
            Transform f = new GameObject("TailFeather" + i).transform;
            f.SetParent(tail, false);
            f.localPosition = new Vector3(0f, (i % 2) * 0.015f, 0f);
            Part("Feather", f, Vector3.zero, tailFeather, whiteMat);
            tailFeathers.Add(f);
        }

        // 다리 + 발톱 (평소엔 뒤로 접어 넣음)
        for (int side = 0; side < 2; side++)
        {
            float sx = side == 0 ? 1f : -1f;
            legs[side] = new GameObject(side == 0 ? "Leg_R" : "Leg_L").transform;
            legs[side].SetParent(transform, false);
            legs[side].localPosition = new Vector3(sx * 0.22f, -0.32f, 0.1f);
            Part("Thigh", legs[side], new Vector3(0f, -0.12f, 0f), Ellipsoid(new Vector3(0.17f, 0.2f, 0.17f), 6, 4), bodyMat);
            Part("Shin", legs[side], new Vector3(0f, -0.42f, 0f), BoxMesh(new Vector3(0.09f, 0.42f, 0.09f)), beakMat);
            for (int c = 0; c < 4; c++)
            {
                // 앞발톱 3개 + 뒷발톱 1개
                float yaw = c < 3 ? (c - 1) * 28f : 180f;
                Transform toe = new GameObject("Toe").transform;
                toe.SetParent(legs[side], false);
                toe.localPosition = new Vector3(0f, -0.62f, 0f);
                toe.localRotation = Quaternion.Euler(0f, yaw, 0f);
                Part("ToeMesh", toe, new Vector3(0f, 0f, 0.12f), BoxMesh(new Vector3(0.06f, 0.06f, 0.24f)), beakMat);
                Transform claw = Part("Claw", toe, new Vector3(0f, -0.04f, 0.27f), ClawMesh(), clawMat);
                claw.localRotation = Quaternion.Euler(30f, 0f, 0f);
            }
        }
    }

    // 한 번 세게 날개를 내려침 (깃털 발사, 급제동 등)
    public void Burst() { burst = 1f; }

    // ───────────────────────── 움직이기 ─────────────────────────

    void LateUpdate()
    {
        if (body == null) return;
        float dt = Time.deltaTime;
        burst = Mathf.MoveTowards(burst, 0f, dt * 2.5f);

        float rate = flapRate * (1f + burst * 1.2f) * (1f - tuck * 0.8f);
        phase += dt * rate;
        float u = Mathf.Repeat(phase, 1f);
        // 내려치기(앞 45%)는 빠르게, 올리기(뒤 55%)는 천천히. stroke: +1 = 날개 위, -1 = 아래
        float stroke = u < 0.45f ? Mathf.Cos(Mathf.PI * u / 0.45f) : -Mathf.Cos(Mathf.PI * (u - 0.45f) / 0.55f);
        float upstroke = u < 0.45f ? 0f : Mathf.Sin(Mathf.PI * (u - 0.45f) / 0.55f);   // 올리는 동안 0→1→0
        float downstroke = u < 0.45f ? Mathf.Sin(Mathf.PI * u / 0.45f) : 0f;

        // 활공할 땐 날갯짓 대신 살짝 들어 올린 채 바람에 흔들림
        float glideWobble = Mathf.Sin(Time.time * 1.7f) * 4f;
        float amp = flapAmp * (1f + burst * 0.5f);
        float roll = Mathf.Lerp(stroke * amp + 6f, 12f + glideWobble, glide);
        float sweep = Mathf.Lerp(-upstroke * 14f + downstroke * 6f, 0f, glide);
        float twist = Mathf.Lerp(downstroke * 8f - upstroke * 6f, 2f, glide);
        float elbowRoll = Mathf.Lerp(-upstroke * 22f + stroke * 8f, -4f, glide);
        float elbowFold = Mathf.Lerp(upstroke * 38f, 0f, glide);
        float fan = Mathf.Lerp(0.55f + downstroke * 0.45f, 0.85f, glide);

        // 울부짖기: 날개를 위·앞으로 활짝
        roll = Mathf.Lerp(roll, 38f + Mathf.Sin(Time.time * 9f) * 3f, spread);
        sweep = Mathf.Lerp(sweep, 14f, spread);
        elbowRoll = Mathf.Lerp(elbowRoll, -10f, spread);
        elbowFold = Mathf.Lerp(elbowFold, -6f, spread);
        fan = Mathf.Lerp(fan, 1.15f, spread);

        // 급강하: 날개를 뒤로 접어 화살촉 모양
        roll = Mathf.Lerp(roll, 6f, tuck);
        sweep = Mathf.Lerp(sweep, -58f, tuck);
        twist = Mathf.Lerp(twist, 6f, tuck);
        elbowRoll = Mathf.Lerp(elbowRoll, -8f, tuck);
        elbowFold = Mathf.Lerp(elbowFold, 72f, tuck);
        fan = Mathf.Lerp(fan, 0.15f, tuck);

        for (int side = 0; side < 2; side++)
        {
            float s = side == 0 ? 1f : -1f;
            shoulder[side].localRotation = Quaternion.Euler(twist, s * -sweep, s * roll);
            elbow[side].localRotation = Quaternion.Euler(0f, s * elbowFold, s * elbowRoll);
            for (int i = 0; i < primaries[side].Count; i++)
            {
                float k = i / (float)(PrimaryCount - 1);
                // 손가락 깃털이 부채처럼 벌어짐 + 끝이 살짝 위로 휨 + 바람에 파르르
                float flutter = Mathf.Sin(Time.time * 23f + i * 1.7f + side) * 2.5f * (1f - tuck);
                float yaw = Mathf.Lerp(4f, 52f, k) * fan;
                float lift = 6f + k * 4f + downstroke * -10f * (1f - glide) + flutter;
                primaries[side][i].localRotation = Quaternion.Euler(0f, s * yaw, s * lift);
            }
        }

        // 몸: 내려칠 때 살짝 떠오르고(반동), 앞뒤로 끄덕
        float bob = Mathf.Lerp(-stroke * 0.09f * amp / 38f, 0f, Mathf.Max(glide, tuck));
        body.localPosition = new Vector3(0f, bob, 0f);
        body.localRotation = Quaternion.Euler(Mathf.Lerp(downstroke * 3f, 0f, glide), 0f, 0f);

        // 꼬리: 부채 펴짐/접힘, 날갯짓에 맞춰 살짝 까딱
        float tailSpread = Mathf.Lerp(Mathf.Lerp(0.35f, 1.1f, tailFan), 0.1f, tuck);
        tail.localRotation = Quaternion.Euler(-stroke * 3f * (1f - glide) + spread * -15f, 0f, 0f);
        for (int i = 0; i < tailFeathers.Count; i++)
        {
            float k = i / (float)(TailCount - 1) * 2f - 1f;
            tailFeathers[i].localRotation = Quaternion.Euler(Mathf.Abs(k) * -3f, k * 32f * tailSpread, 0f);
        }

        // 다리: 평소엔 뒤로 접고, 발톱 공격 땐 앞으로
        for (int side = 0; side < 2; side++)
            legs[side].localRotation = Quaternion.Euler(Mathf.Lerp(100f, -55f, talons), 0f, (side == 0 ? 1f : -1f) * talons * 8f);

        // 머리: 몸이 흔들려도 목표를 계속 노려봄 (울부짖을 땐 하늘로 고개를 젖힘)
        Quaternion want = Quaternion.identity;
        if (lookTarget.HasValue)
        {
            Vector3 d = transform.InverseTransformDirection(lookTarget.Value - head.position);
            float yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -75f, 75f);
            float pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -45f, 50f);
            want = Quaternion.Euler(pitch, yaw, 0f);
        }
        want = Quaternion.Slerp(want, Quaternion.Euler(-35f, 0f, 0f), spread);
        headRot = Quaternion.Slerp(headRot, want, 1f - Mathf.Exp(-dt * 10f));
        head.localRotation = headRot;
        jaw.localRotation = Quaternion.Euler(Mathf.Max(beakOpen, spread) * 28f, 0f, 0f);
    }

    // ───────────────────────── 모양 (로우폴리 메쉬) ─────────────────────────

    // 몸통: 가슴은 두툼하고 꼬리 쪽으로 가늘어지는 타원 단면들을 이어 붙임
    static Mesh BodyMesh()
    {
        var b = new MeshKit();
        b.Loft(new[] { new Vector3(0, 0.05f, -1.25f), new Vector3(0, 0.04f, -0.85f), new Vector3(0, 0f, -0.3f), new Vector3(0, 0f, 0.3f), new Vector3(0, 0.07f, 0.8f), new Vector3(0, 0.2f, 1.12f) },
               new[] { 0.16f, 0.36f, 0.52f, 0.56f, 0.46f, 0.3f },
               new[] { 0.12f, 0.3f, 0.46f, 0.5f, 0.44f, 0.3f }, 8);
        return b.ToMesh("EagleBody");
    }

    static Mesh HeadMesh()
    {
        var b = new MeshKit();
        b.Loft(new[] { new Vector3(0, -0.08f, -0.15f), new Vector3(0, 0f, 0.15f), new Vector3(0, 0.12f, 0.45f), new Vector3(0, 0.17f, 0.68f), new Vector3(0, 0.15f, 0.82f) },
               new[] { 0.32f, 0.33f, 0.3f, 0.24f, 0.12f },
               new[] { 0.3f, 0.32f, 0.3f, 0.22f, 0.11f }, 8);
        return b.ToMesh("EagleHead");
    }

    // 윗부리: 두툼하게 나와서 끝이 아래로 갈고리처럼 휨
    static Mesh BeakMesh()
    {
        var b = new MeshKit();
        b.Loft(new[] { new Vector3(0, 0.12f, 0.7f), new Vector3(0, 0.12f, 0.9f), new Vector3(0, 0.07f, 1.07f), new Vector3(0, -0.04f, 1.17f), new Vector3(0, -0.15f, 1.15f) },
               new[] { 0.13f, 0.1f, 0.075f, 0.045f, 0.008f },
               new[] { 0.12f, 0.1f, 0.075f, 0.045f, 0.008f }, 6);
        return b.ToMesh("EagleBeak");
    }

    static Mesh JawMesh()
    {
        var b = new MeshKit();
        b.Loft(new[] { new Vector3(0, 0f, -0.05f), new Vector3(0, -0.02f, 0.12f), new Vector3(0, -0.03f, 0.27f) },
               new[] { 0.1f, 0.07f, 0.01f }, new[] { 0.05f, 0.04f, 0.01f }, 5);
        return b.ToMesh("EagleJaw");
    }

    // 날개 안쪽(어깨 → 팔꿈치): 앞쪽 가장자리는 곧고 뒤쪽은 넓게
    static Mesh ArmMesh()
    {
        var b = new MeshKit();
        b.Prism(new[] { new Vector3(0, 0, 0.32f), new Vector3(2.25f, 0, 0.22f), new Vector3(2.3f, 0, -0.95f), new Vector3(1.3f, 0, -1.3f), new Vector3(0.1f, 0, -1.2f) }, Vector3.up, 0.13f);
        return b.ToMesh("EagleArm");
    }

    // 날개 위쪽 앞부분의 밝은 갈색 깃털 띠 (색 차이로 입체감)
    static Mesh CovertMesh()
    {
        var b = new MeshKit();
        b.Prism(new[] { new Vector3(0.05f, 0.05f, 0.3f), new Vector3(2.2f, 0.05f, 0.2f), new Vector3(2.15f, 0.05f, -0.35f), new Vector3(0.1f, 0.05f, -0.5f) }, Vector3.up, 0.08f);
        return b.ToMesh("EagleCoverts");
    }

    // 날개 바깥(팔꿈치 → 손목)
    static Mesh HandMesh()
    {
        var b = new MeshKit();
        b.Prism(new[] { new Vector3(-0.05f, 0, 0.22f), new Vector3(1.75f, 0, 0.08f), new Vector3(1.85f, 0, -0.2f), new Vector3(1.4f, 0, -0.9f), new Vector3(-0.05f, 0, -1.0f) }, Vector3.up, 0.1f);
        return b.ToMesh("EagleHand");
    }

    // 손가락 깃털: 뿌리는 좁고 가운데 넓다가 끝이 뾰족
    static Mesh FeatherMesh(float len, float width)
    {
        var b = new MeshKit();
        b.Prism(new[] { new Vector3(0, 0, width * 0.3f), new Vector3(len * 0.45f, 0, width * 0.5f), new Vector3(len, 0, 0.02f), new Vector3(len * 0.55f, 0, -width * 0.5f), new Vector3(0, 0, -width * 0.3f) }, Vector3.up, 0.04f);
        return b.ToMesh("EagleFeather");
    }

    static Mesh TailFeatherMesh()
    {
        var b = new MeshKit();
        b.Prism(new[] { new Vector3(-0.08f, 0, 0), new Vector3(0.08f, 0, 0), new Vector3(0.17f, 0, -1.2f), new Vector3(0f, 0, -1.38f), new Vector3(-0.17f, 0, -1.2f) }, Vector3.up, 0.05f);
        return b.ToMesh("EagleTailFeather");
    }

    static Mesh ClawMesh()
    {
        var b = new MeshKit();
        b.Loft(new[] { new Vector3(0, 0, -0.02f), new Vector3(0, -0.02f, 0.08f), new Vector3(0, -0.09f, 0.14f) }, new[] { 0.04f, 0.03f, 0.005f }, new[] { 0.04f, 0.03f, 0.005f }, 5);
        return b.ToMesh("EagleClaw");
    }

    static Mesh Ellipsoid(Vector3 radii, int sides, int rings)
    {
        var centers = new Vector3[rings + 1];
        var rx = new float[rings + 1];
        var ry = new float[rings + 1];
        for (int i = 0; i <= rings; i++)
        {
            float a = Mathf.PI * i / rings;
            centers[i] = new Vector3(0, 0, -Mathf.Cos(a) * radii.z);
            rx[i] = Mathf.Max(0.002f, Mathf.Sin(a) * radii.x);
            ry[i] = Mathf.Max(0.002f, Mathf.Sin(a) * radii.y);
        }
        var b = new MeshKit();
        b.Loft(centers, rx, ry, sides);
        return b.ToMesh("Ellipsoid");
    }

    static Mesh BoxMesh(Vector3 size)
    {
        var b = new MeshKit();
        Vector3 h = size * 0.5f;
        b.Prism(new[] { new Vector3(-h.x, -h.y, 0), new Vector3(h.x, -h.y, 0), new Vector3(h.x, h.y, 0), new Vector3(-h.x, h.y, 0) }, Vector3.forward, size.z);
        return b.ToMesh("Box");
    }

    static Mesh Mirror(Mesh m, bool mirror)
    {
        if (!mirror) return m;
        Vector3[] v = m.vertices;
        int[] t = m.triangles;
        for (int i = 0; i < v.Length; i++) v[i].x = -v[i].x;
        for (int i = 0; i < t.Length; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; } // 뒤집으면 면 방향도 반대로
        Mesh r = new Mesh { name = m.name + "_L", vertices = v, triangles = t };
        r.RecalculateNormals();
        r.RecalculateBounds();
        return r;
    }

    static Transform Part(string name, Transform parent, Vector3 localPos, Mesh mesh, Material mat)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return g.transform;
    }
}

// 각진 로우폴리 메쉬 만들기: 꼭짓점을 면마다 따로 써서 평평한 면 그림자
public class MeshKit
{
    private readonly List<Vector3> verts = new List<Vector3>();
    private readonly List<int> tris = new List<int>();

    // outward 방향을 보는 면이 되도록 꼭짓점 순서를 맞춰서 추가
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { Vector3 t = b; b = c; c = t; }
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
    }

    // 중심선(centers)을 따라 타원 단면(가로 rx, 세로 ry)을 이어 붙인 관 + 양 끝 마개
    public void Loft(Vector3[] centers, float[] rx, float[] ry, int sides)
    {
        int n = centers.Length;
        var rings = new Vector3[n][];
        var tangents = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 tan = (centers[Mathf.Min(i + 1, n - 1)] - centers[Mathf.Max(i - 1, 0)]).normalized;
            tangents[i] = tan;
            Vector3 right = Vector3.Cross(Vector3.up, tan);
            right = right.sqrMagnitude < 0.001f ? Vector3.right : right.normalized;
            Vector3 up = Vector3.Cross(tan, right).normalized;
            rings[i] = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = Mathf.PI * 2f * k / sides;
                rings[i][k] = centers[i] + right * Mathf.Cos(a) * rx[i] + up * Mathf.Sin(a) * ry[i];
            }
        }
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 mid = (centers[i] + centers[i + 1]) * 0.5f;
            for (int k = 0; k < sides; k++)
            {
                int k2 = (k + 1) % sides;
                Vector3 a = rings[i][k], b = rings[i][k2], c = rings[i + 1][k2], d = rings[i + 1][k];
                Vector3 face = (a + b + c + d) * 0.25f;
                Tri(a, b, c, face - mid);
                Tri(a, c, d, face - mid);
            }
        }
        for (int k = 0; k < sides; k++)
        {
            int k2 = (k + 1) % sides;
            Tri(centers[0], rings[0][k], rings[0][k2], -tangents[0]);
            Tri(centers[n - 1], rings[n - 1][k], rings[n - 1][k2], tangents[n - 1]);
        }
    }

    // 평평한 다각형(poly, normal 방향으로 두께 th)을 위·아래 면 + 옆면으로
    public void Prism(Vector3[] poly, Vector3 normal, float th)
    {
        int n = poly.Length;
        Vector3 center = Vector3.zero;
        foreach (Vector3 p in poly) center += p;
        center /= n;
        Vector3 off = normal.normalized * th * 0.5f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = poly[i], b = poly[(i + 1) % n];
            Tri(center + off, a + off, b + off, normal);
            Tri(center - off, a - off, b - off, -normal);
            Vector3 side = (a + b) * 0.5f - center;
            Tri(a + off, b + off, b - off, side);
            Tri(a + off, b - off, a - off, side);
        }
    }

    public Mesh ToMesh(string name)
    {
        Mesh m = new Mesh { name = name };
        if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // 도시 빌딩 창문처럼 큰 메쉬
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
