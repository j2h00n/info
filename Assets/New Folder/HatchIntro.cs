using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// 🥚 1스테이지 스토리 시작: 바위 기둥 위 둥지에 알 하나 → ← → 를 번갈아 누르면 알이 흔들리며 금이 가고
// 다 깨지면 윗껍질이 튀어 오르고 갈매기가 퐁 나와서, 카메라가 뒤로 돌아가며 날아오르면서 스토리 시작
// (모션 조작이면 고개를 좌우로 까딱여도 됨)
public class HatchIntro : MonoBehaviour
{
    [Header("연결")]
    public BirdController bird;
    public NewMonoBehaviourScript obstacleSpawner; // 둥지 받침 바위 기둥: 장애물과 같은 바위·풀·색

    [Header("머티리얼")]
    public Material eggMat, crackMat, nestMat, nestDarkMat;

    [Header("사운드")]
    public AudioClip tapSound, crackSound, chirpSound;

    [Header("설정")]
    public int pressesToHatch = 10;
    public Vector3 closeCamPos = new Vector3(1.4f, 0.9f, 2.9f);  // 새(알) 기준: 앞쪽 오른편에서 알을 봄

    const float EggHeight = 1.15f, EggRadius = 0.42f, CrackT = 0.58f; // 금이 가는 높이 (아래에서 58%)
    const int Sides = 18, Rings = 14;

    private Transform nest, eggRoot, eggTop, eggBottom, seagull;
    private readonly List<LineRenderer> cracks = new List<LineRenderer>();
    private float[] zig;                 // 금 높이 들쭉날쭉 (금 선과 깨진 껍질 모양이 같도록 공유)
    private int presses, lastDir, lastMotionSign;
    private float tilt, tiltVel;         // 알 흔들림 (좌우 기울기 스프링)
    private float hop;
    private AudioSource sfx;
    private Text prompt;
    private Canvas canvas;
    private Transform cam;
    private Camera camComp;
    private Vector3 camBasePos;
    private Quaternion camBaseRot;
    private readonly List<GameObject> leftovers = new List<GameObject>();

    // BirdController.StartStory에서 호출 (끝나면 bird.StartGame)
    public void Begin()
    {
        Time.timeScale = 1f;
        Rigidbody rb = bird.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true; // 알 속에 있는 동안은 떨어지지 않게
        bird.transform.rotation = Quaternion.identity;

        seagull = bird.transform.Find("Seagull");
        if (seagull != null) seagull.localScale = Vector3.zero; // 아직 알 속

        cam = Camera.main.transform;
        camComp = Camera.main;
        camBasePos = cam.localPosition;
        camBaseRot = cam.localRotation;

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        float v = PlayerPrefs.GetFloat("JumpVolume", 1f);
        sfx.volume = v * v;

        BuildNest();
        BuildEgg();
        BuildPrompt();
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // 카메라: 알 앞쪽 오른편으로 다가감
        Vector3 eggCenter = new Vector3(0f, 0.05f, 0f);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            float e = Mathf.SmoothStep(0f, 1f, t / 0.6f);
            PlaceCamera(Vector3.Lerp(camBasePos, closeCamPos, e), eggCenter, Mathf.Lerp(0f, 1f, e));
            yield return null;
        }
        PlaceCamera(closeCamPos, eggCenter, 1f);

        // ← → 를 눌러 알 깨기
        while (presses < pressesToHatch)
        {
            int dir = ReadPress();
            if (dir != 0)
            {
                presses++;
                // 앞쪽에서 보는 시점이라 화면의 왼쪽 = 세상 +x → 왼쪽 누르면 화면 왼쪽으로 기울게
                tiltVel += dir * 260f;
                hop = 1f;
                lastDir = dir;
                if (sfx != null && tapSound != null) { sfx.pitch = Random.Range(0.9f, 1.15f); sfx.PlayOneShot(tapSound); }
                UpdateCracks();
            }
            AnimateEgg();
            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.06f;
            prompt.transform.localScale = Vector3.one * pulse;
            yield return null;
        }

