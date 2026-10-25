using UnityEngine;
using System.Collections.Generic;

// 🚦 도시 스테이지 장애물 (NewMonoBehaviourScript 대신): 벽 간격마다 신호등 한 줄 + 4차선마다 마주 달려오는 자동차
// 차선은 4개뿐이라 한 차선이 17m로 넓음 → 자동차는 차선을 꽉 채우게(폭 11m, 높이 7.5m), 신호등도 1.6배로 크게
// 신호등 줄은 두 가지
//  - 신호등 문(게이트): 양쪽 기둥 + 가로 막대(끝에 보강대)에 차선마다 신호등 + 초록 도로 표지판 → 막대 위로 넘거나 신호등 아래로 빠져나감
//  - 팔 달린 신호등 기둥 2~3개: 둥근 기둥 + 받침 + 팔 + 도로 표지판, 높이·팔 길이가 제각각 → 사이사이로 피함
// 신호등: 모서리를 깎은 몸통 + 노란 테두리 배경판 + 불마다 차양
// 자동차: 옆모습(본닛·앞유리·지붕·트렁크)을 폭만큼 밀어낸 로우폴리 차체 + 유리창 + 범퍼·그릴 + 사이드미러 + 휠캡 + 전조등·후미등
//         차종(승용차·SUV·버스·트럭)마다 모양은 한 번만 만들어 같이 씀 (색은 차마다)
// 기둥은 차선 경계(흰 점선)·중앙선·인도 끝에만 서서 자동차와 겹치지 않음. 자동차는 차도(roadY) 위를 새 쪽(-z)으로 달려옴
// 매달린 신호등 아래 끝은 가장 큰 차 지붕보다 위 → 차가 신호등을 뚫고 지나가 보이지 않음
// 부딪히면 게임오버 (태그 Obstacle)
public class CityTraffic : MonoBehaviour
{
    public Transform player;
    public float rowSpacing = 24f;          // 신호등 줄 간격 (벽 간격)
    public float roadY = -6.5f;

    [Header("머티리얼")]
    public Material poleMat;                // 기둥·막대 (짙은 회색 금속)
    public Material signalMat;              // 신호등 몸통 (검정)
    public Material lampOffMat;             // 꺼진 불
    public Material redMat, yellowMat, greenMat; // 켜진 불 (발광)
    public Material carBodyMat;             // 차 몸통 (색은 차마다 다르게)
    public Material carGlassMat;            // 차 유리
    public Material tireMat;                // 바퀴
    public Material headlightMat;           // 전조등 (발광)
    public Material taillightMat;           // 후미등 (빨강 발광)
    public Material carTrimMat;             // 범퍼·그릴 (짙은 회색)
    public Material hubMat;                 // 휠캡 (은색)
    public Material backplateMat;           // 신호등 배경판 테두리 (노랑)
    public Material streetSignMat;          // 도로 표지판 (초록)
    public Material signTextMat;            // 표지판 글자 줄 (흰색)

    [Header("자동차")]
    public float spawnAhead = 160f;         // 새 앞 이만큼에서 나타남
    public Vector2 laneSpeed = new Vector2(7f, 15f);   // 차선마다 속도 (같은 차선은 같은 속도 → 추월·겹침 없음)
    public Vector2 spawnInterval = new Vector2(1.4f, 4.2f);
    public Color[] carColors =
    {
        new Color(0.85f, 0.15f, 0.15f), new Color(0.95f, 0.78f, 0.15f), new Color(0.2f, 0.45f, 0.85f),
        new Color(0.92f, 0.92f, 0.92f), new Color(0.15f, 0.15f, 0.18f), new Color(0.3f, 0.65f, 0.4f), new Color(0.6f, 0.62f, 0.66f),
    };

    // 자동차 크기: 차종마다 폭은 CarWidth로 차선을 꽉 채우고, 높이는 CarHeight를 넘지 않게 (넘으면 높이·길이만 덜 키움)
    // → 버스·트럭도 지붕 높이가 승용차와 같아서 신호등이 그 위에 매달릴 수 있음
    const float CarWidth = 11f;
    const float CarHeight = 7.5f;
    const float SignalScale = 1.6f;   // 신호등 크기 배율
    const float HeadHeight = 2.6f * SignalScale; // 매달린 신호등 길이
    const float TallestCarTop = -6.5f + CarHeight + 0.4f; // 차 지붕 높이 (판정 상자 여유 포함, 약 1.4)

