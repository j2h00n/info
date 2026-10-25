using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// 🧊 얼음 동굴 스테이지 300m: 동굴 붕괴 탈출 (35초 버티기)
// 1. 시작 컷씬 (위아래 검은 띠): 쿠르릉 땅울림 → 앞뒤로 고드름·얼음 덩어리가 떨어지기 시작 → "THE CAVE IS COLLAPSING!"
//    → 카메라가 새 둘레를 돌아 새 앞쪽으로 (독수리 추격전과 같은 시점: 새 얼굴과 뒤에서 무너져 오는 동굴이 보임)
// 2. 탈출: 화면 기준으로 좌우 조작이 맞게 반대로. 구간마다 정해진(고정) 패턴, 모든 구간에 고드름이 떨어짐
//    0~7초 고드름 / 7~14초 쓰러지는 얼음 기둥 / 14~21초 굴러오는 얼음 바위 / 21~28초 고드름 커튼 / 28~35초 마지막 붕괴
// 3. 35초 버티면 탈출 컷씬: 앞쪽 출구의 빛 속으로 → 하얗게 번쩍 → 바깥 설산에서 동굴 입구가 무너지는 걸 보며 날아 나옴 → 클리어
//
// 공격은 새를 따라 움직이는 "아레나" 안에서 계산 (로컬 x/y = 월드와 같음, z = 새로부터 앞(+)/뒤(-) 거리)
public class IceCaveEscape : MonoBehaviour
{
    [Header("연결")]
    public BirdController bird;
    public NewMonoBehaviourScript obstacleSpawner;
    public IceCaveTunnel tunnel;
    public GameObject outsideWorld;         // 바깥 설산 배경 (동굴 안에선 꺼 둠, 탈출하면 켬)
    public Material outsideSkybox;          // 바깥 하늘

    [Header("UI")]
    public GameObject bossHud;
    public RectTransform timeFill;
    public TextMeshProUGUI bossName;
    public TMP_FontAsset titleFont;
    public Material titleMaterial;

    [Header("머티리얼")]
    public Material icicleMat, beamMat, boulderMat, debrisMat, shardMat, dustMat, telegraphMat, lightMat, cliffMat, snowMat;

    [Header("사운드")]
    public AudioClip escapeBgm, rumbleSound, icicleSound, shatterSound, thudSound;

    [Header("설정")]
    public float surviveSeconds = 35f;
    public float telegraphTime = 1.8f;      // 빨간 경고가 뜨고 공격이 새에게 닿기까지 걸리는 시간 (모든 공격 공통)
    public float icicleFallSpeed = 42f;
    public float beamSpeed = 40f;
    public float boulderSpeed = 36f;
    public Vector3 chaseCamOffset = new Vector3(0f, 0.9f, 6.5f);  // 새 앞쪽 (조금 위)
    public Vector3 chaseCamLook = new Vector3(0f, 1.8f, -10f);   // 새 뒤쪽을 봄 → 새는 화면 아래쪽, 무너지는 동굴은 가운데
    public float chaseFov = 62f;

    // ── 상태 ──
    private bool running, finished, escaping;
    private float elapsed;
    private Transform arena;
    private AudioSource sfx, rumbleSrc;
    private Coroutine patternLoop;
    private readonly List<Hazard> hazards = new List<Hazard>();
    private readonly List<Transform> debris = new List<Transform>();
    private readonly List<float> debrisVy = new List<float>();        // 잔해마다 떨어지는 속도
    private readonly List<Vector3> debrisSpin = new List<Vector3>();  // 잔해마다 회전
    private float debrisTimer;
    private Vector3 birdStart;              // 씬 시작 때 새 위치 (바깥 배경 위치 맞출 때 씀)

    // 공격: 아레나 안에서 움직이며 매 프레임 판정
    private class Hazard
    {
        public Transform t;
        public Vector3 vel;
        public Vector3 spin;
        public GameObject mark;
        public System.Func<Vector3, Vector3, bool> hit;   // (이전 위치, 지금 위치) → 맞았나
        public System.Func<Vector3, bool> markOff;        // 경고를 지울 때
        public System.Func<Vector3, bool> done;           // 다 지나갔나
        public System.Action<Vector3> onDone;
    }

    // 카메라 (코루틴이 원하는 위치/방향/화각을 정하면 LateUpdate에서 적용)
    private Transform cam;
    private Camera camComp;
    private Vector3 camBasePos;
    private Quaternion camBaseRot;
    private float camBaseFov;
    private bool camControlled;
    private Vector3 camPos, camLook;
    private float camFov, camRoll;
    private float shakeTime, shakeDuration, shakeStrength, rumbleShake;

    // 경고 표시
    private readonly List<GameObject> telegraphs = new List<GameObject>();
    private readonly List<Renderer> teleFills = new List<Renderer>();
    private readonly List<Renderer> teleEdges = new List<Renderer>();

    // 컷씬 UI
    private Canvas cineCanvas;
    private RectTransform barTop, barBottom;
    private TextMeshProUGUI title, subtitle, callout;
    private Image whiteFlash;

    private bool Dead { get { return bird == null || bird.IsGameOver; } }
    private Vector3 BirdLocal { get { return new Vector3(bird.transform.position.x, bird.transform.position.y, 0f); } }
    private Vector3 ToWorld(Vector3 local) { return arena.TransformPoint(local); }

    void Start()
    {
        if (bird != null) birdStart = bird.transform.position;
    }

    // BirdController가 스토리 모드 300m에서 호출 (또는 메뉴의 탈출 바로 하기)
    public void Begin()
    {
        if (running) return;
        running = true;

        arena = new GameObject("CollapseArena").transform;
        arena.position = new Vector3(0f, 0f, bird.transform.position.z);

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        rumbleSrc = gameObject.AddComponent<AudioSource>();
        rumbleSrc.playOnAwake = false;
        rumbleSrc.loop = true;
        rumbleSrc.clip = rumbleSound;
        float v = PlayerPrefs.GetFloat("JumpVolume", 1f);
        sfx.volume = v * v; // 효과음 슬라이더와 같은 크기
        rumbleSrc.volume = v * v * 0.45f;

        camComp = Camera.main;
        cam = camComp.transform;
        camBasePos = cam.localPosition;
        camBaseRot = cam.localRotation;
        camBaseFov = camComp.fieldOfView;

        BuildCinemaUI();
        if (bossHud != null) bossHud.SetActive(false);
        if (obstacleSpawner != null) obstacleSpawner.StopAndScatter();
        StartCoroutine(Intro());
    }

    // ───────────────────────── 1. 시작 컷씬 ─────────────────────────

