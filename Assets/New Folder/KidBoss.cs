using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// 🏙️ 도시 스테이지 400m 보스: 새총 쏘는 아이 (4챕터 마지막)
// 1. 시작 컷씬 (위아래 검은 띠): 새가 날갯짓하며 멈춤 → 신호등이 쓰러지고 차들이 지나감 → 아이가 길 한가운데로 걸어 나옴 → "SLINGSHOT KID!"
// 2. 30초 버티기: 새는 제자리에서 좌우·위아래로만 피함. 아이가 고정 패턴으로 돌멩이를 쏨 (빨간 경고 1.8초 뒤에 날아옴)
//    0~7초 조준 사격 / 7~12초 세 발 부채꼴·가로 일렬 / 12~18초 돌멩이 비·세로 일렬 / 18~23초 "움직이지 마!"(새 둘레를 감싸는 링)·큰 돌 / 23~30초 마지막 연사
// 3. 30초가 지나면 피할 수 없는 기술 "LOCKED ON!": 조준선이 새를 따라다니고 아이가 새총을 끝까지 당김
//    → 슬로모션으로 날아온 돌멩이가 새를 쫓아가 맞힘 (컷씬)
// 4. 엔딩 컷씬 (THE END): 새가 빙글빙글 떨어져 도로에 쓰러짐 → 아이가 다가와 내려다봄 → 화면이 어두워지며 엔딩 문구 → RESTART / MENU
public class KidBoss : MonoBehaviour
{
    [Header("연결")]
    public BirdController bird;
    public CityTraffic traffic;
    public CeilingHazard ceilingHazard;

    [Header("UI")]
    public GameObject bossHud;
    public RectTransform timeFill;
    public TextMeshProUGUI bossName;

    [Header("머티리얼")]
    public Material skinMat, hairMat, shirtMat, stripeMat, shortsMat, shoeMat, capMat, eyeMat, woodMat, bandMat, pebbleMat, telegraphMat;
    public Material cheekMat;   // 볼터치·혀 (분홍)
    public Material tapeMat;    // 반창고·손잡이 테이프 (살구색)

    [Header("사운드")]
    public AudioClip bossBgm, shootSound, hitSound;

    [Header("설정")]
    public float surviveSeconds = 30f;
    public float telegraphTime = 1.8f;      // 빨간 경고가 뜨고 돌멩이가 새에게 닿기까지
    public float pebbleSpeed = 55f;
    public float kidScale = 6.5f;           // 아이 키 약 16m (새보다 크게, 얼굴은 새 머리 위)
    public float kidDistance = 40f;         // 새 앞 이만큼에 아이가 섬
    public float roadY = -6.5f;
    public float fightCeiling = 13f;        // 보스전 동안 천장 (위로 도망 못 가게)

    // ── 상태 ──
    private bool running, fighting, finale;
    private float elapsed, bossZ;
    private Coroutine patternLoop;
    private AudioSource sfx;
    private AudioClip beep;
    private Transform cam;
    private Camera camComp;

    // 아이
    private Transform kid, body, head, leftArm, rightArm, slingshot, pouch, bandL, bandR, prongL, prongR;
    private float pull;          // 새총 당긴 정도 (0 ~ 1.3)
    private float pullTarget;
    private float walkBob;
    private bool stepping;       // 옆으로 자리 옮기는 중
    private float laugh, cheer;  // 웃음(약 올리기) / 환호(새를 맞힘)
    private Transform mouth, pupilL, pupilR;

    // 돌멩이
    private class Shot
    {
        public Transform t;
        public Vector3 vel;
        public float radius;
        public GameObject mark;
        public bool falling;     // 위에서 떨어지는 돌 (비)
    }
    private readonly List<Shot> shots = new List<Shot>();

    // 경고 표시
    private readonly List<GameObject> telegraphs = new List<GameObject>();
    private readonly List<Renderer> teleFills = new List<Renderer>();
    private readonly List<Renderer> teleEdges = new List<Renderer>();

    // UI
    private Canvas canvas, endCanvas;
    private RectTransform barTop, barBottom, lockRing;
    private Text title, subtitle, callout, lockText;
    private Image flash, fade;
    private Text[] endLines;

    void Start()
    {
        if (bird == null) bird = FindAnyObjectByType<BirdController>();
        if (traffic == null) traffic = FindAnyObjectByType<CityTraffic>();
        if (ceilingHazard == null) ceilingHazard = FindAnyObjectByType<CeilingHazard>();
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        float v = PlayerPrefs.GetFloat("JumpVolume", 1f);
        sfx.volume = v * v; // 효과음 슬라이더와 같은 크기
        beep = Tone(1046f, 0.09f, 0.3f);
        camComp = Camera.main;
        cam = camComp.transform;
        BuildUI();
    }

    // BirdController가 스토리 모드 400m에서 호출 (또는 메뉴의 보스 바로 하기)
    public void Begin()
    {
        if (running) return;
        running = true;
        if (ceilingHazard != null) ceilingHazard.Stop();
        if (traffic != null) traffic.ClearForBoss();
        if (bossHud != null) bossHud.SetActive(false);
        StartCoroutine(Intro());
    }

    // ───────────────────────── 1. 시작 컷씬 ─────────────────────────

    IEnumerator Intro()
    {
        bird.cutscene = true;
        Rigidbody rb = bird.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        StartCoroutine(Letterbox(true, 0.5f));
        if (bossBgm != null) StartCoroutine(SwapMusic(bossBgm));

        // 새: 앞으로 가던 힘이 빠지며 제자리 비행 높이(5)로
        Vector3 start = bird.transform.position;
        float speed = bird.forwardSpeed;
        float hoverY = 5f;
        float travel = 0f;
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            float s = Mathf.Lerp(speed, 0f, t / 1.2f);
            travel += s * Time.deltaTime;
            bird.transform.position = new Vector3(Mathf.Lerp(start.x, 0f, t / 1.2f), Mathf.Lerp(start.y, hoverY, t / 1.2f), start.z + travel);
            bird.transform.rotation = Quaternion.Slerp(bird.transform.rotation, Quaternion.identity, Time.deltaTime * 5f);
            yield return null;
        }
        bossZ = bird.transform.position.z;