        yield return Hatch();
    }

    // 왼쪽 = +1, 오른쪽 = -1 (알이 화면에서 그쪽으로 기움). 모션 조작이면 고개 기울기가 바뀔 때마다
    int ReadPress()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) return 1;
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) return -1;
        if (bird.UsesMotionControl)
        {
            float m = bird.MoveInput;
            int sign = m > 0.6f ? 1 : m < -0.6f ? -1 : 0;
            if (sign != 0 && sign != lastMotionSign) { lastMotionSign = sign; return -sign; }
            if (sign == 0 && Mathf.Abs(m) < 0.2f) lastMotionSign = 0;
        }
        return 0;
    }

    // 흔들흔들: 눌린 쪽으로 기울었다가 스프링처럼 돌아옴 + 콩 튀어오름
    void AnimateEgg()
    {
        float dt = Time.deltaTime;
        tiltVel += (-tilt * 90f - tiltVel * 9f) * dt;
        tilt += tiltVel * dt;
        hop = Mathf.MoveTowards(hop, 0f, dt * 5f);
        float shiver = presses > pressesToHatch * 0.6f ? Mathf.Sin(Time.time * 55f) * 1.2f : 0f; // 거의 깨질 땐 부들부들
        eggRoot.localPosition = new Vector3(0f, -0.52f + Mathf.Sin(hop * Mathf.PI) * 0.08f, 0f);
        eggRoot.localRotation = Quaternion.Euler(0f, 0f, -(tilt + shiver));
    }

    IEnumerator Hatch()
    {
        if (sfx != null) { sfx.pitch = 1f; if (crackSound != null) sfx.PlayOneShot(crackSound); }
        foreach (LineRenderer l in cracks) if (l != null) l.gameObject.SetActive(false);
        StartCoroutine(FadePrompt());

        // 윗껍질: 빙글 돌며 튀어 올랐다가 떨어짐 + 작은 껍질 조각들
        eggTop.SetParent(null, true);
        leftovers.Add(eggTop.gameObject);
        Vector3 topVel = new Vector3(Random.Range(-0.6f, 0.6f), 4.2f, Random.Range(-0.3f, 0.6f));
        Vector3 topSpin = new Vector3(Random.Range(-400f, 400f), Random.Range(-200f, 200f), Random.Range(-400f, 400f));
        var shards = new List<Transform>();
        var shardVel = new List<Vector3>();
        for (int i = 0; i < 9; i++)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(s.GetComponent<Collider>());
            s.name = "EggShard";
            s.GetComponent<Renderer>().sharedMaterial = eggMat;
            s.transform.position = eggRoot.TransformPoint(new Vector3(0f, EggHeight * CrackT, 0f)) + Random.insideUnitSphere * 0.2f;
            s.transform.localScale = new Vector3(Random.Range(0.06f, 0.14f), 0.025f, Random.Range(0.06f, 0.14f));
            s.transform.rotation = Random.rotation;
            shards.Add(s.transform);
            leftovers.Add(s);
            Vector2 flat = Random.insideUnitCircle.normalized * Random.Range(1f, 2.5f);
            shardVel.Add(new Vector3(flat.x, Random.Range(2f, 4f), flat.y));
        }

        // 갈매기: 알 속에서 통통 튀며 커짐 + 짹
        if (sfx != null && chirpSound != null) sfx.PlayOneShot(chirpSound);
        SeagullFlap flap = bird.GetComponentInChildren<SeagullFlap>();
        float t = 0f;
        while (t < 1.5f)
        {
            float dt = Time.deltaTime;
            t += dt;
            topVel += Physics.gravity * dt;
            eggTop.position += topVel * dt;
            eggTop.rotation = Quaternion.Euler(topSpin * dt) * eggTop.rotation;
            for (int i = 0; i < shards.Count; i++)
            {
                shardVel[i] += Physics.gravity * dt;
                shards[i].position += shardVel[i] * dt;
                shards[i].Rotate(300f * dt, 200f * dt, 0f);
            }
            // 쏙: 0.35초 동안 0 → 1.2 → 1 (통통)
            float u = Mathf.Clamp01(t / 0.35f);
            float pop = u < 0.7f ? Mathf.Lerp(0.2f, 1.2f, u / 0.7f) : Mathf.Lerp(1.2f, 1f, (u - 0.7f) / 0.3f);
            if (seagull != null) seagull.localScale = Vector3.one * pop;
            if (t > 0.5f && t - dt <= 0.5f && flap != null) flap.Flap(); // 처음으로 날갯짓 파닥
            if (t > 1.0f && t - dt <= 1.0f && flap != null) flap.Flap();
            // 남은 아랫껍질은 살짝 흔들리다 멈춤
            tiltVel += (-tilt * 90f - tiltVel * 9f) * dt;
            tilt += tiltVel * dt;
            eggRoot.localRotation = Quaternion.Euler(0f, 0f, -tilt);
            yield return null;
        }

        // 아랫껍질·둥지는 그 자리에 남기고 (새가 날아가며 멀어짐), 카메라는 새 뒤로 돌아가며 출발
        eggRoot.SetParent(null, true);
        leftovers.Add(eggRoot.gameObject);
        Vector3 from = cam.localPosition;
        Quaternion fromRot = cam.localRotation;
        for (t = 0f; t < 0.9f; t += Time.deltaTime)
        {
            float e = Mathf.SmoothStep(0f, 1f, t / 0.9f);
            // 새 둘레를 돌아 뒤쪽 원래 자리로
            float ang = Mathf.Lerp(Mathf.Atan2(from.x, from.z), Mathf.Atan2(camBasePos.x, camBasePos.z), e);
            float radius = Mathf.Lerp(new Vector2(from.x, from.z).magnitude, new Vector2(camBasePos.x, camBasePos.z).magnitude, e);
            cam.localPosition = new Vector3(Mathf.Sin(ang) * radius, Mathf.Lerp(from.y, camBasePos.y, e), Mathf.Cos(ang) * radius);
            cam.localRotation = Quaternion.Slerp(Quaternion.LookRotation(-cam.localPosition + new Vector3(0f, 0.05f, 0f)), camBaseRot, e);
            yield return null;
        }
        cam.localPosition = camBasePos;
        cam.localRotation = camBaseRot;

        Rigidbody rb = bird.GetComponent<Rigidbody>();
        rb.isKinematic = false;
        bird.StartGame();
        rb.AddForce(Vector3.up * bird.flapForce * 0.8f, ForceMode.Impulse); // 둥지에서 힘차게 박차고 날아오름
        if (flap != null) flap.Flap();

        Destroy(canvas.gameObject);
        yield return new WaitForSeconds(6f); // 둥지가 멀리 지나간 뒤 정리
        foreach (GameObject g in leftovers) if (g != null) Destroy(g);
        if (nest != null) Destroy(nest.gameObject);
    }

    void PlaceCamera(Vector3 localPos, Vector3 lookAtLocal, float k)
    {
        cam.localPosition = localPos;
        cam.localRotation = Quaternion.Slerp(camBaseRot, Quaternion.LookRotation(lookAtLocal - localPos), k);
    }

    // ───────────────────────── 금 ─────────────────────────

    // 누른 횟수에 따라 금이 점점 길어짐: 앞쪽에서 시작해 둘레를 따라 번지고, 막판엔 위아래로 가지를 침
    void UpdateCracks()
    {
        float p = presses / (float)pressesToHatch;
        int stage = p >= 0.85f ? 3 : p >= 0.55f ? 2 : p >= 0.25f ? 1 : 0;
        while (cracks.Count < stage)
        {
            int s = cracks.Count;
            // 정면(카메라 쪽, +z에서 +x로 조금 돈 방향)에서 시작해 단계마다 더 넓게
            float center = Mathf.Atan2(closeCamPos.x, closeCamPos.z);
            float half = new[] { 0.7f, 1.6f, Mathf.PI }[s];
            var pts = new List<Vector3>();
            int n = Mathf.CeilToInt(half * 2f / (Mathf.PI * 2f / Sides)) + 1;
            for (int i = 0; i <= n; i++)
            {
                float a = center - half + half * 2f * i / n;
                pts.Add(SurfacePoint(a, CrackT + Zig(a)) * 1.03f);
            }
            cracks.Add(MakeCrackLine(pts));
            if (s == 2)
            {
                // 가지 금: 위아래로 삐죽삐죽
                for (int b = 0; b < 4; b++)
                {
                    float a = center + (b - 1.5f) * 0.9f;
                    float dirT = b % 2 == 0 ? 0.16f : -0.14f;
                    cracks.Add(MakeCrackLine(new List<Vector3>
                    {
                        SurfacePoint(a, CrackT + Zig(a)) * 1.03f,
                        SurfacePoint(a + 0.12f, CrackT + Zig(a) + dirT * 0.5f) * 1.03f,
                        SurfacePoint(a - 0.05f, CrackT + Zig(a) + dirT) * 1.03f,
                    }));
                }
            }
        }
    }

    LineRenderer MakeCrackLine(List<Vector3> pts)
    {
        GameObject g = new GameObject("Crack");
        g.transform.SetParent(eggRoot, false);
        LineRenderer l = g.AddComponent<LineRenderer>();
        l.useWorldSpace = false;
        l.sharedMaterial = crackMat;
        l.widthMultiplier = 0.028f;
        l.numCornerVertices = 2;
        l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        l.positionCount = pts.Count;
        l.SetPositions(pts.ToArray());
        return l;
    }

    // ───────────────────────── 만들기 ─────────────────────────

    // 알 모양: 아래가 통통하고 위로 갈수록 살짝 뾰족 (t: 0 아래 ~ 1 위, ang: 둘레 각도)
    Vector3 SurfacePoint(float ang, float t)
    {
        float y = t * EggHeight;
        float r = EggRadius * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)), 0.8f) * (1.08f - 0.22f * t);
        return new Vector3(Mathf.Sin(ang) * r, y, Mathf.Cos(ang) * r);
    }

    float Zig(float ang)
    {
        int k = Mathf.RoundToInt(Mathf.Repeat(ang, Mathf.PI * 2f) / (Mathf.PI * 2f) * Sides) % Sides;
        return zig[k];
    }

    void BuildEgg()
    {
        zig = new float[Sides];
        for (int k = 0; k < Sides; k++) zig[k] = (k % 2 == 0 ? 1f : -1f) * Random.Range(0.035f, 0.07f);

        eggRoot = new GameObject("Egg").transform;
        eggRoot.SetParent(bird.transform, false);
        eggRoot.localPosition = new Vector3(0f, -0.52f, 0f);
        eggBottom = Shell("EggBottom", false);
        eggTop = Shell("EggTop", true);
    }

    // 껍질 반쪽: 바깥면 + 안쪽면 + 깨진 단면 (금 높이는 들쭉날쭉)
    Transform Shell(string name, bool top)
    {
        var outer = new List<Vector3[]>();
        for (int i = 0; i <= Rings; i++)
        {
            float f = i / (float)Rings;
            var ring = new Vector3[Sides];
            for (int k = 0; k < Sides; k++)
            {
                float ang = Mathf.PI * 2f * k / Sides;
                float crack = CrackT + zig[k];
                float t = top ? Mathf.Lerp(crack, 1f, f) : Mathf.Lerp(0f, crack, f);
                ring[k] = SurfacePoint(ang, t);
            }
            outer.Add(ring);
        }
        Vector3 axis(Vector3 p) { return new Vector3(0f, p.y, 0f); }
        var b = new MeshKit();
        int edge = top ? 0 : Rings; // 깨진 쪽 고리
        for (int i = 0; i < Rings; i++)
            for (int k = 0; k < Sides; k++)
            {
                int k2 = (k + 1) % Sides;
                Vector3 p0 = outer[i][k], p1 = outer[i][k2], p2 = outer[i + 1][k2], p3 = outer[i + 1][k];
                Vector3 mid = (p0 + p1 + p2 + p3) * 0.25f;
                Vector3 outDir = mid - axis(mid);
                b.Tri(p0, p1, p2, outDir); b.Tri(p0, p2, p3, outDir);
                // 안쪽면 (껍질 두께만큼 안으로)
                Vector3 q0 = Shrink(p0), q1 = Shrink(p1), q2 = Shrink(p2), q3 = Shrink(p3);
                b.Tri(q0, q1, q2, -outDir); b.Tri(q0, q2, q3, -outDir);
            }
        // 깨진 단면: 바깥 고리와 안쪽 고리를 이음
        for (int k = 0; k < Sides; k++)
        {
            int k2 = (k + 1) % Sides;
            Vector3 a = outer[edge][k], c = outer[edge][k2];
            Vector3 rim = top ? Vector3.down : Vector3.up;
            b.Tri(a, c, Shrink(c), rim); b.Tri(a, Shrink(c), Shrink(a), rim);
        }
        GameObject g = new GameObject(name);
        g.transform.SetParent(eggRoot, false);
        g.AddComponent<MeshFilter>().sharedMesh = b.ToMesh(name);
        g.AddComponent<MeshRenderer>().sharedMaterials = new[] { eggMat };
        return g.transform;
    }

    static Vector3 Shrink(Vector3 p)
    {
        Vector3 c = new Vector3(0f, p.y, 0f);
        return c + (p - c) * 0.88f;
    }

    // 둥지: 바위 기둥 꼭대기 + 잔가지를 둥글게 엮은 그릇 + 바닥 짚
    void BuildNest()
    {
        nest = new GameObject("Nest").transform;
        nest.position = bird.transform.position + new Vector3(0f, -0.62f, 0f);

        // 잔가지: 두 겹으로 둥글게, 제각각 기울여서
        for (int layer = 0; layer < 2; layer++)
        {
            int count = 22;
            float radius = layer == 0 ? 0.62f : 0.72f;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * 2f * (i + layer * 0.5f) / count;
                Vector3 p = new Vector3(Mathf.Sin(a) * radius, 0.05f + layer * 0.12f, Mathf.Cos(a) * radius);
                Transform twig = Part(PrimitiveType.Cylinder, nest, i % 3 == 0 ? nestDarkMat : nestMat, p, new Vector3(0.07f, 0.42f, 0.07f));
                // 둘레를 따라 눕히되 살짝씩 비뚤게
                twig.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f + Random.Range(-25f, 25f), 90f + Random.Range(-18f, 18f));
            }
        }
        Part(PrimitiveType.Sphere, nest, nestDarkMat, new Vector3(0f, -0.02f, 0f), new Vector3(1.3f, 0.25f, 1.3f)); // 바닥
        Part(PrimitiveType.Sphere, nest, nestMat, new Vector3(0f, -0.12f, 0f), new Vector3(1.6f, 0.3f, 1.6f));     // 바닥 아래쪽 덩어리

        BuildPillar();
    }

    // 둥지 받침: 장애물 벽과 같은 바위를 둥지 아래로 쌓아 내림 (충돌 없음) + 꼭대기에 풀
    void BuildPillar()
    {
        if (obstacleSpawner == null || obstacleSpawner.rockPrefabs == null || obstacleSpawner.rockPrefabs.Length == 0) return;
        float top = nest.position.y - 0.15f;
        float y = top;
        bool first = true;
        while (y > bird.fallLimitY - 30f)
        {
            GameObject prefab = obstacleSpawner.rockPrefabs[Random.Range(0, obstacleSpawner.rockPrefabs.Length)];
            GameObject rock = Instantiate(prefab, nest);
            foreach (Collider c in rock.GetComponentsInChildren<Collider>()) Destroy(c);
            Bounds mb = rock.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            Vector3 size = first ? new Vector3(2.6f, 2.2f, 2.6f) : new Vector3(Random.Range(2.6f, 3.4f), Random.Range(3f, 4.5f), Random.Range(2.6f, 3.4f));
            // 바위 메쉬는 Z축이 위 (장애물과 같은 방식). 첫 바위는 꼭대기가 평평하게 뒤집어서 둥지가 얹히게
            rock.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(first ? 90f : 270f, 0f, 0f);
            rock.transform.localScale = new Vector3(size.x / mb.size.x, size.z / mb.size.y, size.y / mb.size.z);
            float meshTop = first ? -mb.min.z : mb.max.z;
            rock.transform.position = new Vector3(nest.position.x + (first ? 0f : Random.Range(-0.3f, 0.3f)), y - meshTop * rock.transform.localScale.z, nest.position.z);
            foreach (Renderer r in rock.GetComponentsInChildren<Renderer>())
            {
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", obstacleSpawner.rockTint);
                r.SetPropertyBlock(block);
            }
            y -= size.y * 0.75f;
            first = false;
        }

        GameObject[] grass = obstacleSpawner.grassPrefabs;
        if (grass != null && grass.Length > 0)
            for (int i = 0; i < 4; i++)
            {
                GameObject g = Instantiate(grass[Random.Range(0, grass.Length)], nest);
                foreach (Collider c in g.GetComponentsInChildren<Collider>()) Destroy(c);
                float a = Random.Range(0f, Mathf.PI * 2f);
                g.transform.position = new Vector3(nest.position.x + Mathf.Sin(a) * 1.1f, top - 0.1f, nest.position.z + Mathf.Cos(a) * 1.1f);
                g.transform.rotation = Quaternion.Euler(270f, Random.Range(0f, 360f), 0f);
                g.transform.localScale = Vector3.one * Random.Range(0.8f, 1.2f);
            }
    }

    Transform Part(PrimitiveType type, Transform parent, Material mat, Vector3 localPos, Vector3 localScale)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>()); // 보기만 (이름이 "Cube"류 판정에 안 걸리게)
        g.name = "NestPart";
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = localScale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g.transform;
    }

    // 화면 아래 안내 문구 (한글이라 윈도우 글꼴)
    void BuildPrompt()
    {
        GameObject root = new GameObject("HatchUI", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1f;

        GameObject g = new GameObject("Prompt", typeof(RectTransform));
        g.transform.SetParent(root.transform, false);
        prompt = g.AddComponent<Text>();
        prompt.font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 40);
        prompt.fontSize = 40;
        prompt.fontStyle = FontStyle.Bold;
        prompt.alignment = TextAnchor.MiddleCenter;
        prompt.horizontalOverflow = HorizontalWrapMode.Overflow;
        prompt.color = new Color(0.98f, 0.72f, 0.30f);
        prompt.text = bird.UsesMotionControl ? "고개를 좌우로 까딱여서 알을 깨요!" : "← → 를 번갈아 눌러 알을 깨요!";
        prompt.raycastTarget = false;
        Outline o = g.AddComponent<Outline>();
        o.effectColor = new Color(0.11f, 0.13f, 0.25f, 0.9f);
        o.effectDistance = new Vector2(2f, -2f);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 110f);
        rt.sizeDelta = new Vector2(1000f, 80f);
    }

    IEnumerator FadePrompt()
    {
        Color c = prompt.color;
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            prompt.color = new Color(c.r, c.g, c.b, 1f - t / 0.3f);
            yield return null;
        }
        prompt.enabled = false;
    }
}