    IEnumerator Intro()
    {
        bird.cutscene = true;
        Rigidbody rb = bird.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        bird.transform.rotation = Quaternion.identity;
        float birdSpeed = bird.forwardSpeed;
        float cineY = Mathf.Clamp(bird.transform.position.y, 2f, 8f);
        camControlled = true;
        StartCoroutine(Letterbox(true, 0.5f));
        if (escapeBgm != null) StartCoroutine(SwapMusic(escapeBgm));

        // 쿠르릉: 땅울림 + 강한 흔들림
        Play(rumbleSound, 1f);
        if (rumbleSrc.clip != null) rumbleSrc.Play();
        Shake(1.2f, 0.35f);
        rumbleShake = 0.06f;
        escaping = true; // 뒤에서 무너지는 잔해가 떨어지기 시작

        bool titled = false, bigDrop = false;
        float t = 0f;
        while (t < 5f)
        {
            float dt = Time.deltaTime;
            t += dt;
            // 새: 놀라 잠깐 느려졌다가 다시 힘껏 날아감
            float speed = t < 0.6f ? Mathf.Lerp(birdSpeed, 7f, t / 0.6f) : t < 2.2f ? 7f : Mathf.Lerp(7f, birdSpeed, Mathf.Clamp01((t - 2.2f) / 1f));
            Vector3 bp = bird.transform.position;
            bp.z += speed * dt;
            bp.y = Mathf.Lerp(bp.y, cineY, dt * 1.5f);
            bp.x = Mathf.Lerp(bp.x, Mathf.Clamp(bp.x, -12f, 12f), dt);
            bird.transform.position = bp;
            arena.position = new Vector3(0f, 0f, bp.z);
            Vector3 me = BirdLocal;

            if (t < 1.8f)
            {
                // ── 장면 A: 새 뒤 위에서 – 바로 앞에 큰 고드름들이 쾅쾅 떨어짐 ──
                if (!bigDrop && t > 0.5f)
                {
                    bigDrop = true;
                    for (int i = 0; i < 4; i++) StartCoroutine(DecorIcicle(new Vector3(me.x + (i - 1.5f) * 6f, 30f, 14f + i * 5f), 0.12f * i));
                }
                camPos = ToWorld(me + new Vector3(0f, 3.5f, -9f));
                camLook = ToWorld(me + new Vector3(0f, 3f, 14f));
                camFov = Mathf.Lerp(camBaseFov, 64f, t / 1.8f);
                camRoll = 0f;
            }
            else if (t < 3.4f)
            {
                // ── 장면 B: 새 옆 아래에서 뒤를 돌아봄 – 동굴이 무너져 오는 게 보임 ──
                float u = (t - 1.8f) / 1.6f;
                if (!titled) { titled = true; StartCoroutine(ShowTitle("THE CAVE IS COLLAPSING!", "Escape before it buries you!", 1.5f)); Shake(0.6f, 0.25f); }
                camPos = ToWorld(me + new Vector3(4.5f, -1.2f, 3.5f));
                camLook = ToWorld(me + new Vector3(-1f, 2.5f, -18f));
                camFov = Mathf.Lerp(58f, 50f, u);
                camRoll = -5f;
            }
            else
            {
                // ── 장면 C: 카메라가 새 둘레를 돌아 새 앞쪽(탈출 시점)으로 ──
                float u = Mathf.SmoothStep(0f, 1f, (t - 3.4f) / 1.6f);
                Vector3 side = me + new Vector3(4.5f, -1.2f, 3.5f);
                camPos = ToWorld(Vector3.Lerp(side, me + chaseCamOffset, u));
                camLook = ToWorld(Vector3.Lerp(me + new Vector3(-1f, 2.5f, -18f), me + chaseCamLook, u));
                camFov = Mathf.Lerp(50f, chaseFov, u);
                camRoll = Mathf.Lerp(-5f, 0f, u);
            }
            yield return null;
        }

        // ── 탈출 시작 ──
        StartCoroutine(ShowTitle("ESCAPE!", Mathf.RoundToInt(surviveSeconds) + " seconds", 1.2f));
        StartCoroutine(Letterbox(false, 0.5f));
        rb.isKinematic = false;
        rb.linearVelocity = new Vector3(0f, 0f, birdSpeed);
        bird.cutscene = false;
        bird.mirrorControls = true;
        if (bossHud != null) bossHud.SetActive(true);
        elapsed = 0f;
        patternLoop = StartCoroutine(Patterns());
    }

    // ───────────────────────── 2. 고정 패턴 ─────────────────────────

    // 시간표: T = 공격이 새에게 닿는 시각(초). 위치는 그 공격의 경고가 뜰 때 새 위치 기준 (x 오프셋)
    private struct Step
    {
        public float time;
        public string kind;     // icicle / beam / boulder / curtain / callout
        public float[] xs;      // icicle: x 오프셋들, curtain: [틈 오프셋], boulder: x,y 쌍
        public string text;
        public float tele;
    }

    static Step Ice(float t, params float[] xs) { return new Step { time = t, kind = "icicle", xs = xs }; }
    static Step Beam(float t) { return new Step { time = t, kind = "beam" }; }
    static Step Rock(float t, params float[] xy) { return new Step { time = t, kind = "boulder", xs = xy }; }
    static Step Curtain(float t, float gap) { return new Step { time = t, kind = "curtain", xs = new[] { gap } }; }
    static Step Call(float t, string text) { return new Step { time = t, kind = "callout", text = text }; }

    List<Step> Schedule()
    {
        var s = new List<Step>
        {
            // 구간 1 (0~7초): 고드름 – 하나씩 → 둘씩, 내 자리에 꼭 하나
            Call(0.3f, "ICICLES!"),
            Ice(1.6f, 0f), Ice(3.0f, 0f, -6f), Ice(4.3f, 0f, 6f), Ice(5.6f, -3f, 3f), Ice(6.6f, 0f),
            // 구간 2 (7~14초): 쓰러져 날아오는 얼음 기둥(높이를 바꿔 피함) + 고드름
            Call(7.1f, "WATCH YOUR HEAD!"),
            Beam(8.0f), Ice(9.3f, 0f, -5f, 5f), Beam(10.4f), Beam(11.6f), Ice(11.6f, 0f), Ice(13.0f, -4f, 0f, 4f),
            // 구간 3 (14~21초): 뒤에서 굴러오는 얼음 바위 + 고드름
            Call(14.1f, "BOULDERS!"),
            Rock(15.0f, 0f, 0f), Rock(16.2f, 0f, 0f, -5f, 2f, 5f, -2f), Ice(17.4f, 0f, -7f, 7f),
            Rock(18.6f, 0f, 0f, 4f, 3f, -4f, 3f, 0f, -4f), Ice(19.9f, 0f), Rock(19.9f, 0f, 0f),
            // 구간 4 (21~28초): 고드름 커튼 – 한 칸만 비어 있음
            Call(21.1f, "FIND THE GAP!"),
            Curtain(22.4f, 4.5f), Curtain(24.2f, -4.5f), Curtain(25.8f, 1.5f), Beam(27.2f),
            // 구간 5 (28~35초): 마지막 붕괴 – 경고가 짧아지고 섞여서 옴
            Call(28.1f, "FINAL COLLAPSE!"),
            Beam(28.9f), Ice(28.9f, 0f), Rock(30.1f, 0f, 0f, -4f, 0f, 4f, 0f), Curtain(31.3f, -3f),
            Beam(32.6f), Rock(32.6f, 0f, 0f), Ice(33.8f, 0f, -4f, 4f),
        };
        for (int i = 0; i < s.Count; i++)
        {
            Step st = s[i];
            st.tele = telegraphTime;
            s[i] = st;
        }
        s.Sort((a, b) => (a.time - a.tele).CompareTo(b.time - b.tele));
        return s;
    }