    // 신호등 높이: 팔 달린 기둥 꼭대기는 최대 MaxPoleTop + 0.5, 신호등 문 막대 윗면은 최대 MaxBeamY + 막대 두께
    // 최소 높이는 매달린 신호등 아래 끝이 트럭 지붕보다 0.5 위 + Lift (신호등 아래로 빠져나갈 틈을 넉넉하게)
    const float Lift = 2.5f;
    const float MaxPoleTop = 12f + Lift, MaxBeamY = 9.5f + Lift;
    const float MinHangY = TallestCarTop + HeadHeight + 0.5f + Lift;
    public const float MaxSignalHeight = MaxPoleTop + 0.5f; // 가장 높은 신호등 꼭대기

    static readonly float[] Lanes = { -25.5f, -8.5f, 8.5f, 25.5f };
    // 기둥을 세울 수 있는 곳: 인도 끝 + 차선 경계 + 중앙선
    static readonly float[] PoleSpots = { -34f, -17f, 0f, 17f, 34f };

    class Car { public Rigidbody rb; public float speed, length; public int lane; }

    private float nextRowZ = NewMonoBehaviourScript.FirstChunkZ;
    private readonly Queue<GameObject> rows = new Queue<GameObject>();
    private readonly List<Car> cars = new List<Car>();
    private readonly float[] laneSpeeds = new float[Lanes.Length];
    private readonly float[] laneNext = new float[Lanes.Length];
    private readonly Car[] laneLast = new Car[Lanes.Length];
    private MaterialPropertyBlock block;

    // 차종별 모양 (몸통 / 유리 / 범퍼·그릴 / 짐칸), 신호등 몸통·배경판·차양 모양: 처음 한 번만 만듦
    private readonly Dictionary<int, Mesh[]> carMeshes = new Dictionary<int, Mesh[]>();
    private Mesh headMesh, plateMesh, visorMesh;

    void Awake()
    {
        NewMonoBehaviourScript.Spacing = rowSpacing; // 벽 개수 세기
    }

    void Start()
    {
        if (player == null) player = GameObject.Find("Cube").transform;
        block = new MaterialPropertyBlock();
        BuildSignalMeshes();
        for (int i = 0; i < Lanes.Length; i++)
        {
            laneSpeeds[i] = Random.Range(laneSpeed.x, laneSpeed.y);
            laneNext[i] = Random.Range(0f, spawnInterval.y);
        }
        // 처음부터 길에 차가 다니고 있게 (출발점 바로 앞은 비움)
        for (int i = 0; i < Lanes.Length; i++)
            for (float z = player.position.z + 40f + Random.Range(0f, 30f); z < player.position.z + spawnAhead; z += Random.Range(35f, 80f))
                SpawnCar(i, z);
    }

    private bool stopped;

    // 보스 등장: 신호등·자동차를 더 만들지 않고, 남은 신호등 줄은 양옆으로 쓰러지듯 치움 + 차들은 빨리 지나가 버림
    public void ClearForBoss()
    {
        stopped = true;
        foreach (GameObject row in rows) if (row != null) StartCoroutine(Topple(row));
        rows.Clear();
        foreach (Car c in cars) c.speed *= 3.5f;
    }