        // 아이: 오른쪽 인도에서 길 한가운데로 걸어 나옴 (카메라는 새 뒤 높이서 길 전체를 봄)
        BuildKid();
        Vector3 kidFrom = new Vector3(30f, roadY, bossZ + kidDistance + 6f), kidTo = new Vector3(0f, roadY, bossZ + kidDistance);
        cam.SetParent(null, true);
        Vector3 camFrom = cam.position;
        Vector3 camWide = bird.transform.position + new Vector3(0f, 5f, -14f);
        bool titled = false;
        kid.position = kidFrom;
        kid.rotation = Facing(Vector3.left);
        for (float t = 0f; t < 3.2f; t += Time.deltaTime)
        {
            float u = Mathf.Clamp01(t / 2.4f);
            kid.position = Vector3.Lerp(kidFrom, kidTo, Mathf.SmoothStep(0f, 1f, u));
            walkBob += Time.deltaTime * (u < 1f ? 9f : 0f);
            AnimateKid(u < 1f);
            if (u >= 1f) kid.rotation = Quaternion.Slerp(kid.rotation, Facing(Vector3.back), Time.deltaTime * 4f);
            bird.transform.position = new Vector3(0f, hoverY + Mathf.Sin(t * 3f) * 0.2f, bossZ);
            cam.position = Vector3.Lerp(camFrom, camWide, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.2f)));
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(kid.position + Vector3.up * 8f - cam.position), Time.deltaTime * 3f);
            if (!titled && t > 2.3f) { titled = true; StartCoroutine(ShowTitle("SLINGSHOT KID!", "30초 동안 돌멩이를 피하세요!", 1.6f)); }
            yield return null;
        }
        // 아이가 새총을 들어 겨눔
        pullTarget = 0.2f;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime) { AnimateKid(false); yield return null; }

        // 카메라는 다시 새 뒤로 (원래 자리)
        cam.SetParent(bird.transform, true);
        Vector3 lp = cam.localPosition; Quaternion lr = cam.localRotation;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            float e = Mathf.SmoothStep(0f, 1f, t / 0.6f);
            cam.localPosition = Vector3.Lerp(lp, new Vector3(0f, 2f, -7f), e);
            cam.localRotation = Quaternion.Slerp(lr, Quaternion.identity, e);
            yield return null;
        }
        cam.localPosition = new Vector3(0f, 2f, -7f);
        cam.localRotation = Quaternion.identity;

        // ── 보스전 시작: 앞으로는 안 가고 제자리에서 피함 ──
        StartCoroutine(Letterbox(false, 0.5f));
        bird.forwardSpeed = 0f;
        bird.ceilingY = fightCeiling;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        bird.cutscene = false;
        if (bossHud != null) bossHud.SetActive(true);
        elapsed = 0f;
        fighting = true;
        patternLoop = StartCoroutine(Patterns());
    }

    // ───────────────────────── 2. 고정 패턴 ─────────────────────────

    // T = 돌멩이가 새에게 닿는 시각(초). 위치는 경고가 뜰 때 새 위치 기준
    // 구간이 바뀔 때마다 아이가 옆으로 자리를 옮겨서 (move) 돌이 날아오는 각도가 달라지고, 외칠 때마다 약 올리며 웃음
    private struct Step { public float time; public string kind; public string text; public float x; }
    static Step S(float t, string k) { return new Step { time = t, kind = k }; }
    static Step Call(float t, string text) { return new Step { time = t, kind = "callout", text = text }; }
    static Step Move(float t, float x) { return new Step { time = t, kind = "move", x = x }; }

    List<Step> Schedule()
    {
        var s = new List<Step>
        {
            // 0~7초: 몸풀기 조준 사격 (마지막엔 빠른 저격)
            Call(0.3f, "AIMED SHOTS!"),
            S(1.8f, "aim"), S(3.0f, "aim"), S(4.2f, "sniper"), S(5.4f, "spread"), S(6.6f, "aim"),
            // 7~11초: 왼쪽으로 옮겨서 지그재그
            Move(6.9f, -12f), Call(7.0f, "ZIGZAG!"),
            S(8.6f, "zigzag"), S(9.8f, "spread"), S(11.0f, "zigzag"),
            // 11~16초: 오른쪽으로 옮겨서 돌멩이 비
            Move(11.3f, 12f), Call(11.4f, "PEBBLE RAIN!"),
            S(12.9f, "rain"), S(14.1f, "rain2"), S(15.2f, "column"), S(16.2f, "sniper"),
            // 16~21초: 가운데로 와서 "움직이지 마!" (가만히 있으면 사는 패턴)
            Move(16.6f, 0f), Call(16.8f, "DON'T MOVE!"),
            S(18.3f, "spiral"), S(19.7f, "ring"), S(21.0f, "cross"),
            // 21~25초: 틈 하나만 남긴 돌멩이 벽
            Call(21.6f, "FIND THE GAP!"),
            S(23.2f, "gap"), S(24.4f, "big"),
            // 25~30초: 왼쪽으로 옮겨서 마지막 연사
            Move(24.7f, -8f), Call(24.9f, "FINAL VOLLEY!"),
            S(26.2f, "row"), S(27.2f, "zigzag"), S(28.2f, "cross"), S(29.2f, "sniper"),
        };
        s.Sort((a, b) => (a.time - Lead(a)).CompareTo(b.time - Lead(b)));
        return s;
    }

    float Lead(Step st) { return st.kind == "callout" || st.kind == "move" ? 0f : telegraphTime; }

    IEnumerator Patterns()
    {
        foreach (Step st in Schedule())
        {
            while (elapsed < st.time - Lead(st)) { if (bird.IsGameOver) yield break; yield return null; }
            if (bird.IsGameOver) yield break;
            Vector3 me = BirdPos();
            float tele = telegraphTime;
            switch (st.kind)
            {
                case "callout": StartCoroutine(ShowCallout(st.text)); StartCoroutine(Taunt()); break;
                case "move": StartCoroutine(SideStep(st.x)); break;
                case "aim": StartCoroutine(Fire(me, 1.9f, tele)); break;
                case "sniper": StartCoroutine(Fire(me, 1.6f, tele, true, 1.8f)); break;   // 작고 빠른 한 발
                case "big": StartCoroutine(Fire(me, 3.6f, tele, true, 0.75f)); break;     // 크고 느린 바위
                case "spread":
                    for (int i = -1; i <= 1; i++) StartCoroutine(Fire(me + new Vector3(i * 6f, 0f, 0f), 1.9f, tele, i == 0));
                    break;
                case "row":
                    for (int i = -2; i <= 2; i++) StartCoroutine(Fire(me + new Vector3(i * 6.5f, 0f, 0f), 1.9f, tele + (i + 2) * 0.12f, i == -2));
                    break;
                case "column":
                    for (int i = -2; i <= 2; i++) StartCoroutine(Fire(me + new Vector3(0f, i * 3.4f, 0f), 1.9f, tele + (i + 2) * 0.12f, i == -2));
                    break;
                case "zigzag": // 왼쪽에서 오른쪽으로 위아래 번갈아 6발이 차례로
                    for (int i = 0; i < 6; i++)
                        StartCoroutine(Fire(me + new Vector3(-12.5f + i * 5f, i % 2 == 0 ? 3f : -3f, 0f), 1.9f, tele + i * 0.14f, i == 0));
                    break;
                case "ring": // 새 둘레 6방향 동시에: 가만히 있으면 안 맞음
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        StartCoroutine(Fire(me + new Vector3(Mathf.Cos(a) * 5.5f, Mathf.Sin(a) * 5.5f, 0f), 1.9f, tele, i == 0));
                    }
                    break;
                case "spiral": // 새 둘레를 한 바퀴 빙 돌며 8발 차례로: 가만히 있으면 안 맞음
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        StartCoroutine(Fire(me + new Vector3(Mathf.Cos(a) * 6f, Mathf.Sin(a) * 6f, 0f), 1.9f, tele + i * 0.1f, i == 0));
                    }
                    break;
                case "cross": // 새 자리를 지나는 X자 (가운데 포함) → 대각선이 아닌 쪽으로 비켜야 함
                    StartCoroutine(Fire(me, 1.9f, tele));
                    for (int k = 1; k <= 2; k++)
                        for (int d = 0; d < 4; d++)
                        {
                            float sx = d % 2 == 0 ? 1f : -1f, sy = d < 2 ? 1f : -1f;
                            StartCoroutine(Fire(me + new Vector3(sx * k * 3.6f, sy * k * 3.6f, 0f), 1.9f, tele + k * 0.1f, false));
                        }
                    break;
                case "gap": // 화면을 덮는 큰 돌멩이 벽, 새 근처의 한 칸만 비어 있음
                {
                    float[] cols = { -28f, -21f, -14f, -7f, 0f, 7f, 14f, 21f, 28f };
                    float[] rows = { -3.5f, 1.5f, 6.5f, 11f };
                    int gc = 0, gr = 0;
                    for (int c = 0; c < cols.Length; c++) if (Mathf.Abs(cols[c] - me.x) < Mathf.Abs(cols[gc] - me.x)) gc = c;
                    for (int r = 0; r < rows.Length; r++) if (Mathf.Abs(rows[r] - me.y) < Mathf.Abs(rows[gr] - me.y)) gr = r;
                    gc = Mathf.Clamp(gc + (Random.value < 0.5f ? -1 : 1), 0, cols.Length - 1); // 바로 옆 칸 (조금 움직여야 함)
                    for (int c = 0; c < cols.Length; c++)
                        for (int r = 0; r < rows.Length; r++)
                            if (c != gc || r != gr)
                                StartCoroutine(Fire(new Vector3(cols[c], rows[r], bossZ), 3.2f, tele, c == 0 && r == 0, 0.8f, 3.1f));
                    break;
                }
                case "rain":
                    foreach (float dx in new[] { -7f, 0f, 7f }) StartCoroutine(Rain(me.x + dx, tele, dx == 0f));
                    break;
                case "rain2":
                    foreach (float dx in new[] { -10.5f, -3.5f, 3.5f, 10.5f }) StartCoroutine(Rain(me.x + dx, tele, dx < -10f));
                    break;
            }
        }
    }

    Vector3 BirdPos() { return ClampTarget(bird.transform.position); }

    Vector3 ClampTarget(Vector3 p)
    {
        return new Vector3(Mathf.Clamp(p.x, -33f, 33f), Mathf.Clamp(p.y, bird.fallLimitY + 0.5f, fightCeiling - 0.5f), bossZ);
    }

    // 새총 한 발: 원 경고 → (날아가는 시간 + 당기는 시간 전에) 아이가 당김 → 놓으면 돌멩이가 경고 자리를 지나감
    // speedMul: 돌 속도 배율, hitOverride: 맞는 범위 (0이면 경고 원의 2/3)
    IEnumerator Fire(Vector3 target, float radius, float tele, bool sound = true, float speedMul = 1f, float hitOverride = 0f)
    {
        target = ClampTarget(target);
        GameObject mark = MakeCircleTelegraph(target, radius);
        float speed = pebbleSpeed * speedMul;
        Vector3 from = PouchPos();
        float flight = Vector3.Distance(from, target) / speed;
        yield return Wait(tele - flight - 0.5f);
        pullTarget = 1f;
        AimAt(target);
        yield return Wait(0.5f);
        if (!fighting) { RemoveTelegraph(mark); yield break; }
        from = PouchPos();
        Release();
        if (sound) Play(shootSound, Random.Range(1.1f, 1.3f) * (speedMul > 1.2f ? 1.25f : speedMul < 0.9f ? 0.8f : 1f));
        Transform t = MakePebble(from, radius > 3f ? 1.8f : radius < 1.7f ? 0.55f : 0.7f);
        float hit = hitOverride > 0f ? hitOverride : radius * 0.66f;
        shots.Add(new Shot { t = t, vel = (target - from).normalized * speed, radius = hit, mark = mark });
    }

    // 구간이 바뀔 때 아이가 옆으로 몇 걸음 옮김
    IEnumerator SideStep(float x)
    {
        if (kid == null) yield break;
        Vector3 from = kid.position, to = new Vector3(x, roadY, from.z);
        float dur = Mathf.Max(0.6f, Mathf.Abs(x - from.x) / 14f);
        stepping = true;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            kid.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur));
            walkBob += Time.deltaTime * 10f;
            yield return null;
        }
        kid.position = to;
        stepping = false;
    }

    // 약 올리기: 고개를 젖히고 깔깔 웃으며 몸을 들썩임 (입이 크게 벌어짐)
    IEnumerator Taunt()
    {
        laugh = 1f;
        for (float t = 0f; t < 1.1f; t += Time.deltaTime) { laugh = 1f - t / 1.1f; yield return null; }
        laugh = 0f;
    }

    // 돌멩이 비: 아이가 하늘로 쏨 → 세로 띠 경고 자리에 위에서 떨어짐
    IEnumerator Rain(float x, float tele, bool shoot)
    {
        x = Mathf.Clamp(x, -33f, 33f);
        GameObject mark = MakeRectTelegraph(new Vector3(x, 4f, bossZ), new Vector2(2.6f, 22f));
        const float startY = 34f, fallSpeed = 50f;
        float fall = (startY - bird.transform.position.y) / fallSpeed;
        if (shoot)
        {
            yield return Wait(Mathf.Max(0f, tele - fall - 1.1f));
            pullTarget = 1f;
            AimAt(PouchPos() + new Vector3(0f, 30f, -5f));
            yield return Wait(0.5f);
            Release();
            Play(shootSound, 0.9f);
            Transform up = MakePebble(PouchPos(), 0.7f);
            shots.Add(new Shot { t = up, vel = new Vector3(0f, 60f, -8f), radius = 0f });
            yield return Wait(Mathf.Max(0f, tele - fall - Mathf.Max(0f, tele - fall - 1.1f) - 0.5f));
        }
        else yield return Wait(tele - fall);
        if (!fighting) { RemoveTelegraph(mark); yield break; }
        Transform t = MakePebble(new Vector3(x, startY, bossZ), 0.8f);
        shots.Add(new Shot { t = t, vel = new Vector3(0f, -fallSpeed, 0f), radius = 1.3f, mark = mark, falling = true });
    }

    IEnumerator Wait(float seconds)
    {
        for (float w = 0f; w < seconds && fighting; w += Time.deltaTime) yield return null;
    }

    // ───────────────────────── 매 프레임 ─────────────────────────

    void Update()
    {
        if (!running) return;
        if (kid != null && !finale) AnimateKid(stepping);
        if (!fighting || bird.IsGameOver) return;

        elapsed += Time.deltaTime;
        float left = Mathf.Max(0f, surviveSeconds - elapsed);
        if (timeFill != null) timeFill.anchorMax = new Vector2(left / surviveSeconds, 1f);
        if (bossName != null) bossName.text = "SLINGSHOT KID   " + Mathf.CeilToInt(left) + "s";
        if (left <= 0f) StartCoroutine(LockOn());
    }

    void LateUpdate()
    {
        if (!running) return;
        Vector3 me = bird.transform.position;
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            Shot s = shots[i];
            if (s.t == null) { shots.RemoveAt(i); continue; }
            Vector3 prev = s.t.position;
            Vector3 cur = prev + s.vel * Time.deltaTime;
            s.t.position = cur;
            s.t.Rotate(new Vector3(400f, 250f, 0f) * Time.deltaTime, Space.Self);
            if (fighting && !bird.IsGameOver && s.radius > 0f && DistanceToSegment(me, prev, cur) < s.radius)
            {
                Play(hitSound, 1.2f);
                Feathers(me, 12);
                bird.GameOver();
                fighting = false;
                cheer = 1f;
                StartCoroutine(ShowCallout("HAHA! GOT YOU!"));
                if (bossHud != null) bossHud.SetActive(false);
            }
            // 경고는 돌멩이가 새 자리를 지나면 지움
            if (s.mark != null && (s.falling ? cur.y < me.y - 2f : cur.z < bossZ - 1f)) { RemoveTelegraph(s.mark); s.mark = null; }
            if (cur.z < bossZ - 25f || cur.y < roadY - 2f || cur.y > 60f)
            {
                if (s.mark != null) RemoveTelegraph(s.mark);
                Destroy(s.t.gameObject);
                shots.RemoveAt(i);
            }
        }
        if (telegraphs.Count > 0) PulseTelegraphs();
    }

    // ───────────────────────── 3. 피할 수 없는 기술 + 엔딩 ─────────────────────────

    IEnumerator LockOn()
    {
        fighting = false;
        finale = true;
        if (patternLoop != null) StopCoroutine(patternLoop);
        ClearTelegraphs();
        if (bossHud != null) bossHud.SetActive(false);
        StartCoroutine(ShowCallout("LOCKED ON!"));

        // 1) 조준: 아이가 새를 똑바로 겨누고 천천히 끝까지 당김, 조준선이 새를 따라다니며 좁혀짐 (새는 계속 움직일 수 있지만 소용없음)
        lockRing.gameObject.SetActive(true);
        lockText.gameObject.SetActive(true);
        float aimTime = 2.4f;
        int lastBeep = -1;
        for (float t = 0f; t < aimTime; t += Time.deltaTime)
        {
            pullTarget = Mathf.Lerp(0.3f, 1.3f, t / aimTime);
            AimAt(bird.transform.position);
            AnimateKid(false);
            Vector3 sp = camComp.WorldToScreenPoint(bird.transform.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, sp, null, out Vector2 local);
            lockRing.anchoredPosition = local;
            float k = 1f - t / aimTime;
            lockRing.sizeDelta = Vector2.one * Mathf.Lerp(90f, 260f, k * k);
            lockRing.localRotation = Quaternion.Euler(0f, 0f, t * 120f);
            lockText.rectTransform.anchoredPosition = local + new Vector2(0f, 90f);
            int b = Mathf.FloorToInt(t * (2f + t * 2f));
            if (b != lastBeep) { lastBeep = b; Play(beep, 1f + t * 0.2f); }
            yield return null;
        }

        // 2) 발사 → 슬로모션: 카메라가 옆으로 빠져 날아가는 돌멩이를 따라감, 돌멩이는 새를 쫓아감
        bird.cutscene = true;
        Rigidbody rb = bird.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        lockRing.gameObject.SetActive(false);
        lockText.gameObject.SetActive(false);
        StartCoroutine(Letterbox(true, 0.3f));
        Vector3 from = PouchPos();
        Release();
        Play(shootSound, 0.8f);
        Transform rock = MakePebble(from, 0.9f);
        cam.SetParent(null, true);
        Vector3 birdPos = bird.transform.position;
        Vector3 mid = (from + birdPos) * 0.5f;
        Vector3 side = new Vector3(birdPos.x >= 0f ? -1f : 1f, 0f, 0f);
        cam.position = mid + side * 16f + Vector3.up * 2f;
        cam.rotation = Quaternion.LookRotation(mid - cam.position);
        Time.timeScale = 0.25f;
        while (Vector3.Distance(rock.position, bird.transform.position) > 0.8f)
        {
            float dt = Time.deltaTime;
            Vector3 to = bird.transform.position - rock.position;
            rock.position += to.normalized * Mathf.Min(to.magnitude, 60f * dt);
            rock.Rotate(new Vector3(500f, 300f, 0f) * dt, Space.Self);
            // 새는 놀라서 파닥이며 제자리
            bird.transform.position = birdPos + Vector3.up * Mathf.Sin(Time.time * 30f) * 0.08f;
            Vector3 focus = Vector3.Lerp(rock.position, bird.transform.position, 0.35f);
            cam.position = Vector3.Lerp(cam.position, focus + side * 6f + Vector3.up * 1.2f + Vector3.back * 1.5f, dt * 3f);
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(focus - cam.position), dt * 6f);
            yield return null;
        }

        // 3) 명중: 하얀 번쩍임 + 깃털 + 쿵, 돌멩이는 튕겨 나감
        Play(hitSound, 0.9f);
        StartCoroutine(Flash());
        Feathers(bird.transform.position, 26);
        Vector3 rockVel = new Vector3(side.x * -6f, 8f, 10f);
        Time.timeScale = 0.35f;
        StartCoroutine(FadeMusic(2.5f));

        // 4) 새가 빙글빙글 떨어져 도로에 쓰러짐 (천천히 → 점점 보통 속도로)
        Vector3 pos = bird.transform.position;
        Vector3 vel = new Vector3(side.x * 2f, 3f, -3f);
        Vector3 spin = new Vector3(380f, 120f, 460f);
        float groundY = roadY + 0.6f;
        while (pos.y > groundY)
        {
            float dt = Time.deltaTime;
            Time.timeScale = Mathf.MoveTowards(Time.timeScale, 1f, Time.unscaledDeltaTime * 0.6f);
            vel += Vector3.down * 22f * dt;
            pos += vel * dt;
            bird.transform.position = pos;
            bird.transform.Rotate(spin * dt, Space.World);
            if (rock != null) { rockVel += Physics.gravity * dt; rock.position += rockVel * dt; }
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(pos - cam.position), dt * 5f);
            yield return null;
        }
        Time.timeScale = 1f;
        pos.y = groundY;
        bird.transform.position = pos;
        bird.transform.rotation = Quaternion.Euler(0f, 30f, 95f); // 옆으로 쓰러짐
        SeagullFlap flap = bird.GetComponentInChildren<SeagullFlap>();
        if (flap != null) flap.enabled = false;
        Play(hitSound, 0.6f);
        Feathers(pos, 10);
        if (rock != null) Destroy(rock.gameObject, 1f);

        // 5) 쓰러진 새를 낮은 곳에서 비추고, 아이가 걸어와 내려다봄
        Vector3 camLow = pos + new Vector3(side.x * 7f, 1.5f, -9f);
        Vector3 kidStop = new Vector3(pos.x * 0.5f, roadY, pos.z + 12f);
        Vector3 kidStart = kid.position;
        for (float t = 0f; t < 3.4f; t += Time.deltaTime)
        {
            float u = Mathf.Clamp01(t / 2.6f);
            kid.position = Vector3.Lerp(kidStart, kidStop, Mathf.SmoothStep(0f, 1f, u));
            Vector3 look = pos - kid.position; look.y = 0f;
            kid.rotation = Quaternion.Slerp(kid.rotation, Facing(look), Time.deltaTime * 3f);
            walkBob += Time.deltaTime * (u < 1f ? 7f : 0f);
            pullTarget = 0f;
            AnimateKid(u < 1f);
            // 다 오면 고개를 숙여 새를 내려다봄
            if (head != null) head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(u >= 1f ? -28f : 0f, 0f, 0f), Time.deltaTime * 2f);
            cam.position = Vector3.Lerp(cam.position, camLow, Time.deltaTime * 1.5f);
            Vector3 focus = Vector3.Lerp(pos, kid.position + Vector3.up * kidScale * 1.6f, 0.45f);
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(focus - cam.position), Time.deltaTime * 2.5f);
            yield return null;
        }

        // 6) 화면이 어두워지며 엔딩 문구
        if (bird.scoreBadge != null) bird.scoreBadge.SetActive(false);
        endCanvas.enabled = true;
        for (float t = 0f; t < 1.6f; t += Time.deltaTime) { fade.color = new Color(0f, 0f, 0f, t / 1.6f); yield return null; }
        fade.color = Color.black;
        yield return Letterbox(false, 0.3f); // 검은 띠가 위쪽 문구를 가리지 않게 먼저 걷음
        // 한 줄씩 천천히 (깨달음 문장들은 더 오래 머묾)
        float[] holds = { 1.4f, 0.9f, 2.0f, 2.4f, 1.2f };
        float[] fades = { 1f, 1f, 1.4f, 1.6f, 1.2f };
        for (int i = 0; i < endLines.Length; i++)
        {
            Text line = endLines[i];
            for (float t = 0f; t < fades[i]; t += Time.deltaTime) { SetAlpha(line, t / fades[i]); yield return null; }
            SetAlpha(line, 1f);
            yield return new WaitForSeconds(holds[i]);
        }
        bird.GameOver(); // RESTART / MENU 버튼
    }

    IEnumerator Flash()
    {
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime) { flash.color = new Color(1f, 1f, 1f, 1f - t / 0.5f); yield return null; }
        flash.color = new Color(1f, 1f, 1f, 0f);
    }

    IEnumerator FadeMusic(float seconds)
    {
        AudioSource src = bird.bgmAudioSource;
        if (src == null) yield break;
        float v0 = src.volume;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) { src.volume = v0 * (1f - t / seconds); yield return null; }
        src.volume = 0f;
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

    // ───────────────────────── 아이 모델 ─────────────────────────

    // 크기 1 기준(키 약 2.4)으로 만들고 kidScale배. 앞이 -z(새 쪽)
    // 잼민이: 거꾸로 쓴 야구모자 + 삐죽 머리 + 눈동자가 새를 따라봄 + 짓궂게 치켜뜬 눈썹 + 주근깨 + 볼 반창고
    //        + 씩 웃는 입(웃을 땐 크게 벌어지며 이·혀가 보임) + 줄무늬 티셔츠(가슴에 번개) + 책가방 + 허리춤 돌멩이 주머니
    //        + 반바지 + 무릎 반창고 + 줄무늬 양말 + 두툼한 운동화(흰 밑창·끈)
    // 왼팔은 새총을 앞으로 내밀고, 오른팔은 고무줄을 뒤로 당김 (팔은 어깨를 축으로 돌아감)
    void BuildKid()
    {
        kid = new GameObject("SlingshotKid").transform;
        kid.localScale = Vector3.one * kidScale;
        body = new GameObject("Body").transform;
        body.SetParent(kid, false);

        for (int s = -1; s <= 1; s += 2)
        {
            // 운동화: 몸통 + 흰 밑창 + 앞코 + 끈
            Part(body, PrimitiveType.Cube, new Vector3(s * 0.17f, 0.1f, -0.06f), new Vector3(0.27f, 0.17f, 0.5f), shoeMat);
            Part(body, PrimitiveType.Cube, new Vector3(s * 0.17f, 0.025f, -0.06f), new Vector3(0.29f, 0.06f, 0.53f), stripeMat);
            Part(body, PrimitiveType.Sphere, new Vector3(s * 0.17f, 0.09f, -0.3f), new Vector3(0.27f, 0.16f, 0.16f), stripeMat);
            for (int k = 0; k < 2; k++) Part(body, PrimitiveType.Cube, new Vector3(s * 0.17f, 0.19f, -0.16f + k * 0.08f), new Vector3(0.2f, 0.02f, 0.025f), stripeMat);
            // 다리 + 줄무늬 양말
            Part(body, PrimitiveType.Cylinder, new Vector3(s * 0.17f, 0.47f, 0f), new Vector3(0.18f, 0.29f, 0.18f), skinMat);
            Part(body, PrimitiveType.Cylinder, new Vector3(s * 0.17f, 0.23f, 0f), new Vector3(0.2f, 0.06f, 0.2f), stripeMat);
            Part(body, PrimitiveType.Cylinder, new Vector3(s * 0.17f, 0.3f, 0f), new Vector3(0.2f, 0.02f, 0.2f), shoeMat);
        }
        // 무릎 반창고 (오른쪽)
        Part(body, PrimitiveType.Cube, new Vector3(0.17f, 0.55f, -0.09f), new Vector3(0.11f, 0.05f, 0.02f), tapeMat);
        // 반바지 + 주머니
        Part(body, PrimitiveType.Cube, new Vector3(0f, 0.86f, 0f), new Vector3(0.62f, 0.36f, 0.38f), shortsMat);
        Part(body, PrimitiveType.Cube, new Vector3(-0.2f, 0.84f, -0.195f), new Vector3(0.14f, 0.14f, 0.01f), shoeMat);
        // 줄무늬 티셔츠 + 가슴 번개 무늬
        Part(body, PrimitiveType.Cube, new Vector3(0f, 1.3f, 0f), new Vector3(0.64f, 0.6f, 0.4f), shirtMat);
        for (int k = 0; k < 3; k++) Part(body, PrimitiveType.Cube, new Vector3(0f, 1.1f + k * 0.17f, 0f), new Vector3(0.66f, 0.06f, 0.42f), stripeMat);
        GameObject bolt = Part(body, PrimitiveType.Cube, new Vector3(0.03f, 1.33f, -0.212f), new Vector3(0.07f, 0.24f, 0.01f), capMat);
        bolt.transform.localRotation = Quaternion.Euler(0f, 0f, -25f);
        // 허리춤 돌멩이 주머니 (오른쪽 엉덩이)
        Part(body, PrimitiveType.Sphere, new Vector3(0.36f, 0.95f, 0f), new Vector3(0.16f, 0.2f, 0.16f), bandMat);
        Part(body, PrimitiveType.Sphere, new Vector3(0.36f, 1.04f, -0.02f), new Vector3(0.09f, 0.07f, 0.09f), pebbleMat);
        // 책가방 (등 뒤) + 멜빵
        Part(body, PrimitiveType.Cube, new Vector3(0f, 1.32f, 0.3f), new Vector3(0.5f, 0.56f, 0.22f), shoeMat);
        Part(body, PrimitiveType.Cube, new Vector3(0f, 1.22f, 0.42f), new Vector3(0.36f, 0.22f, 0.06f), capMat);
        for (int s = -1; s <= 1; s += 2) Part(body, PrimitiveType.Cube, new Vector3(s * 0.17f, 1.33f, -0.205f), new Vector3(0.06f, 0.56f, 0.012f), shoeMat);

        head = new GameObject("Head").transform;
        head.SetParent(body, false);
        head.localPosition = new Vector3(0f, 1.62f, 0f);
        Part(head, PrimitiveType.Sphere, new Vector3(0f, 0.32f, 0f), new Vector3(0.68f, 0.64f, 0.64f), skinMat);
        // 삐죽 머리 (모자 아래로 삐져나옴) + 거꾸로 쓴 모자 (챙이 뒤로)
        Part(head, PrimitiveType.Sphere, new Vector3(0f, 0.43f, 0.06f), new Vector3(0.72f, 0.48f, 0.66f), hairMat);
        for (int k = 0; k < 5; k++)
        {
            float a = -60f + k * 30f;
            GameObject spike = Part(head, PrimitiveType.Cube, new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.3f, 0.42f, -0.24f + Mathf.Abs(a) * 0.0012f), new Vector3(0.09f, 0.16f, 0.06f), hairMat);
            spike.transform.localRotation = Quaternion.Euler(-30f, 0f, -a * 0.6f);
        }
        Part(head, PrimitiveType.Sphere, new Vector3(0f, 0.52f, 0.03f), new Vector3(0.7f, 0.36f, 0.66f), capMat);
        Part(head, PrimitiveType.Cube, new Vector3(0f, 0.44f, 0.4f), new Vector3(0.5f, 0.04f, 0.3f), capMat);   // 뒤로 간 챙
        Part(head, PrimitiveType.Cube, new Vector3(0f, 0.6f, -0.24f), new Vector3(0.16f, 0.12f, 0.02f), stripeMat); // 모자 앞 로고
        for (int s = -1; s <= 1; s += 2)
        {
            // 눈: 흰자 + 새를 따라보는 눈동자
            Part(head, PrimitiveType.Sphere, new Vector3(s * 0.12f, 0.34f, -0.29f), new Vector3(0.13f, 0.14f, 0.06f), stripeMat);
            Transform pupil = Part(head, PrimitiveType.Sphere, new Vector3(s * 0.12f, 0.33f, -0.315f), new Vector3(0.07f, 0.08f, 0.03f), eyeMat).transform;
            if (s < 0) pupilL = pupil; else pupilR = pupil;
            // 짓궂게 치켜뜬 눈썹 (안쪽이 내려감)
            GameObject brow = Part(head, PrimitiveType.Cube, new Vector3(s * 0.13f, 0.44f, -0.3f), new Vector3(0.13f, 0.03f, 0.02f), hairMat);
            brow.transform.localRotation = Quaternion.Euler(0f, 0f, s * 18f);
            Part(head, PrimitiveType.Sphere, new Vector3(s * 0.34f, 0.3f, 0f), new Vector3(0.1f, 0.14f, 0.08f), skinMat);    // 귀
            Part(head, PrimitiveType.Sphere, new Vector3(s * 0.2f, 0.22f, -0.26f), new Vector3(0.1f, 0.06f, 0.03f), cheekMat); // 볼터치
            for (int k = 0; k < 3; k++) // 주근깨
                Part(head, PrimitiveType.Sphere, new Vector3(s * (0.14f + k * 0.04f), 0.26f + (k % 2) * 0.025f, -0.29f), new Vector3(0.018f, 0.018f, 0.01f), hairMat);
        }
        // 볼 반창고 (왼쪽 볼에 X자)
        for (int k = -1; k <= 1; k += 2)
        {
            GameObject tape = Part(head, PrimitiveType.Cube, new Vector3(-0.21f, 0.19f, -0.255f), new Vector3(0.11f, 0.035f, 0.01f), tapeMat);
            tape.transform.localRotation = Quaternion.Euler(0f, 0f, k * 35f);
        }
        Part(head, PrimitiveType.Sphere, new Vector3(0f, 0.27f, -0.33f), new Vector3(0.07f, 0.06f, 0.06f), skinMat); // 코
        // 입: 짙은 입 안 + 윗니 + 혀 (웃을 때 세로로 크게 벌어짐)
        mouth = new GameObject("Mouth").transform;
        mouth.SetParent(head, false);
        mouth.localPosition = new Vector3(0.03f, 0.17f, -0.3f);
        mouth.localRotation = Quaternion.Euler(0f, 0f, -6f); // 한쪽 입꼬리가 올라간 비웃음
        Part(mouth, PrimitiveType.Cube, Vector3.zero, new Vector3(0.17f, 1f, 0.02f), eyeMat);
        Part(mouth, PrimitiveType.Cube, new Vector3(0f, 0.38f, -0.005f), new Vector3(0.15f, 0.25f, 0.02f), stripeMat);
        Part(mouth, PrimitiveType.Cube, new Vector3(0f, -0.3f, -0.006f), new Vector3(0.1f, 0.35f, 0.02f), cheekMat);
        mouth.localScale = new Vector3(1f, 0.035f, 1f);

        leftArm = Arm(-1);
        rightArm = Arm(1);

        // 새총: 왼손에 쥔 Y자 나무 (테이프 감은 손잡이 + 두 갈래) + 고무줄 두 가닥 + 가죽 주머니 (오른손)
        slingshot = new GameObject("Slingshot").transform;
        slingshot.SetParent(leftArm, false);
        slingshot.localPosition = new Vector3(0f, 0f, -0.62f);
        Part(slingshot, PrimitiveType.Cylinder, new Vector3(0f, -0.08f, 0f), new Vector3(0.06f, 0.14f, 0.06f), woodMat);
        Part(slingshot, PrimitiveType.Cylinder, new Vector3(0f, -0.1f, 0f), new Vector3(0.065f, 0.06f, 0.065f), tapeMat);
        prongL = Part(slingshot, PrimitiveType.Cylinder, new Vector3(-0.07f, 0.13f, 0f), new Vector3(0.05f, 0.11f, 0.05f), woodMat).transform;
        prongL.localRotation = Quaternion.Euler(0f, 0f, 30f);
        prongR = Part(slingshot, PrimitiveType.Cylinder, new Vector3(0.07f, 0.13f, 0f), new Vector3(0.05f, 0.11f, 0.05f), woodMat).transform;
        prongR.localRotation = Quaternion.Euler(0f, 0f, -30f);
        pouch = Part(kid, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.12f, 0.08f, 0.1f), bandMat).transform;
        bandL = Part(kid, PrimitiveType.Cube, Vector3.zero, Vector3.one * 0.03f, bandMat).transform;
        bandR = Part(kid, PrimitiveType.Cube, Vector3.zero, Vector3.one * 0.03f, bandMat).transform;
    }

    // 어깨를 축으로 도는 팔: 줄무늬 소매 + 팔뚝 + 손목 밴드 + 주먹
    Transform Arm(int side)
    {
        Transform shoulder = new GameObject(side < 0 ? "LeftArm" : "RightArm").transform;
        shoulder.SetParent(body, false);
        shoulder.localPosition = new Vector3(side * 0.38f, 1.5f, 0f);
        Part(shoulder, PrimitiveType.Cube, new Vector3(0f, 0f, -0.08f), new Vector3(0.19f, 0.19f, 0.22f), shirtMat);
        Part(shoulder, PrimitiveType.Cube, new Vector3(0f, 0f, -0.15f), new Vector3(0.2f, 0.2f, 0.04f), stripeMat);
        GameObject arm = Part(shoulder, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.32f), new Vector3(0.12f, 0.25f, 0.12f), skinMat);
        arm.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject band = Part(shoulder, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.52f), new Vector3(0.14f, 0.03f, 0.14f), capMat);
        band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Part(shoulder, PrimitiveType.Sphere, new Vector3(0f, 0f, -0.6f), new Vector3(0.16f, 0.16f, 0.16f), skinMat);
        return shoulder;
    }

    // 걷기(팔다리 흔들림 + 들썩임) / 겨누기 + 새총 당기기 / 웃기(고개 젖히고 들썩, 입 크게) / 환호(맞히면 두 팔 번쩍)
    void AnimateKid(bool walk)
    {
        if (kid == null) return;
        pull = Mathf.MoveTowards(pull, pullTarget, Time.deltaTime * (pullTarget > pull ? 2.2f : 12f));
        float bob = walk ? Mathf.Abs(Mathf.Sin(walkBob)) * 0.05f : Mathf.Sin(Time.time * 2f) * 0.01f;
        bob += laugh * Mathf.Abs(Mathf.Sin(Time.time * 18f)) * 0.04f + cheer * Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.12f;
        body.localPosition = new Vector3(0f, bob, 0f);
        if (cheer > 0f)
        {
            // 두 팔 번쩍 + 신나서 흔들기
            float wave = Mathf.Sin(Time.time * 12f) * 15f;
            leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, Quaternion.Euler(-150f, 0f, -15f + wave), Time.deltaTime * 8f);
            rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, Quaternion.Euler(-150f, 0f, 15f - wave), Time.deltaTime * 8f);
        }
        else if (walk)
        {
            float swing = Mathf.Sin(walkBob) * 35f;
            leftArm.localRotation = Quaternion.Euler(-70f + swing, 0f, 0f);
            rightArm.localRotation = Quaternion.Euler(-70f - swing, 0f, 0f);
        }
        else
        {
            // 왼팔은 앞으로 쭉, 오른팔은 당긴 만큼 가운데(가슴) 쪽으로 접힘 (오른손 = 주머니)
            leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, Quaternion.Euler(-4f, -10f, 0f), Time.deltaTime * 8f);
            rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, Quaternion.Euler(-4f, 40f + Mathf.Clamp01(pull) * 32f, 0f), Time.deltaTime * 12f);
        }
        // 웃을 땐 고개를 젖히고 입을 크게
        if (head != null && !finale)
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(laugh * 18f + cheer * 12f, 0f, Mathf.Sin(Time.time * 16f) * 6f * laugh), Time.deltaTime * 10f);
        if (mouth != null)
        {
            float open = Mathf.Max(laugh, cheer) * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 18f)));
            mouth.localScale = new Vector3(1f, Mathf.Lerp(0.035f, 0.14f, open), 1f);
        }
        // 눈동자는 새를 따라봄
        if (pupilL != null && bird != null)
        {
            Vector3 local = head.InverseTransformPoint(bird.transform.position);
            Vector2 look = new Vector2(Mathf.Clamp(local.x * 0.02f, -0.025f, 0.025f), Mathf.Clamp(local.y * 0.02f, -0.02f, 0.02f));
            pupilL.localPosition = new Vector3(-0.12f + look.x, 0.33f + look.y, -0.315f);
            pupilR.localPosition = new Vector3(0.12f + look.x, 0.33f + look.y, -0.315f);
        }
        // 고무줄: 두 갈래 끝 → 주머니. 주머니는 당기면 오른손 쪽으로, 놓으면 갈래 가운데로 튕김
        Vector3 tipL = prongL.TransformPoint(new Vector3(0f, 1f, 0f)), tipR = prongR.TransformPoint(new Vector3(0f, 1f, 0f));
        Vector3 rest = (tipL + tipR) * 0.5f;
        Vector3 hand = rightArm.TransformPoint(new Vector3(0f, 0f, -0.6f));
        Vector3 p = Vector3.Lerp(rest, hand, cheer > 0f ? 0f : Mathf.Clamp01(pull));
        pouch.position = p;
        Band(bandL, tipL, p);
        Band(bandR, tipR, p);
    }

    void Band(Transform band, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        band.position = (a + b) * 0.5f;
        if (d.sqrMagnitude > 0.0001f) band.rotation = Quaternion.LookRotation(d);
        band.localScale = new Vector3(0.025f, 0.025f, d.magnitude / kidScale);
    }

    // 아이 몸을 목표 쪽으로 살짝 틀어 겨눔 (뒤로 돌지는 않게)
    void AimAt(Vector3 target)
    {
        if (kid == null) return;
        Vector3 d = target - kid.position; d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        kid.rotation = Quaternion.Slerp(kid.rotation, Facing(d), 0.6f);
    }

    // 아이 모델은 얼굴이 -z → dir 쪽을 보게 하는 회전
    static Quaternion Facing(Vector3 dir) { return Quaternion.LookRotation(-dir); }

    void Release()
    {
        pull = 0f;
        pullTarget = 0.15f;
    }

    Vector3 PouchPos() { return pouch != null ? pouch.position : new Vector3(0f, roadY + 7f, bossZ + kidDistance); }

    Transform MakePebble(Vector3 at, float size)
    {
        Transform t = Part(null, PrimitiveType.Sphere, Vector3.zero, new Vector3(size, size * 0.85f, size * 0.95f), pebbleMat).transform;
        t.name = "Pebble";
        t.position = at;
        t.rotation = Random.rotation;
        return t;
    }

    GameObject Part(Transform parent, PrimitiveType shape, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(shape);
        Destroy(g.GetComponent<Collider>());
        if (parent != null) g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    // 깃털: 흰 납작한 조각이 흩날리며 천천히 떨어짐 (보기만)
    void Feathers(Vector3 at, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject f = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(f.GetComponent<Collider>());
            f.name = "Feather";
            f.GetComponent<Renderer>().sharedMaterial = stripeMat;
            f.transform.position = at + Random.insideUnitSphere * 0.5f;
            f.transform.rotation = Random.rotation;
            f.transform.localScale = new Vector3(0.18f, 0.5f, 1f);
            StartCoroutine(Flutter(f.transform, Random.insideUnitSphere * 5f + Vector3.up * 3f));
        }
    }

    IEnumerator Flutter(Transform f, Vector3 v)
    {
        Vector3 spin = Random.insideUnitSphere * 360f;
        for (float t = 0f; t < 3f && f != null; t += Time.deltaTime)
        {
            v = Vector3.Lerp(v, new Vector3(Mathf.Sin(t * 4f) * 1.5f, -1.2f, 0f), Time.deltaTime * 2f);
            f.position += v * Time.deltaTime;
            f.Rotate(spin * Time.deltaTime, Space.World);
            yield return null;
        }
        if (f != null) Destroy(f.gameObject);
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

    static AudioClip Tone(float freq, float length, float amp)
    {
        int rate = 44100, n = Mathf.RoundToInt(rate * length);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Min(1f, t * 200f) * Mathf.Clamp01((length - t) * 30f);
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * amp * env;
        }
        AudioClip c = AudioClip.Create("LockBeep", n, 1, rate, false);
        c.SetData(data, 0);
        return c;
    }

    // ───────────────────────── 경고 표시 (새 바로 뒤 평면에 빨간 원·띠, 깜빡임) ─────────────────────────

    GameObject MakeCircleTelegraph(Vector3 center, float radius)
    {
        Transform root = new GameObject("SlingWarning").transform;
        root.position = new Vector3(center.x, center.y, bossZ + 0.8f);
        var fill = new MeshKit();
        var ring = new MeshKit();
        const int seg = 28;
        for (int i = 0; i < seg; i++)
        {
            float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
            fill.Tri(Vector3.zero, d0 * radius, d1 * radius, Vector3.back);
            Vector3 o0 = d0 * radius, o1 = d1 * radius, i0 = d0 * (radius - 0.25f), i1 = d1 * (radius - 0.25f);
            ring.Tri(i0, o0, o1, Vector3.back);
            ring.Tri(i0, o1, i1, Vector3.back);
        }
        AddTelegraphMesh(root, fill.ToMesh("WarnFill"), true);
        AddTelegraphMesh(root, ring.ToMesh("WarnRing"), false);
        telegraphs.Add(root.gameObject);
        return root.gameObject;
    }

    GameObject MakeRectTelegraph(Vector3 center, Vector2 size)
    {
        Transform root = new GameObject("SlingWarning").transform;
        root.position = new Vector3(center.x, center.y, bossZ + 0.8f);
        float hx = size.x * 0.5f, hy = size.y * 0.5f, e = 0.25f;
        var fill = new MeshKit();
        fill.Prism(new[] { new Vector3(-hx, -hy, 0f), new Vector3(hx, -hy, 0f), new Vector3(hx, hy, 0f), new Vector3(-hx, hy, 0f) }, Vector3.forward, 0.01f);
        var edge = new MeshKit();
        edge.Prism(new[] { new Vector3(-hx, -hy, 0f), new Vector3(-hx + e, -hy, 0f), new Vector3(-hx + e, hy, 0f), new Vector3(-hx, hy, 0f) }, Vector3.forward, 0.02f);
        edge.Prism(new[] { new Vector3(hx - e, -hy, 0f), new Vector3(hx, -hy, 0f), new Vector3(hx, hy, 0f), new Vector3(hx - e, hy, 0f) }, Vector3.forward, 0.02f);
        AddTelegraphMesh(root, fill.ToMesh("WarnFill"), true);
        AddTelegraphMesh(root, edge.ToMesh("WarnEdge"), false);
        telegraphs.Add(root.gameObject);
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

    void PulseTelegraphs()
    {
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 14f);
        var fill = new MaterialPropertyBlock();
        fill.SetColor("_BaseColor", new Color(1f, 0.05f, 0.03f, Mathf.Lerp(0.2f, 0.42f, blink)));
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

    // ───────────────────────── UI ─────────────────────────

    // 컷씬용 검은 띠 + 큰 제목·부제 + 패턴 이름(외침) + 조준선 + 하얀 번쩍임 / 엔딩: 검은 화면 + 문구 (버튼보다 아래에 그려짐)
    void BuildUI()
    {
        Font font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 40);
        canvas = NewCanvas("KidBossUI", 34);
        Transform root = canvas.transform;

        flash = NewUI<Image>("Flash", root);
        flash.color = new Color(1f, 1f, 1f, 0f);
        flash.raycastTarget = false;
        Stretch(flash.rectTransform);

        barTop = Bar(root, "BarTop", 1f);
        barBottom = Bar(root, "BarBottom", 0f);

        title = NewText(root, font, 84, new Color(1f, 0.62f, 0.25f), new Vector2(0f, 150f), new Vector2(1100f, 120f));
        subtitle = NewText(root, font, 30, Color.white, new Vector2(0f, 82f), new Vector2(1100f, 50f));
        callout = NewText(root, font, 46, new Color(1f, 0.85f, 0.3f), new Vector2(0f, 230f), new Vector2(900f, 70f));

        // 조준선: 빨간 고리 + "LOCKED ON"
        Image ring = NewUI<Image>("LockRing", root);
        ring.sprite = RingSprite();
        ring.color = new Color(1f, 0.15f, 0.1f, 0.95f);
        ring.raycastTarget = false;
        lockRing = ring.rectTransform;
        lockRing.gameObject.SetActive(false);
        lockText = NewText(root, font, 32, new Color(1f, 0.2f, 0.15f), Vector2.zero, new Vector2(400f, 50f));
        lockText.text = "LOCKED ON";
        lockText.gameObject.SetActive(false);

        // 엔딩: 검은 화면 + 문구 (메인 캔버스의 RESTART/MENU 버튼보다 아래에 그려지게 정렬 순서 -1)
        endCanvas = NewCanvas("KidBossEnding", -1);
        fade = NewUI<Image>("Fade", endCanvas.transform);
        fade.color = new Color(0f, 0f, 0f, 0f);
        fade.raycastTarget = false;
        Stretch(fade.rectTransform);
        // 가운데 RESTART / MENU 버튼보다 위에 차례로. 깨달음 문장은 따로 강조(금빛), 마지막 줄은 THE END
        string[] lines =
        {
            "작은 갈매기는 낯선 도시에서 쓰러지고 말았다...",
            "갈매기는 깨달았다.",
            "그동안에 고통과 시련들은 하늘을 나는 것이 자유가 아니었음을...",
            "하늘은 새에게 자유를 준 적이 없다.",
            "THE END",
        };
        int[] sizes = { 28, 30, 28, 38, 60 };
        float[] ys = { 318f, 268f, 228f, 172f, 104f };
        Color plain = new Color(0.92f, 0.92f, 0.95f, 0f), gold = new Color(1f, 0.78f, 0.35f, 0f);
        Color[] colors = { plain, plain, plain, gold, gold };
        endLines = new Text[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            Text t = NewText(endCanvas.transform, font, sizes[i], colors[i], new Vector2(0f, ys[i]), new Vector2(1240f, 70f));
            t.text = lines[i];
            endLines[i] = t;
        }
        endCanvas.enabled = false;
    }

    Canvas NewCanvas(string name, int order)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = order;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1f;
        return c;
    }

    IEnumerator ShowTitle(string main, string sub, float hold)
    {
        title.text = main;
        subtitle.text = sub;
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            float e = Mathf.SmoothStep(0f, 1f, t / 0.25f);
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, e);
            SetAlpha(title, e); SetAlpha(subtitle, e);
            yield return null;
        }
        title.rectTransform.localScale = Vector3.one;
        yield return new WaitForSeconds(hold);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { SetAlpha(title, 1f - t / 0.4f); SetAlpha(subtitle, 1f - t / 0.4f); yield return null; }
        title.text = subtitle.text = "";
    }

    IEnumerator ShowCallout(string text)
    {
        callout.text = text;
        for (float t = 0f; t < 1.4f; t += Time.deltaTime)
        {
            float a = t < 0.15f ? t / 0.15f : t > 1.1f ? (1.4f - t) / 0.3f : 1f;
            SetAlpha(callout, a);
            callout.rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0f, 0.15f - t) * 3f);
            yield return null;
        }
        callout.text = "";
    }

    static void SetAlpha(Text t, float a) { t.color = new Color(t.color.r, t.color.g, t.color.b, a); }

    Text NewText(Transform parent, Font font, int size, Color color, Vector2 pos, Vector2 box)
    {
        Text t = NewUI<Text>("Text", parent);
        t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        Outline o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.08f, 0.05f, 0.1f, 0.95f);
        o.effectDistance = new Vector2(2f, -2f);
        t.rectTransform.anchoredPosition = pos;
        t.rectTransform.sizeDelta = box;
        return t;
    }

    RectTransform Bar(Transform parent, string name, float y)
    {
        Image img = NewUI<Image>(name, parent);
        img.color = Color.black;
        img.raycastTarget = false;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, y);
        rt.anchorMax = new Vector2(1f, y);
        rt.pivot = new Vector2(0.5f, y);
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    IEnumerator Letterbox(bool on, float duration)
    {
        float from = barTop.sizeDelta.y, to = on ? 90f : 0f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float hgt = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, hgt);
            yield return null;
        }
        barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, to);
    }

    // 조준선 고리 (십자 눈금 4개 포함)
    static Sprite RingSprite()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.06f);
                float tick = (Mathf.Abs(dx) < 0.035f || Mathf.Abs(dy) < 0.035f) && r > 0.55f && r < 1f ? 1f : 0f;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Max(ring, tick));
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static T NewUI<T>(string name, Transform parent) where T : Graphic
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return g.AddComponent<T>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