    IEnumerator Patterns()
    {
        foreach (Step st in Schedule())
        {
            while (elapsed < st.time - st.tele) { if (Dead || finished) yield break; yield return null; }
            if (Dead || finished) yield break;
            switch (st.kind)
            {
                case "callout": StartCoroutine(ShowCallout(st.text)); break;
                case "icicle":
                    for (int i = 0; i < st.xs.Length; i++) StartCoroutine(Icicle(BirdLocal.x + st.xs[i], st.tele, i == 0));
                    break;
                case "curtain": StartCoroutine(CurtainRow(BirdLocal.x, st.xs[0], st.tele)); break;
                case "beam": StartCoroutine(IceBeam(st.tele)); break;
                case "boulder":
                    Vector3 me = BirdLocal;
                    for (int i = 0; i + 1 < st.xs.Length; i += 2) StartCoroutine(Boulder(me + new Vector3(st.xs[i], st.xs[i + 1], 0f), st.tele));
                    break;
            }
        }
    }

    // 고드름: 세로 띠 경고 → 위에서 내리꽂혀 바닥에 부딪혀 산산조각. 띠 안에 있으면 맞음
    IEnumerator Icicle(float x, float tele, bool sound = true)
    {
        x = Mathf.Clamp(x, -32f, 32f);
        GameObject mark = MakeRectTelegraph(new Vector3(x, 6f, 0f), new Vector2(2.4f, 30f));
        float targetY = Mathf.Clamp(BirdLocal.y, bird.fallLimitY, bird.ceilingY);
        float startY = targetY + 22f;
        float fallTime = (startY - targetY) / icicleFallSpeed;
        yield return Wait(tele - fallTime);
        if (Dead || finished) { RemoveTelegraph(mark); yield break; }

        Transform t = MakeIcicle(new Vector3(x, startY, 0f));
        if (sound) Play(icicleSound, Random.Range(0.95f, 1.1f));
        float floorY = tunnel != null ? tunnel.floorY + 1f : bird.fallLimitY - 8f;
        hazards.Add(new Hazard
        {
            t = t,
            vel = new Vector3(0f, -icicleFallSpeed, 0f),
            mark = mark,
            // 고드름 몸통(끝 ~ 뿌리 5m)과 새 사이 거리
            hit = (prev, cur) => DistanceToSegment(BirdLocal, cur, cur + Vector3.up * 5f) < 0.95f,
            markOff = cur => cur.y < BirdLocal.y - 1.5f,
            done = cur => cur.y < floorY,
            onDone = cur => { Shatter(ToWorld(cur), 10, 5f); },
        });
    }

    // 고드름 커튼: 새 자리 기준 ±12m에 3m 간격으로 고드름, gap 오프셋 자리만 비움
    IEnumerator CurtainRow(float centerX, float gap, float tele)
    {
        bool first = true;
        for (float dx = -12f; dx <= 12.01f; dx += 3f)
        {
            if (Mathf.Abs(dx - gap) < 1.6f) continue;
            StartCoroutine(Icicle(centerX + dx, tele, first)); // 소리는 하나만 (8개가 한꺼번에 울리면 시끄러움)
            first = false;
        }
        yield break;
    }

    // 얼음 기둥: 내 높이에 가로 띠 경고 → 뒤에서 굴러 떨어진 긴 얼음 기둥이 빙글빙글 돌며 날아와 지나감. 높이를 바꿔 피함
    IEnumerator IceBeam(float tele)
    {
        Vector3 target = ClampTarget(BirdLocal);
        Vector2 size = new Vector2(30f, 3.2f);
        GameObject mark = MakeRectTelegraph(target, size);
        const float startZ = -32f;
        yield return Wait(tele - (-startZ) / beamSpeed);
        if (Dead || finished) { RemoveTelegraph(mark); yield break; }

        Transform t = MakeBeam(new Vector3(target.x, target.y, startZ));
        Play(thudSound, 1.3f);
        float half = size.y * 0.5f - 0.35f; // 가장자리 스치면 봐줌
        hazards.Add(new Hazard
        {
            t = t,
            vel = new Vector3(0f, 0f, beamSpeed),
            spin = new Vector3(540f, 0f, 0f),
            mark = mark,
            hit = (prev, cur) => prev.z <= 0f && cur.z >= 0f && Mathf.Abs(BirdLocal.y - target.y) < half && Mathf.Abs(BirdLocal.x - target.x) < size.x * 0.5f,
            markOff = cur => cur.z >= -0.6f,
            done = cur => cur.z > 14f,
        });
    }

    // 얼음 바위: 원 경고 → 뒤에서 굴러 날아와 그 자리를 지나감
    IEnumerator Boulder(Vector3 target, float tele)
    {
        target = ClampTarget(target);
        GameObject mark = MakeCircleTelegraph(target, 1.9f);
        Vector3 start = new Vector3(target.x + Random.Range(-2f, 2f), target.y + 5f, -32f);
        yield return Wait(tele - Vector3.Distance(start, target) / boulderSpeed);
        if (Dead || finished) { RemoveTelegraph(mark); yield break; }

        Transform t = MakeBoulder(start);
        Vector3 vel = (target - start).normalized * boulderSpeed;
        hazards.Add(new Hazard
        {
            t = t,
            vel = vel,
            spin = new Vector3(Random.Range(200f, 400f), Random.Range(-100f, 100f), 0f),
            mark = mark,
            hit = (prev, cur) => DistanceToSegment(BirdLocal, prev, cur) < 1.5f,
            markOff = cur => cur.z >= -0.6f,
            done = cur => cur.z > 14f,
        });
    }

    IEnumerator Wait(float seconds)
    {
        float w = 0f;
        while (w < seconds && !Dead && !finished)
        {
            w += Time.deltaTime;
            PulseTelegraphs(0.5f);
            yield return null;
        }
    }

    // ───────────────────────── 3. 탈출 컷씬 ─────────────────────────