    System.Collections.IEnumerator Topple(GameObject row)
    {
        foreach (Collider c in row.GetComponentsInChildren<Collider>()) c.enabled = false;
        Transform t = row.transform;
        float side = Random.value < 0.5f ? -1f : 1f;
        for (float u = 0f; u < 1.2f && t != null; u += Time.deltaTime)
        {
            t.position += new Vector3(side * 45f, -18f, 0f) * Time.deltaTime;
            t.rotation = Quaternion.Euler(0f, 0f, -side * u * 70f);
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    void Update()
    {
        if (player == null || stopped) return;

        // 신호등 줄: 새 앞 120m까지 미리 세움
        while (player.position.z + 120f > nextRowZ)
        {
            rows.Enqueue(BuildRow(nextRowZ));
            nextRowZ += rowSpacing;
        }
        while (rows.Count > 0 && rows.Peek().transform.position.z < player.position.z - 20f) Destroy(rows.Dequeue());

        // 자동차: 차선마다 시간 간격을 두고 새 앞쪽에서 나타남 (앞차와 너무 붙으면 다음으로 미룸)
        for (int i = 0; i < Lanes.Length; i++)
        {
            laneNext[i] -= Time.deltaTime;
            if (laneNext[i] > 0f) continue;
            float z = player.position.z + spawnAhead;
            Car last = laneLast[i];
            if (last != null && last.rb != null && last.rb.position.z > z - last.length - 10f) { laneNext[i] = 0.3f; continue; }
            SpawnCar(i, z);
            laneNext[i] = Random.Range(spawnInterval.x, spawnInterval.y);
        }
    }

    void FixedUpdate()
    {
        for (int i = cars.Count - 1; i >= 0; i--)
        {
            Car c = cars[i];
            if (c.rb == null) { cars.RemoveAt(i); continue; }
            Vector3 p = c.rb.position + Vector3.back * c.speed * Time.fixedDeltaTime;
            c.rb.MovePosition(p);
            if (player != null && p.z < player.position.z - 30f)
            {
                Destroy(c.rb.gameObject);
                cars.RemoveAt(i);
            }
        }
    }

    // ───────────────────────── 신호등 줄 ─────────────────────────

    GameObject BuildRow(float z)
    {
        GameObject row = new GameObject("TrafficRow");
        row.transform.position = new Vector3(0f, 0f, z);
        // 이 줄 신호등은 모두 같은 색 (빨강 / 노랑 / 초록)
        int light = Random.Range(0, 3);
        if (Random.value < 0.45f) Gantry(row.transform, light);
        else Poles(row.transform, light);
        return row;
    }

    // 신호등 문: 기둥 두 개 사이 가로 막대(양 끝 보강대) + 차선마다 신호등 + 가운데 도로 표지판
    void Gantry(Transform row, int light)
    {
        int a, b;
        do { a = Random.Range(0, PoleSpots.Length); b = Random.Range(0, PoleSpots.Length); }
        while (b - a < 1);
        float x0 = PoleSpots[a], x1 = PoleSpots[b];
        float beamY = Random.Range(MinHangY, MaxBeamY);
        float k = SignalScale;
        float th = 0.8f * k, pw = 0.7f * k;
        Pole(row, x0, beamY + th, pw);
        Pole(row, x1, beamY + th, pw);
        Part(row, "Beam", new Vector3((x0 + x1) * 0.5f, beamY + th * 0.5f, 0f), new Vector3(x1 - x0 + pw, th, 0.6f * k), poleMat, true);
        // 보강대: 기둥과 막대가 만나는 모서리에 비스듬한 막대
        for (int s = 0; s < 2; s++)
        {
            float px = s == 0 ? x0 : x1, dir = s == 0 ? 1f : -1f;
            GameObject brace = Part(row, "Brace", new Vector3(px + dir * 1.3f * k, beamY - 1.1f * k, 0f), new Vector3(0.25f * k, 3.4f * k, 0.25f * k), poleMat, true);
            brace.transform.localRotation = Quaternion.Euler(0f, 0f, dir * 45f);
        }
        bool signed = false;
        foreach (float lane in Lanes)
        {
            if (lane <= x0 + 1f || lane >= x1 - 1f) continue;
            SignalHead(row, new Vector3(lane, beamY, 0f), light);
            // 첫 신호등 옆에 도로 표지판 하나
            if (!signed && x1 - x0 > 20f) { signed = true; StreetSign(row, new Vector3(lane + 4.2f * k, beamY - 0.2f * k, 0f)); }
        }
    }

    // 팔 달린 신호등 기둥 2~3개 (서로 다른 자리, 높이·팔 길이 제각각)
    void Poles(Transform row, int light)
    {
        int count = Random.Range(2, 4);
        var used = new List<int>();
        float k = SignalScale;
        for (int n = 0; n < count; n++)
        {
            int spot;
            do { spot = Random.Range(0, PoleSpots.Length); } while (used.Contains(spot));
            used.Add(spot);

            float x = PoleSpots[spot];
            float top = Random.Range(MinHangY, MaxPoleTop);
            // 팔은 길 가운데 쪽으로 (가운데 기둥은 아무 쪽이나)
            float dir = x > 0.5f ? -1f : x < -0.5f ? 1f : (Random.value < 0.5f ? -1f : 1f);
            float arm = Random.Range(8f, 15f);
            Pole(row, x, top + 0.5f, 0.6f * k);
            // 팔: 가로로 눕힌 둥근 막대 + 끝 마개
            GameObject armGo = Part(row, "Arm", new Vector3(x + dir * arm * 0.5f, top + 0.2f, 0f), new Vector3(0.45f * k, arm * 0.5f, 0.45f * k), poleMat, true, PrimitiveType.Cylinder);
            armGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Part(row, "ArmCap", new Vector3(x + dir * arm, top + 0.2f, 0f), new Vector3(0.55f, 0.55f, 0.55f) * k, poleMat, false, PrimitiveType.Sphere);
            SignalHead(row, new Vector3(x + dir * arm, top, 0f), light);
            if (arm > 11f) SignalHead(row, new Vector3(x + dir * arm * 0.5f, top, 0f), light);
            else StreetSign(row, new Vector3(x + dir * 2.6f * k, top - 0.1f * k, 0f));
            // 보행자 신호등 (기둥 중간)
            if (Random.value < 0.5f) Part(row, "WalkSignal", new Vector3(x + dir * 0.6f * k, roadY + 3.5f * k, 0f), new Vector3(0.6f * k, 1.2f * k, 0.5f * k), signalMat, true);
        }
    }

    // 둥근 기둥 + 넓은 받침 (top: 꼭대기 높이, d: 굵기)
    void Pole(Transform row, float x, float top, float d)
    {
        float h = top - roadY;
        Part(row, "Pole", new Vector3(x, roadY + h * 0.5f, 0f), new Vector3(d, h * 0.5f, d), poleMat, true, PrimitiveType.Cylinder);
        Part(row, "PoleBase", new Vector3(x, roadY + 0.5f, 0f), new Vector3(d * 1.8f, 0.5f, d * 1.8f), poleMat, false, PrimitiveType.Cylinder);
    }

    // 초록 도로 표지판 (흰 테두리 + 글자 줄 두 개). center = 위쪽 가운데
    void StreetSign(Transform row, Vector3 top)
    {
        float k = SignalScale;
        Vector3 c = top + new Vector3(0f, -0.45f * k, -0.35f * k);
        Part(row, "SignBorder", c + new Vector3(0f, 0f, 0.02f), new Vector3(3.3f, 0.95f, 0.05f) * k, signTextMat != null ? signTextMat : signalMat, true);
        Part(row, "Sign", c, new Vector3(3.1f, 0.78f, 0.06f) * k, streetSignMat != null ? streetSignMat : signalMat, false);
        Part(row, "SignText", c + new Vector3(-0.3f, 0.14f, -0.04f) * k, new Vector3(2.0f, 0.12f, 0.02f) * k, signTextMat != null ? signTextMat : lampOffMat, false);
        Part(row, "SignText", c + new Vector3(0.2f, -0.14f, -0.04f) * k, new Vector3(1.4f, 0.12f, 0.02f) * k, signTextMat != null ? signTextMat : lampOffMat, false);
    }

    // 신호등 하나: 위가 top에 매달린 몸통(모서리 깎음) + 노란 테두리 배경판 + 빨강/노랑/초록 불(하나만 켜짐) + 불마다 차양
    void SignalHead(Transform row, Vector3 top, int light)
    {
        float k = SignalScale;
        GameObject head = new GameObject("Signal");
        head.tag = "Obstacle";
        head.transform.SetParent(row, false);
        head.transform.localPosition = top + new Vector3(0f, -HeadHeight * 0.5f, 0f);
        head.transform.localScale = Vector3.one * k;
        MeshPart(head.transform, "Housing", headMesh, signalMat);
        MeshPart(head.transform, "Visors", visorMesh, signalMat);
        MeshPart(head.transform, "Backplate", plateMesh, backplateMat != null ? backplateMat : signalMat);
        BoxCollider col = head.AddComponent<BoxCollider>();
        col.size = new Vector3(1.0f, 2.6f, 0.8f);
        // 매다는 짧은 막대
        Part(head.transform, "Hanger", new Vector3(0f, 1.38f, 0f), new Vector3(0.18f, 0.18f, 0.18f), poleMat, false);

        Material[] on = { redMat, yellowMat, greenMat };
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = new Vector3(0f, 0.85f - i * 0.82f, -0.42f);
            GameObject lens = Part(head.transform, "Lamp", p, new Vector3(0.58f, 0.58f, 0.1f), i == light ? on[i] : lampOffMat, false, PrimitiveType.Sphere);
            if (i == light) lens.transform.localScale = new Vector3(0.62f, 0.62f, 0.14f);
        }
    }

    // 신호등 모양 (크기 1배 기준, 앞이 -z): 몸통 1.0×2.6×0.8, 배경판 1.6×3.2, 차양은 불 위 반원통 대신 세 면 지붕
    void BuildSignalMeshes()
    {
        var housing = new MeshKit();
        // 앞뒤로 모서리를 깎은 팔각 단면을 세로로 세운 몸통 → 정면에서 보면 둥근 모서리 상자
        const float hw = 0.5f, hh = 1.3f, hd = 0.4f, c = 0.14f;
        Vector3[] oct =
        {
            new Vector3(-hw + c, -hh, 0f), new Vector3(hw - c, -hh, 0f), new Vector3(hw, -hh + c, 0f), new Vector3(hw, hh - c, 0f),
            new Vector3(hw - c, hh, 0f), new Vector3(-hw + c, hh, 0f), new Vector3(-hw, hh - c, 0f), new Vector3(-hw, -hh + c, 0f),
        };
        housing.Prism(oct, Vector3.forward, hd * 2f);
        headMesh = housing.ToMesh("SignalHousing");

        var plate = new MeshKit();
        const float pw = 0.8f, ph = 1.6f, pc = 0.25f;
        Vector3[] board =
        {
            new Vector3(-pw + pc, -ph, 0f), new Vector3(pw - pc, -ph, 0f), new Vector3(pw, -ph + pc, 0f), new Vector3(pw, ph - pc, 0f),
            new Vector3(pw - pc, ph, 0f), new Vector3(-pw + pc, ph, 0f), new Vector3(-pw, ph - pc, 0f), new Vector3(-pw, -ph + pc, 0f),
        };
        // 몸통 바로 뒤 (z +0.42)
        for (int i = 0; i < board.Length; i++) board[i] += new Vector3(0f, 0f, 0.44f);
        plate.Prism(board, Vector3.forward, 0.04f);
        plateMesh = plate.ToMesh("SignalBackplate");

        var visor = new MeshKit();
        for (int i = 0; i < 3; i++)
        {
            float y = 0.85f - i * 0.82f;
            float r = 0.36f, depth = 0.5f, z0 = -0.4f, z1 = z0 - depth;
            // 불 둘레 위쪽 반원을 앞으로 내민 차양 (6조각), 안팎 양쪽 면
            const int seg = 6;
            for (int s = 0; s < seg; s++)
            {
                float a0 = Mathf.PI * s / seg, a1 = Mathf.PI * (s + 1) / seg;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r, y + Mathf.Sin(a0) * r, 0f), p1 = new Vector3(Mathf.Cos(a1) * r, y + Mathf.Sin(a1) * r, 0f);
                Vector3 a = p0 + Vector3.forward * z0, b = p1 + Vector3.forward * z0, cc = p1 + Vector3.forward * z1, d = p0 + Vector3.forward * z1;
                Vector3 outDir = (p0 + p1) * 0.5f - new Vector3(0f, y, 0f);
                visor.Tri(a, b, cc, outDir); visor.Tri(a, cc, d, outDir);
                visor.Tri(a, b, cc, -outDir); visor.Tri(a, cc, d, -outDir);
            }
        }
        visorMesh = visor.ToMesh("SignalVisors");
    }

    // ───────────────────────── 자동차 ─────────────────────────

    // 차종: 0 승용차 / 1 SUV / 2 버스 / 3 트럭. 앞(전조등)이 새 쪽(-z)
    void SpawnCar(int lane, float z)
    {
        float r = Random.value;
        int type = r < 0.5f ? 0 : r < 0.75f ? 1 : r < 0.88f ? 2 : 3;
        GameObject car = new GameObject("Car");
        car.tag = "Obstacle";
        Transform t = car.transform;
        t.position = new Vector3(Lanes[lane] + Random.Range(-0.8f, 0.8f), roadY, z);
        Color color = carColors[Random.Range(0, carColors.Length)];
        if (type == 2) color = Random.value < 0.5f ? new Color(0.2f, 0.55f, 0.35f) : new Color(0.25f, 0.4f, 0.8f);
        bool taxi = type == 0 && color.r > 0.9f && color.g > 0.7f && color.b < 0.3f; // 노란 승용차는 택시

        // 차종별 크기 (폭, 높이, 길이), 바퀴 위치
        float w, h, len, wheelR, wheelZ;
        switch (type)
        {
            case 0: w = 2.0f; h = 1.5f; len = 4.4f; wheelR = 0.38f; wheelZ = 1.35f; break;
            case 1: w = 2.2f; h = 2.0f; len = 4.8f; wheelR = 0.45f; wheelZ = 1.5f; break;
            case 2: w = 2.6f; h = 3.3f; len = 11f; wheelR = 0.52f; wheelZ = 3.6f; break;
            default: w = 2.5f; h = 3.4f; len = 8f; wheelR = 0.52f; wheelZ = 2.7f; break;
        }

        Mesh[] m = CarMeshes(type);
        Paint(MeshPart(t, "Body", m[0], carBodyMat), color);
        MeshPart(t, "Glass", m[1], carGlassMat);
        MeshPart(t, "Trim", m[2], carTrimMat != null ? carTrimMat : tireMat);
        if (m[3] != null) Paint(MeshPart(t, "Cargo", m[3], carBodyMat), new Color(0.9f, 0.9f, 0.87f));

        // 바퀴 + 휠캡
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 wp = new Vector3(sx * (w * 0.5f - 0.05f), wheelR, sz * wheelZ);
                GameObject wheel = Part(t, "Wheel", wp, new Vector3(wheelR * 2f, 0.14f, wheelR * 2f), tireMat, false, PrimitiveType.Cylinder);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                GameObject hub = Part(t, "Hub", wp + new Vector3(sx * 0.04f, 0f, 0f), new Vector3(wheelR * 1.1f, 0.12f, wheelR * 1.1f), hubMat != null ? hubMat : tireMat, false, PrimitiveType.Cylinder);
                hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

        // 전조등 (앞, 발광) + 후미등 (뒤, 빨강)
        float lightY = type >= 2 ? 0.95f : type == 1 ? 0.95f : 0.75f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            Part(t, "Headlight", new Vector3(sx * w * 0.34f, lightY, -len * 0.5f - 0.03f), new Vector3(0.48f, 0.22f, 0.06f), headlightMat, false);
            Part(t, "Taillight", new Vector3(sx * w * 0.38f, lightY + 0.1f, len * 0.5f + 0.03f), new Vector3(0.4f, 0.2f, 0.06f), taillightMat != null ? taillightMat : redMat, false);
        }
        if (taxi) // 택시 지붕 표시등
            Part(t, "TaxiSign", new Vector3(0f, h + 0.12f, 0.2f), new Vector3(0.8f, 0.26f, 0.32f), headlightMat, false);
        if (type == 2) // 버스 행선지 표시판 (주황 발광)
            Part(t, "Destination", new Vector3(0f, 2.98f, -len * 0.5f - 0.04f), new Vector3(1.8f, 0.26f, 0.05f), yellowMat, false);