    // 1. 카메라가 새 뒤로 돌아가고, 앞쪽 출구의 하얀 빛이 점점 커짐 (뒤에선 계속 무너짐)
    // 2. 빛 속으로 → 화면이 하얗게
    // 3. 바깥: 설산 절벽의 동굴 입구에서 새가 튀어나오고, 입구가 무너져 막힘 → "ESCAPED!" → 클리어
    IEnumerator Victory()
    {
        finished = true;
        if (patternLoop != null) StopCoroutine(patternLoop);
        ClearTelegraphs();
        foreach (Hazard h in hazards) if (h.t != null) Destroy(h.t.gameObject);
        hazards.Clear();
        bird.mirrorControls = false;
        if (bossHud != null) bossHud.SetActive(false);
        StartCoroutine(Letterbox(true, 0.4f));

        bird.cutscene = true;
        Rigidbody rb = bird.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        float birdSpeed = bird.forwardSpeed;
        float flyY = Mathf.Clamp(bird.transform.position.y, 2f, 9f);

        // 출구: 지금 위치에서 약 80m 앞 (터널 조각 경계)
        float exitZ = tunnel != null ? tunnel.SetExit(bird.transform.position.z + 70f) : bird.transform.position.z + 80f;
        Transform exitLight = MakeExitLight(exitZ);

        float t = 0f;
        Vector3 me = BirdLocal;
        while (true)
        {
            float dt = Time.deltaTime;
            t += dt;
            Vector3 bp = bird.transform.position;
            float speed = Mathf.Lerp(birdSpeed, birdSpeed * 2.6f, Mathf.Clamp01(t / 1.2f));
            bp.z += speed * dt;
            bp.x = Mathf.Lerp(bp.x, 0f, dt * 1.2f);
            bp.y = Mathf.Lerp(bp.y, flyY, dt * 2f);
            bird.transform.position = bp;
            bird.transform.rotation = Quaternion.Slerp(bird.transform.rotation, Quaternion.identity, dt * 5f);
            arena.position = new Vector3(0f, 0f, bp.z);
            me = BirdLocal;

            // 카메라: 새 앞쪽 → 새 둘레를 반 바퀴 돌아 뒤쪽 위로 (출구 빛이 화면 가운데)
            float orbit = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.4f));
            camPos = ToWorld(me + OrbitOffset(orbit));
            camLook = ToWorld(me + Vector3.Lerp(new Vector3(0f, 1.5f, 14f), chaseCamLook, orbit));
            camFov = Mathf.Lerp(camBaseFov + 6f, chaseFov, orbit);
            camRoll = 0f;