        // 판정: 차 전체를 감싸는 상자 하나 (움직이는 장애물이라 kinematic)
        BoxCollider col = car.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, h * 0.5f + 0.1f, 0f);
        col.size = new Vector3(w, h, len);
        Rigidbody rb = car.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // 차선에 꽉 차게 키움 (판정 상자도 같이 커짐): 폭은 CarWidth, 높이·길이는 CarHeight를 넘지 않는 만큼
        float widthScale = CarWidth / w;
        float heightScale = Mathf.Min(widthScale, CarHeight / (h + 0.1f));
        t.localScale = new Vector3(widthScale, heightScale, heightScale);

        var c = new Car { rb = rb, speed = laneSpeeds[lane], length = len * heightScale, lane = lane };
        cars.Add(c);
        laneLast[lane] = c;
    }

    // 차종별 모양: [0] 몸통, [1] 유리, [2] 범퍼·그릴·창틀(짙은 회색), [3] 짐칸 (트럭만)
    Mesh[] CarMeshes(int type)
    {
        Mesh[] cached;
        if (carMeshes.TryGetValue(type, out cached)) return cached;
        var body = new MeshKit();
        var glass = new MeshKit();
        var trim = new MeshKit();
        MeshKit cargo = null;

        switch (type)
        {
            case 0: // 승용차: 낮은 본닛 → 비스듬한 앞유리 → 둥근 지붕 → 뒷유리 → 트렁크
            {
                const float w = 2.0f;
                Side(body, new[] { -2.2f, 0.32f, -2.26f, 0.62f, -2.12f, 0.86f, -1.0f, 0.98f, -0.42f, 1.46f, 0.75f, 1.5f, 1.42f, 1.0f, 2.15f, 0.94f, 2.25f, 0.62f, 2.2f, 0.32f }, w);
                Pane(glass, -1.0f, 0.98f, -0.42f, 1.46f, w - 0.32f);  // 앞유리
                Pane(glass, 0.75f, 1.5f, 1.42f, 1.0f, w - 0.32f);     // 뒷유리
                Side(glass, new[] { -0.9f, 1.03f, -0.4f, 1.4f, 0.7f, 1.43f, 1.3f, 1.04f }, w + 0.03f);   // 옆 유리창
                Box(trim, new Vector3(-0.06f, 1.02f, -0.15f), new Vector3(0.06f, 1.44f, 0.05f), w + 0.05f); // 가운데 창틀
                Bumpers(trim, w, 2.2f, 0.32f, 0.62f);
                Box(trim, new Vector3(-0.55f, 0.64f, -2.3f), new Vector3(0.55f, 0.84f, -2.2f), 0f); // 그릴
                Mirrors(body, w, 1.05f, -0.55f);
                break;
            }
            case 1: // SUV: 높고 각진 몸통 + 지붕 짐받이
            {
                const float w = 2.2f;
                Side(body, new[] { -2.4f, 0.4f, -2.46f, 0.78f, -2.36f, 1.18f, -1.3f, 1.28f, -0.75f, 1.96f, 1.9f, 2.0f, 2.38f, 1.85f, 2.45f, 0.78f, 2.4f, 0.4f }, w);
                Pane(glass, -1.3f, 1.28f, -0.75f, 1.96f, w - 0.34f);  // 앞유리
                Pane(glass, 2.38f, 1.85f, 2.43f, 1.3f, w - 0.4f);     // 뒷유리
                Side(glass, new[] { -1.2f, 1.33f, -0.73f, 1.88f, 1.86f, 1.9f, 2.22f, 1.34f }, w + 0.03f);
                Box(trim, new Vector3(-0.06f, 1.32f, 0.35f), new Vector3(0.06f, 1.9f, 0.5f), w + 0.05f);
                Bumpers(trim, w, 2.42f, 0.4f, 0.78f);
                Box(trim, new Vector3(-0.65f, 0.8f, -2.5f), new Vector3(0.65f, 1.12f, -2.38f), 0f);
                for (int s = -1; s <= 1; s += 2) Box(trim, new Vector3(s * 0.8f - 0.06f, 2.0f, -0.4f), new Vector3(s * 0.8f + 0.06f, 2.1f, 1.7f), 0f); // 짐받이
                Mirrors(body, w, 1.35f, -0.9f);
                break;
            }
            case 2: // 버스: 긴 상자 + 둥근 앞 지붕 + 옆 창문 띠(창틀) + 출입문 + 큰 앞유리
            {
                const float w = 2.6f;
                Side(body, new[] { -5.5f, 0.35f, -5.56f, 0.8f, -5.5f, 3.0f, -5.25f, 3.3f, 5.3f, 3.3f, 5.5f, 3.1f, 5.52f, 0.8f, 5.5f, 0.35f }, w);
                Side(glass, new[] { -4.3f, 1.55f, -4.3f, 2.78f, 5.05f, 2.78f, 5.05f, 1.55f }, w + 0.04f);    // 옆 창문 띠
                Box(glass, new Vector3(-1.15f, 1.3f, -5.6f), new Vector3(1.15f, 2.85f, -5.52f), 0f);           // 앞유리
                Box(glass, new Vector3(-1.0f, 1.6f, 5.52f), new Vector3(1.0f, 2.7f, 5.58f), 0f);               // 뒷유리
                Box(glass, new Vector3(w * 0.5f - 0.02f, 0.5f, -4.9f), new Vector3(w * 0.5f + 0.04f, 2.8f, -3.9f), 0f); // 출입문 (오른쪽)
                for (float zz = -3.2f; zz < 5f; zz += 1.65f) Box(trim, new Vector3(-0.07f, 1.55f, zz), new Vector3(0.07f, 2.78f, zz + 0.14f), w + 0.06f); // 창틀
                Bumpers(trim, w, 5.55f, 0.35f, 0.8f);
                break;
            }
            default: // 트럭: 짧은 운전석 + 큰 흰 짐칸
            {
                const float w = 2.5f;
                Side(body, new[] { -4.0f, 0.4f, -4.06f, 0.85f, -4.0f, 2.45f, -3.62f, 2.9f, -2.0f, 2.9f, -1.6f, 2.55f, -1.6f, 0.4f }, w);
                Side(glass, new[] { -4.03f, 1.75f, -3.65f, 2.78f, -2.1f, 2.78f, -2.1f, 1.75f }, w + 0.03f);
                Box(glass, new Vector3(-1.05f, 1.75f, -4.1f), new Vector3(1.05f, 2.55f, -4.02f), 0f);
                Bumpers(trim, w, 4.05f, 0.4f, 0.85f);
                Box(trim, new Vector3(-0.8f, 0.9f, -4.1f), new Vector3(0.8f, 1.4f, -4.03f), 0f);
                Box(trim, new Vector3(-w * 0.5f, 0.45f, -1.55f), new Vector3(w * 0.5f, 0.75f, 4.0f), 0f); // 차대
                Mirrors(body, w, 2.0f, -3.4f);
                cargo = new MeshKit();
                Box(cargo, new Vector3(-w * 0.5f, 0.75f, -1.4f), new Vector3(w * 0.5f, 3.4f, 4.0f), 0f);
                for (int s = -1; s <= 1; s += 2) Box(trim, new Vector3(s * w * 0.5f - 0.02f, 1.0f, -1.2f), new Vector3(s * w * 0.5f + 0.02f, 1.18f, 3.8f), 0f); // 짐칸 띠
                break;
            }
        }

        cached = new[] { body.ToMesh("CarBody" + type), glass.ToMesh("CarGlass" + type), trim.ToMesh("CarTrim" + type), cargo != null ? cargo.ToMesh("CarCargo" + type) : null };
        carMeshes[type] = cached;
        return cached;
    }

    // 옆모습(z, y 짝으로 된 다각형)을 폭 w만큼 좌우로 밀어낸 덩어리
    static void Side(MeshKit kit, float[] zy, float w)
    {
        var poly = new Vector3[zy.Length / 2];
        for (int i = 0; i < poly.Length; i++) poly[i] = new Vector3(0f, zy[i * 2 + 1], zy[i * 2]);
        kit.Prism(poly, Vector3.right, w);
    }

    // 앞유리·뒷유리: 몸통의 비스듬한 면 (z0,y0)→(z1,y1) 바로 바깥에 붙인 얇은 판 (양 끝은 조금 줄여서 창틀이 보이게)
    static void Pane(MeshKit kit, float z0, float y0, float z1, float y1, float w)
    {
        Vector2 a = new Vector2(z0, y0), b = new Vector2(z1, y1), d = (b - a).normalized;
        Vector2 n = new Vector2(-d.y, d.x);
        if (n.y < 0f) n = -n;                // 위쪽(바깥)을 보게
        a += d * 0.08f; b -= d * 0.08f;
        Vector2 o = n * 0.03f, i = -n * 0.04f;
        Vector2[] q = { a + o, b + o, b + i, a + i };
        var poly = new Vector3[4];
        for (int k = 0; k < 4; k++) poly[k] = new Vector3(0f, q[k].y, q[k].x);
        kit.Prism(poly, Vector3.right, w);
    }

    // 상자. fullWidth > 0이면 x는 무시하고 차 폭만큼 (-fullWidth/2 ~ fullWidth/2)
    static void Box(MeshKit kit, Vector3 min, Vector3 max, float fullWidth)
    {
        if (fullWidth > 0f) { min.x = -fullWidth * 0.5f; max.x = fullWidth * 0.5f; }
        CityStreet.Box(kit, min, max);
        // 밑면도 (차 아래에서 보일 수 있어서)
        kit.Tri(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), Vector3.down);
        kit.Tri(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), Vector3.down);
    }

    // 앞뒤 범퍼: 차 끝(halfLen)에서 살짝 튀어나온 짙은 띠
    static void Bumpers(MeshKit kit, float w, float halfLen, float y0, float y1)
    {
        Box(kit, new Vector3(0f, y0, -halfLen - 0.08f), new Vector3(0f, y0 + (y1 - y0) * 0.55f, -halfLen + 0.25f), w + 0.06f);
        Box(kit, new Vector3(0f, y0, halfLen - 0.25f), new Vector3(0f, y0 + (y1 - y0) * 0.55f, halfLen + 0.08f), w + 0.06f);
    }

    // 사이드미러 (몸통 색)
    static void Mirrors(MeshKit kit, float w, float y, float z)
    {
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * (w * 0.5f + 0.1f);
            CityStreet.Box(kit, new Vector3(x - 0.1f, y, z - 0.12f), new Vector3(x + 0.1f, y + 0.16f, z + 0.12f));
        }
    }

    // ───────────────────────── 공용 ─────────────────────────

    GameObject MeshPart(Transform parent, string name, Mesh mesh, Material mat)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    void Paint(GameObject g, Color color)
    {
        Renderer r = g.GetComponent<Renderer>();
        r.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        r.SetPropertyBlock(block);
    }

    // 상자(또는 구·원기둥) 조각. solid면 부딪히는 장애물. 원기둥은 size.y가 높이의 절반 (Unity 원기둥은 높이 2)
    GameObject Part(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool solid, PrimitiveType shape = PrimitiveType.Cube)
    {
        GameObject g = GameObject.CreatePrimitive(shape);
        g.name = name;
        Collider c = g.GetComponent<Collider>();
        if (solid)
        {
            if (!(c is BoxCollider)) { Destroy(c); g.AddComponent<BoxCollider>(); }
            g.tag = "Obstacle";
        }
        else Destroy(c);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }
}