            // 출구에 가까워질수록 화면이 하얗게
            float toExit = exitZ - bp.z;
            float glow = Mathf.Clamp01(1f - (toExit - 6f) / 40f);
            whiteFlash.color = new Color(1f, 1f, 1f, glow * glow);
            if (exitLight != null) exitLight.localScale = Vector3.one * (1f + glow * 0.3f);
            if (toExit < 6f) break;
            yield return null;
        }
        whiteFlash.color = Color.white;

        // ── 하얀 화면 뒤에서 바깥으로 바꿈 ──
        escaping = false;
        foreach (Transform d in debris) if (d != null) Destroy(d.gameObject);
        debris.Clear();
        debrisVy.Clear();
        debrisSpin.Clear();
        rumbleShake = 0f;
        if (tunnel != null) tunnel.gameObject.SetActive(false);
        if (exitLight != null) Destroy(exitLight.gameObject);
        GoOutside();
        Transform mouth = BuildCaveMouth(exitZ, flyY);

        // 새는 입구 바로 바깥, 카메라는 입구 앞쪽 비스듬히에서 입구를 돌아봄
        Vector3 b0 = new Vector3(0f, flyY, exitZ + 3f);
        bird.transform.position = b0;
        arena.position = new Vector3(0f, 0f, b0.z);
        // 카메라: 입구에서 60m 떨어진 왼쪽 아래. 새가 입구에서 튀어나와 카메라 오른쪽으로 스쳐 지나가고, 뒤로 입구가 무너짐
        Vector3 mouthCenter = new Vector3(0f, flyY + 2f, exitZ);
        camPos = new Vector3(-7f, flyY + 0.5f, exitZ + 48f);
        camLook = mouthCenter;
        camFov = 56f;

        bool collapsed = false, titled = false, music = false;
        float o = 0f;
        while (o < 4.2f)
        {
            float dt = Time.deltaTime;
            o += dt;
            whiteFlash.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - o / 0.6f));
            Vector3 bp = bird.transform.position;
            bp.z += birdSpeed * 1.4f * dt;
            bp.y += Mathf.Sin(o * 2f) * 0.5f * dt + 0.6f * dt; // 바깥 공기를 마시며 살짝 솟아오름
            bird.transform.position = bp;
            arena.position = new Vector3(0f, 0f, bp.z);

            // 카메라: 입구를 계속 보며 아주 천천히 다가감 (새는 화면 오른쪽으로 빠져나감)
            camPos += Vector3.forward * -1.5f * dt;
            camLook = mouthCenter + Vector3.up * (o * 0.8f);

            if (!collapsed && o > 0.5f) { collapsed = true; StartCoroutine(CollapseMouth(mouth, exitZ, flyY)); }
            if (!titled && o > 1.1f) { titled = true; StartCoroutine(ShowTitle("ESCAPED!", "You made it out of the Ice Cave", 1.8f)); }
            if (!music && o > 0.3f) { music = true; StartCoroutine(SwapMusic(bird.StoryMusic)); }
            yield return null;
        }

        // 새 뒤 시점으로 돌아와 클리어
        if (rumbleSrc != null) rumbleSrc.Stop();
        StartCoroutine(Letterbox(false, 0.4f));
        camControlled = false;
        cam.localPosition = camBasePos;
        cam.localRotation = camBaseRot;
        camComp.fieldOfView = camBaseFov;
        bird.cutscene = false;
        rb.isKinematic = false;
        if (!Dead) bird.StageClear();
    }

    // 카메라가 새 둘레를 도는 위치: 0 = 뒤(원래 시점), 1 = 앞(탈출 시점)
    Vector3 OrbitOffset(float k)
    {
        float ang = k * Mathf.PI;
        float radius = Mathf.Lerp(7f, chaseCamOffset.z, k);
        return new Vector3(Mathf.Sin(ang) * radius * 0.7f, Mathf.Lerp(2f, chaseCamOffset.y, k), -Mathf.Cos(ang) * radius);
    }

    // 바깥: 설산 배경을 켜고 하늘·빛·안개를 밝은 낮으로
    void GoOutside()
    {
        if (outsideWorld != null)
        {
            // 배경은 새를 따라다님 → 씬 시작 때의 간격 그대로 지금 새 위치에 맞춰 놓고 켬
            Vector3 offset = outsideWorld.transform.position - birdStart;
            outsideWorld.transform.position = new Vector3(0f, 0f, bird.transform.position.z) + new Vector3(0f, birdStart.y, 0f) + offset;
            outsideWorld.SetActive(true);
        }
        if (outsideSkybox != null) RenderSettings.skybox = outsideSkybox;
        RenderSettings.fogColor = new Color(0.82f, 0.9f, 0.98f);
        RenderSettings.fogStartDistance = 120f;
        RenderSettings.fogEndDistance = 1800f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.85f, 0.92f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.7f, 0.78f, 0.88f);
        RenderSettings.ambientGroundColor = new Color(0.55f, 0.6f, 0.7f);
        Light sun = RenderSettings.sun;
        if (sun == null) { GameObject l = GameObject.Find("Directional Light"); if (l != null) sun = l.GetComponent<Light>(); }
        if (sun != null)
        {
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 1.4f;
            sun.transform.rotation = Quaternion.Euler(35f, 155f, 0f); // 입구 앞면(새가 날아 나오는 쪽)을 비춤
        }
        camComp.clearFlags = CameraClearFlags.Skybox;
    }

    // ───────────────────────── 매 프레임 ─────────────────────────

    void Update()
    {
        if (!running || finished || Dead || bird.cutscene || patternLoop == null) return;
        elapsed += Time.deltaTime;
        float left = Mathf.Max(0f, surviveSeconds - elapsed);
        if (timeFill != null) timeFill.anchorMax = new Vector2(left / surviveSeconds, 1f);
        if (bossName != null) bossName.text = "ESCAPE THE CAVE   " + Mathf.CeilToInt(left) + "s";
        if (left <= 0f) StartCoroutine(Victory());
    }

    void LateUpdate()
    {
        if (!running || arena == null) return;
        if (!finished) arena.position = new Vector3(0f, 0f, bird.transform.position.z);

        // 공격 이동 + 판정
        for (int i = hazards.Count - 1; i >= 0; i--)
        {
            Hazard h = hazards[i];
            if (h.t == null) { hazards.RemoveAt(i); continue; }
            Vector3 prev = h.t.localPosition;
            Vector3 cur = prev + h.vel * Time.deltaTime;
            h.t.localPosition = cur;
            if (h.spin != Vector3.zero) h.t.localRotation *= Quaternion.Euler(h.spin * Time.deltaTime);
            if (!Dead && !finished && h.hit != null && h.hit(prev, cur))
            {
                Play(shatterSound, 1f);
                Shake(0.4f, 0.4f);
                bird.GameOver();
            }
            if (h.mark != null && h.markOff != null && h.markOff(cur)) { RemoveTelegraph(h.mark); h.mark = null; }
            if (h.done != null && h.done(cur))
            {
                if (h.mark != null) RemoveTelegraph(h.mark);
                if (h.onDone != null) h.onDone(cur);
                Destroy(h.t.gameObject);
                hazards.RemoveAt(i);
            }
        }
        if (telegraphs.Count > 0) PulseTelegraphs(1f);

        UpdateDebris();
        UpdateCamera();
    }

    void UpdateCamera()
    {
        if (cam == null || !camControlled) return;
        if (bird.mirrorControls && !finished)
        {
            // 탈출 중: 새 앞쪽 위에서 새 얼굴과 뒤에서 무너지는 동굴을 같이 봄
            Vector3 me = bird.transform.position;
            camPos = me + chaseCamOffset;
            camLook = me + chaseCamLook;
            camFov = chaseFov;
            camRoll = 0f;
        }
        Vector3 pos = camPos;
        if (shakeTime > 0f)
        {
            shakeTime -= Time.deltaTime;
            pos += Random.insideUnitSphere * shakeStrength * Mathf.Clamp01(shakeTime / shakeDuration);
        }
        if (rumbleShake > 0f) pos += Random.insideUnitSphere * rumbleShake; // 무너지는 내내 잔잔하게 흔들림
        cam.position = pos;
        cam.rotation = Quaternion.LookRotation(camLook - camPos) * Quaternion.Euler(0f, 0f, camRoll);
        camComp.fieldOfView = camFov;
    }

    // 뒤쪽(새 뒤 12~60m)과 양옆에 계속 떨어지는 잔해: 보기만 (판정 없음). 앞쪽 시점에서 무너져 오는 느낌
    void UpdateDebris()
    {
        if (escaping && !Dead)
        {
            debrisTimer -= Time.deltaTime;
            while (debrisTimer <= 0f)
            {
                debrisTimer += finished ? 0.05f : 0.09f;
                float side = Random.value < 0.5f ? -1f : 1f;
                Vector3 local = new Vector3(BirdLocal.x * 0.5f + Random.Range(-22f, 22f), 32f, Random.Range(-60f, -12f));
                if (Random.value < 0.35f) local = new Vector3(side * Random.Range(14f, 30f), 32f, Random.Range(-10f, 40f)); // 옆쪽에도 가끔
                debris.Add(MakeDebris(ToWorld(local)));
                debrisVy.Add(Random.Range(-8f, 0f));
                debrisSpin.Add(Random.insideUnitSphere * 200f);
            }
        }
        float floorY = tunnel != null ? tunnel.floorY : -14f;
        for (int i = debris.Count - 1; i >= 0; i--)
        {
            Transform d = debris[i];
            bool gone = d == null;
            if (!gone)
            {
                debrisVy[i] -= 35f * Time.deltaTime;
                d.position += Vector3.up * debrisVy[i] * Time.deltaTime;
                d.Rotate(debrisSpin[i] * Time.deltaTime, Space.World);
                if (d.position.y < floorY + 0.5f || d.position.z < bird.transform.position.z - 90f)
                {
                    if (d.position.y < floorY + 0.5f && Random.value < 0.3f) Puff(d.position, 2.5f);
                    Destroy(d.gameObject);
                    gone = true;
                }
            }
            if (gone) { debris.RemoveAt(i); debrisVy.RemoveAt(i); debrisSpin.RemoveAt(i); }
        }
    }

    // ───────────────────────── 모양 만들기 ─────────────────────────

    Transform MakeIcicle(Vector3 local)
    {
        GameObject g = new GameObject("FallingIcicle");
        g.transform.SetParent(arena, false);
        g.transform.localPosition = local;
        var kit = new MeshKit();
        IceCaveTunnel.Cone(kit, new Vector3(0f, 5f, 0f), Vector3.down, 5f, 0.6f, 6);       // 끝이 아래 (로컬 원점 = 끝)
        IceCaveTunnel.Cone(kit, new Vector3(0.35f, 4.6f, 0.1f), Vector3.down, 2.2f, 0.3f, 5); // 옆에 작은 고드름
        g.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("FallingIcicle");
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = icicleMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    // 긴 얼음 기둥 (가로로 누운 육각 기둥, 양 끝이 깨진 듯 뾰족)
    Transform MakeBeam(Vector3 local)
    {
        GameObject g = new GameObject("IceBeam");
        g.transform.SetParent(arena, false);
        g.transform.localPosition = local;
        var kit = new MeshKit();
        IceCaveTunnel.Crystal(kit, new Vector3(-15f, 0f, 0f), Vector3.right, 15f, 0.9f);
        IceCaveTunnel.Crystal(kit, new Vector3(15f, 0f, 0f), Vector3.left, 15f, 0.9f);
        g.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("IceBeam");
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = beamMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    // 울퉁불퉁한 얼음 바위
    Transform MakeBoulder(Vector3 local)
    {
        GameObject g = new GameObject("IceBoulder");
        g.transform.SetParent(arena, false);
        g.transform.localPosition = local;
        g.AddComponent<MeshFilter>().sharedMesh = RoughRock(1.25f, "IceBoulder");
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = boulderMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    Transform MakeDebris(Vector3 world)
    {
        GameObject g = new GameObject("CaveDebris");
        g.transform.position = world;
        g.transform.rotation = Random.rotation;
        float s = Random.Range(0.5f, 2.2f);
        g.transform.localScale = Vector3.one * s;
        g.AddComponent<MeshFilter>().sharedMesh = RoughRock(1f, "Debris");
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = Random.value < 0.4f ? icicleMat : debrisMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    // 시작 컷씬: 새 앞쪽에 쾅 떨어지는 큰 고드름 (보기만)
    IEnumerator DecorIcicle(Vector3 local, float delay)
    {
        yield return new WaitForSeconds(delay);
        Transform t = MakeIcicle(local);
        t.localScale = Vector3.one * 1.8f;
        Play(icicleSound, 0.8f);
        float floorY = tunnel != null ? tunnel.floorY + 1f : -12f;
        float vy = -10f;
        while (t != null && t.localPosition.y > floorY)
        {
            vy -= 60f * Time.deltaTime;
            t.localPosition += Vector3.up * vy * Time.deltaTime;
            yield return null;
        }
        if (t == null) yield break;
        Shatter(t.position, 14, 7f);
        Puff(t.position, 5f);
        Shake(0.25f, 0.2f);
        Destroy(t.gameObject);
    }

    // 꼭짓점을 흐트러뜨린 20면체 비슷한 덩어리 (면마다 따로 → 각진 로우폴리)
    static Mesh RoughRock(float radius, string name)
    {
        var kit = new MeshKit();
        float p = (1f + Mathf.Sqrt(5f)) * 0.5f;
        Vector3[] v =
        {
            new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
            new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
            new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1),
        };
        for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized * radius * Random.Range(0.75f, 1.15f);
        int[] f =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        for (int i = 0; i < f.Length; i += 3)
        {
            Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
            kit.Tri(a, b, c, a + b + c);
        }
        return kit.ToMesh(name);
    }

    // 산산조각 (보기만)
    void Shatter(Vector3 at, int count, float power)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(s.GetComponent<Collider>()); // 이름이 "Cube"여도 판정 안 걸리게
            s.name = "IceShard";
            Renderer r = s.GetComponent<Renderer>();
            r.sharedMaterial = shardMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.transform.position = at + Random.insideUnitSphere * 0.5f;
            s.transform.localScale = new Vector3(Random.Range(0.1f, 0.4f), Random.Range(0.08f, 0.3f), Random.Range(0.1f, 0.35f));
            s.transform.rotation = Random.rotation;
            StartCoroutine(FlyBit(s.transform, Random.insideUnitSphere * power + Vector3.up * power * 0.6f, 1.2f, false));
        }
    }

    // 먼지(눈가루) 뭉게: 커지며 옅어짐
    void Puff(Vector3 at, float size)
    {
        if (dustMat == null) return;
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(g.GetComponent<Collider>());
        g.name = "Dust";
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = dustMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        g.transform.position = at;
        g.transform.localScale = Vector3.one * size * 0.4f;
        StartCoroutine(GrowDust(g.transform, size));
    }

    IEnumerator GrowDust(Transform g, float size)
    {
        Renderer r = g.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        for (float t = 0f; t < 1.4f && g != null; t += Time.deltaTime)
        {
            float u = t / 1.4f;
            g.localScale = Vector3.one * size * Mathf.Lerp(0.4f, 1.6f, Mathf.Sqrt(u));
            g.position += Vector3.up * Time.deltaTime * 1.2f;
            block.SetColor("_BaseColor", new Color(0.9f, 0.95f, 1f, 0.55f * (1f - u)));
            r.SetPropertyBlock(block);
            yield return null;
        }
        if (g != null) Destroy(g.gameObject);
    }

    IEnumerator FlyBit(Transform s, Vector3 v, float life, bool keepScale)
    {
        Vector3 spin = Random.insideUnitSphere * 500f;
        for (float t = 0f; t < life && s != null; t += Time.deltaTime)
        {
            v += Physics.gravity * Time.deltaTime;
            s.position += v * Time.deltaTime;
            s.Rotate(spin * Time.deltaTime, Space.World);
            if (!keepScale) s.localScale *= 1f - Time.deltaTime * 0.5f;
            yield return null;
        }
        if (s != null) Destroy(s.gameObject);
    }

    // 출구의 빛: 터널 끝을 막는 하얀 판 + 그 둘레로 퍼지는 빛 (보기만)
    Transform MakeExitLight(float exitZ)
    {
        if (lightMat == null) return null;
        Transform root = new GameObject("ExitLight").transform;
        root.position = new Vector3(0f, 10f, exitZ + 2f);
        // 가운데가 가장 밝고 가장자리로 부드럽게 퍼지는 빛 (색을 1보다 훨씬 밝게 해서 동굴 안개에 묻혀 회색이 되지 않게)
        Texture2D glowTex = SoftGlowTexture();
        var block = new MaterialPropertyBlock();
        block.SetTexture("_BaseMap", glowTex);
        block.SetColor("_BaseColor", new Color(3f, 3f, 3.2f, 1f));
        for (int i = 0; i < 3; i++)
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Glow";
            q.transform.SetParent(root, false);
            q.transform.localPosition = new Vector3(0f, 0f, i * 0.5f);
            q.transform.localScale = new Vector3(120f + i * 70f, 80f + i * 45f, 1f);
            Renderer r = q.GetComponent<Renderer>();
            r.sharedMaterial = lightMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.SetPropertyBlock(block);
        }
        return root;
    }

    static Texture2D SoftGlowTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * size + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // 바깥에서 본 동굴 입구: 눈 덮인 큰 산 밑자락에 뚫린 구멍
    // 구멍 둘레는 각진 얼음 바위를 아치 모양으로 쌓고, 그 뒤로 산(거대한 바위 덩어리 + 눈 모자), 옆으로는 하늘이 보이게
    Transform BuildCaveMouth(float exitZ, float flyY)
    {
        Transform root = new GameObject("CaveMouth").transform;
        root.position = new Vector3(0f, 0f, exitZ);
        const float holeW = 34f, holeH = 30f;
        float holeBottom = flyY - 12f;
        float cx = 0f, cy = holeBottom;

        // 뒤의 산: 아래가 넓고 위가 뾰족한 바위 덩어리 몇 개 + 꼭대기 눈
        // (바위 덩어리는 반지름이 최대 1.15배까지 울퉁불퉁 → 앞쪽 끝이 입구 벽(z 0) 뒤에 머물도록 충분히 뒤에 둠)
        Rock(root, cliffMat, new Vector3(0f, cy + 10f, -150f), new Vector3(150f, 95f, 90f));
        Rock(root, cliffMat, new Vector3(-110f, cy - 5f, -125f), new Vector3(90f, 60f, 70f));
        Rock(root, cliffMat, new Vector3(115f, cy - 8f, -120f), new Vector3(85f, 55f, 70f));
        Rock(root, snowMat, new Vector3(0f, cy + 85f, -155f), new Vector3(70f, 32f, 50f));
        Rock(root, snowMat, new Vector3(-110f, cy + 42f, -127f), new Vector3(40f, 18f, 32f));
        Rock(root, snowMat, new Vector3(115f, cy + 36f, -122f), new Vector3(38f, 16f, 30f));

        // 산 앞자락: 구멍만 비워 두고 큰 바위를 촘촘히. 가운데가 높고 바깥으로 갈수록 낮아지는 산자락 모양 → 양옆 위로 하늘이 보임
        for (float y = cy - 24f; y < cy + holeH + 40f; y += 9f)
            for (float x = -56f; x <= 56f; x += 10f)
            {
                float nx = (x - cx) / (holeW * 0.5f + 3f), ny = (y - cy) / (holeH + 3f);
                if (ny > 0f && nx * nx + ny * ny < 1f) continue; // 아치 모양 구멍
                float top = cy + holeH + 38f - Mathf.Pow(Mathf.Abs(x) / 56f, 1.3f) * 55f + Random.Range(-4f, 4f);
                if (y > top) continue;
                Rock(root, y > top - 10f ? snowMat : cliffMat, // 산자락 꼭대기 줄은 눈
                    new Vector3(x + Random.Range(-2f, 2f), y + Random.Range(-2f, 2f), Random.Range(-6f, 2f)),
                    Vector3.one * Random.Range(7f, 11f));
            }
        // 구멍 안쪽: 어두운 굴 (뒤의 산이 비쳐 보이지 않게)
        Transform inside = Rock(root, cliffMat, new Vector3(cx, cy + holeH * 0.45f, -36f), new Vector3(holeW * 0.75f, holeH * 0.8f, 26f));
        var dark = new MaterialPropertyBlock();
        dark.SetColor("_BaseColor", new Color(0.03f, 0.05f, 0.09f));
        inside.GetComponent<Renderer>().SetPropertyBlock(dark);
        // 구멍 테두리: 작은 바위로 아치를 또렷하게
        for (int i = 0; i < 24; i++)
        {
            float a = Mathf.PI * i / 23f;
            Vector3 p = new Vector3(cx - Mathf.Cos(a) * (holeW * 0.5f + 1.5f), cy + Mathf.Sin(a) * (holeH + 1.5f), 4f);
            Rock(root, cliffMat, p, Vector3.one * Random.Range(3f, 5f));
        }
        // 고드름: 아치 윗부분에 매달림
        var kit = new MeshKit();
        for (int i = 0; i < 14; i++)
        {
            float a = Mathf.Lerp(0.35f, Mathf.PI - 0.35f, i / 13f);
            Vector3 top = new Vector3(cx - Mathf.Cos(a) * holeW * 0.5f, cy + Mathf.Sin(a) * holeH - 0.5f, 5f);
            IceCaveTunnel.Cone(kit, top, Vector3.down, Random.Range(2f, 5f), Random.Range(0.4f, 0.8f), 6);
        }
        GameObject ic = new GameObject("MouthIcicles");
        ic.transform.SetParent(root, false);
        ic.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("MouthIcicles");
        ic.AddComponent<MeshRenderer>().sharedMaterial = icicleMat;
        return root;
    }

    // 입구가 무너짐: 위에서 큰 바위들이 쏟아져 구멍을 막고 눈가루가 펑
    IEnumerator CollapseMouth(Transform mouth, float exitZ, float flyY)
    {
        Play(rumbleSound, 0.9f);
        Play(thudSound, 0.8f);
        Shake(1.2f, 0.25f);
        float holeBottom = flyY - 12f;
        var rocks = new List<Transform>();
        var vel = new List<float>();
        for (int i = 0; i < 22; i++)
        {
            Transform r = Rock(mouth, cliffMat, new Vector3(Random.Range(-16f, 16f), holeBottom + 32f + Random.Range(0f, 25f), Random.Range(4f, 9f)), Vector3.one * Random.Range(4f, 8f));
            rocks.Add(r);
            vel.Add(Random.Range(-6f, 0f));
        }
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)
        {
            for (int i = 0; i < rocks.Count; i++)
            {
                Transform r = rocks[i];
                float stopY = holeBottom + 2f + (i % 6) * 4.5f; // 아래부터 차곡차곡 쌓여 구멍을 막음
                if (r.localPosition.y <= stopY) continue;
                vel[i] -= 40f * Time.deltaTime;
                Vector3 lp = r.localPosition;
                lp.y = Mathf.Max(stopY, lp.y + vel[i] * Time.deltaTime);
                r.localPosition = lp;
                r.Rotate(Vector3.right * 120f * Time.deltaTime, Space.Self);
                if (lp.y <= stopY) Puff(r.position, 7f);
            }
            yield return null;
        }
    }

    Transform Rock(Transform parent, Material mat, Vector3 localPos, Vector3 size)
    {
        GameObject g = new GameObject("MouthRock");
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f));
        g.transform.localScale = size;
        g.AddComponent<MeshFilter>().sharedMesh = RoughRock(1f, "MouthRock");
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return g.transform;
    }

    // ───────────────────────── 경고 표시 (EagleChase와 같은 방식) ─────────────────────────

    GameObject MakeCircleTelegraph(Vector3 center, float radius)
    {
        Transform root = new GameObject("CollapseWarning").transform;
        root.SetParent(arena, false);
        root.localPosition = new Vector3(center.x, center.y, -0.6f);
        var fill = new MeshKit();
        var ring = new MeshKit();
        const int seg = 28;
        for (int i = 0; i < seg; i++)
        {
            float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
            fill.Tri(Vector3.zero, d0 * radius, d1 * radius, Vector3.forward);
            Vector3 o0 = d0 * radius, o1 = d1 * radius, i0 = d0 * (radius - 0.25f), i1 = d1 * (radius - 0.25f);
            ring.Tri(i0, o0, o1, Vector3.forward);
            ring.Tri(i0, o1, i1, Vector3.forward);
        }
        AddTelegraphMesh(root, fill.ToMesh("WarnFill"), true);
        AddTelegraphMesh(root, ring.ToMesh("WarnRing"), false);
        telegraphs.Add(root.gameObject);
        PulseTelegraphs(0f);
        return root.gameObject;
    }

    GameObject MakeRectTelegraph(Vector3 center, Vector2 size)
    {
        Transform root = new GameObject("CollapseWarning").transform;
        root.SetParent(arena, false);
        root.localPosition = new Vector3(center.x, center.y, -0.6f);
        float hx = size.x * 0.5f, hy = size.y * 0.5f, e = 0.25f;
        var fill = new MeshKit();
        fill.Prism(new[] { new Vector3(-hx, -hy, 0f), new Vector3(hx, -hy, 0f), new Vector3(hx, hy, 0f), new Vector3(-hx, hy, 0f) }, Vector3.forward, 0.01f);
        var edge = new MeshKit();
        edge.Prism(new[] { new Vector3(-hx, hy - e, 0f), new Vector3(hx, hy - e, 0f), new Vector3(hx, hy, 0f), new Vector3(-hx, hy, 0f) }, Vector3.forward, 0.02f);
        edge.Prism(new[] { new Vector3(-hx, -hy, 0f), new Vector3(hx, -hy, 0f), new Vector3(hx, -hy + e, 0f), new Vector3(-hx, -hy + e, 0f) }, Vector3.forward, 0.02f);
        edge.Prism(new[] { new Vector3(-hx, -hy, 0f), new Vector3(-hx + e, -hy, 0f), new Vector3(-hx + e, hy, 0f), new Vector3(-hx, hy, 0f) }, Vector3.forward, 0.02f);
        edge.Prism(new[] { new Vector3(hx - e, -hy, 0f), new Vector3(hx, -hy, 0f), new Vector3(hx, hy, 0f), new Vector3(hx - e, hy, 0f) }, Vector3.forward, 0.02f);
        AddTelegraphMesh(root, fill.ToMesh("WarnFill"), true);
        AddTelegraphMesh(root, edge.ToMesh("WarnEdge"), false);
        telegraphs.Add(root.gameObject);
        PulseTelegraphs(0f);
        return root.gameObject;
    }

    void AddTelegraphMesh(Transform root, Mesh mesh, bool isFill)
    {
        GameObject g = new GameObject(isFill ? "Fill" : "Edge");
        g.transform.SetParent(root, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = telegraphMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        (isFill ? teleFills : teleEdges).Add(r);
    }

    void PulseTelegraphs(float progress)
    {
        float speed = Mathf.Lerp(6f, 24f, progress);
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * speed);
        var fill = new MaterialPropertyBlock();
        fill.SetColor("_BaseColor", new Color(1f, 0.05f, 0.03f, Mathf.Lerp(0.22f, 0.45f, blink)));
        var edge = new MaterialPropertyBlock();
        edge.SetColor("_BaseColor", new Color(1f, Mathf.Lerp(0.05f, 0.4f, blink), 0.05f, 0.95f));
        foreach (Renderer r in teleFills) if (r != null) r.SetPropertyBlock(fill);
        foreach (Renderer r in teleEdges) if (r != null) r.SetPropertyBlock(edge);
    }

    void RemoveTelegraph(GameObject g)
    {
        if (g == null) return;
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>()) { teleFills.Remove(r); teleEdges.Remove(r); }
        telegraphs.Remove(g);
        Destroy(g);
    }

    void ClearTelegraphs()
    {
        foreach (GameObject g in telegraphs) if (g != null) Destroy(g);
        telegraphs.Clear();
        teleFills.Clear();
        teleEdges.Clear();
    }

    // ───────────────────────── 도우미 ─────────────────────────

    Vector3 ClampTarget(Vector3 p)
    {
        return new Vector3(Mathf.Clamp(p.x, -32f, 32f), Mathf.Clamp(p.y, bird.fallLimitY + 1f, bird.ceilingY - 1f), 0f);
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len = ab.sqrMagnitude;
        float t = len > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    void Play(AudioClip clip, float pitch)
    {
        if (clip == null || sfx == null) return;
        sfx.pitch = pitch;
        sfx.PlayOneShot(clip);
    }

    void Shake(float duration, float strength)
    {
        shakeDuration = duration;
        shakeTime = duration;
        shakeStrength = strength;
    }

    // ── 컷씬 UI: 위아래 검은 띠 + 큰 제목 + 구간 안내 + 하얀 번쩍임 ──

    void BuildCinemaUI()
    {
        GameObject root = new GameObject("CollapseCinema", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        cineCanvas = root.AddComponent<Canvas>();
        cineCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        cineCanvas.sortingOrder = 40;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1f;

        GameObject wf = new GameObject("WhiteFlash", typeof(RectTransform));
        wf.transform.SetParent(cineCanvas.transform, false);
        whiteFlash = wf.AddComponent<Image>();
        whiteFlash.color = new Color(1f, 1f, 1f, 0f);
        whiteFlash.raycastTarget = false;
        RectTransform wrt = (RectTransform)wf.transform;
        wrt.anchorMin = Vector2.zero; wrt.anchorMax = Vector2.one;
        wrt.offsetMin = wrt.offsetMax = Vector2.zero;

        barTop = Bar("BarTop", new Vector2(0.5f, 1f));
        barBottom = Bar("BarBottom", new Vector2(0.5f, 0f));
        title = Text("Title", 76, new Vector2(0f, -150f));
        title.color = new Color(0.72f, 0.92f, 1f);
        subtitle = Text("Subtitle", 30, new Vector2(0f, -210f));
        subtitle.color = new Color(0.965f, 0.906f, 0.757f);
        // 구간 안내: 화면 위 HUD 바로 아래
        callout = Text("Callout", 40, new Vector2(0f, 0f));
        callout.color = new Color(1f, 0.82f, 0.4f);
        RectTransform crt = callout.rectTransform;
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 1f);
        crt.anchoredPosition = new Vector2(0f, -130f);
    }

    RectTransform Bar(string name, Vector2 pivot)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(cineCanvas.transform, false);
        Image img = g.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = new Vector2(0f, pivot.y);
        rt.anchorMax = new Vector2(1f, pivot.y);
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(0f, 0f);
        return rt;
    }

    TextMeshProUGUI Text(string name, float size, Vector2 pos)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(cineCanvas.transform, false);
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        if (titleFont != null) t.font = titleFont;
        if (titleMaterial != null) t.fontSharedMaterial = titleMaterial;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.text = "";
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1200f, size * 1.4f);
        rt.anchoredPosition = pos;
        return t;
    }

    IEnumerator Letterbox(bool on, float duration)
    {
        float from = barTop.sizeDelta.y, to = on ? 90f : 0f, t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float h = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, h);
            yield return null;
        }
        barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, to);
    }

    IEnumerator ShowTitle(string main, string sub, float hold)
    {
        title.text = main;
        subtitle.text = sub;
        float t = 0f, total = hold + 0.6f;
        while (t < total)
        {
            t += Time.deltaTime;
            float appear = Mathf.Clamp01(t / 0.2f);
            float fade = Mathf.Clamp01((total - t) / 0.4f);
            title.alpha = appear * fade;
            subtitle.alpha = Mathf.Clamp01((t - 0.2f) / 0.2f) * fade;
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, Mathf.SmoothStep(0f, 1f, appear));
            yield return null;
        }
        title.text = subtitle.text = "";
    }

    // 구간 시작 안내: 위에서 툭 떨어지듯 나타났다 사라짐
    IEnumerator ShowCallout(string text)
    {
        callout.text = text;
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            float appear = Mathf.Clamp01(t / 0.15f);
            callout.alpha = appear * Mathf.Clamp01((1.6f - t) / 0.4f);
            callout.rectTransform.anchoredPosition = new Vector2(0f, -130f + (1f - appear) * 30f);
            yield return null;
        }
        callout.text = "";
    }

    IEnumerator SwapMusic(AudioClip clip)
    {
        AudioSource src = bird.bgmAudioSource;
        if (src == null || clip == null || src.clip == clip) yield break;
        float volume = src.volume;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { src.volume = volume * (1f - t / 0.6f); yield return null; }
        src.clip = clip;
        src.loop = true;
        src.Play();
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { src.volume = volume * (t / 0.6f); yield return null; }
        src.volume = volume;
    }
}
