using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// 🦅 사막 스테이지 500m 보스: 모래 독수리 추격전 (35초 버티기)
// 1. 등장 컷씬 (위아래 검은 띠): 머리 위로 휙 지나감 → 앞에서 돌아서 울부짖음 + 이름 → 새에게 내리꽂으며 카메라가 앞으로 돌아감
// 2. 추격전: 카메라가 새 앞에서 새 얼굴을 봄(독수리는 뒤에서 쫓아옴). 화면 기준으로 좌우 조작이 맞게 반대로 바뀜
//    패턴(붉은 경고 후): 급강하(내 위치 한 점) / 날개 베기(가로 또는 세로 띠) / 깃털 날리기(여러 점). 시간이 갈수록 빨라짐
// 3. 35초 버티면 독수리가 지쳐서 날아가고 클리어
//
// 공격은 새를 따라 움직이는 "아레나" 안에서 계산 (로컬 x/y = 월드와 같음, z = 새로부터 앞(+)/뒤(-) 거리)
public class EagleChase : MonoBehaviour
{
    [Header("연결")]
    public BirdController bird;
    public NewMonoBehaviourScript obstacleSpawner;

    [Header("UI")]
    public GameObject bossHud;
    public RectTransform timeFill;          // 남은 시간 막대
    public TextMeshProUGUI bossName;        // "SAND EAGLE  35s"
    public TMP_FontAsset titleFont;
    public Material titleMaterial;

    [Header("머티리얼")]
    public Material bodyMat, wingMat, covertMat, primaryMat, whiteMat, beakMat, eyeMat, pupilMat, clawMat;
    public Material telegraphMat;
    public Material trailMat;

    [Header("사운드")]
    public AudioClip bossBgm;
    public AudioClip screechSound;
    public AudioClip whooshSound;

    [Header("설정")]
    public float surviveSeconds = 40f;
    public float eagleScale = 1.8f;
    public float attackRange = 1.3f;        // 공격 범위 배율 (경고 표시와 판정 크기 둘 다)
    public float telegraphTime = 1f;        // 빨간 경고가 뜨고 공격이 새에게 닿기까지 걸리는 시간
    public int groundPropCount = 70;        // 보스전 동안 바닥에서 계속 흘러가는 선인장·바위 수
    public float groundY = -14f;            // 추격전 바닥 높이 (새가 갈 수 있는 바닥 -5보다 아래, 선인장 꼭대기도 그 아래)
    public Material sandMat, cactusMat, rockMat;
    public float chaseDistance = 13f;       // 평소 새 뒤쪽 거리
    public float dashSpeed = 46f;
    public float featherSpeed = 32f;
    public Vector3 chaseCamOffset = new Vector3(0f, 0.9f, 6.5f);  // 새 앞쪽 (조금 위)
    public Vector3 chaseCamLook = new Vector3(0f, 1.8f, -10f);   // 새 뒤쪽 위를 봄 → 새는 화면 아래쪽, 독수리는 가운데
    public float chaseFov = 62f;

    // ── 상태 ──
    private bool running, finished;
    private float elapsed;                  // 추격전 시작 후 지난 시간
    private Transform arena;
    private EagleModel eagle;
    private Transform eagleT;
    private Vector3 eaglePos, eagleVel;     // 아레나 로컬
    private Quaternion eagleRot = Quaternion.identity;
    private bool steering = true;           // true면 평소처럼 쫓아다니는 위치로 스스로 이동
    private Coroutine attackLoop;
    private TrailRenderer[] trails;
    private AudioSource sfx;

    // 카메라: 코루틴이 매 프레임 원하는 월드 위치/바라볼 곳/화각을 정하면 LateUpdate에서 적용
    private Transform cam;
    private Camera camComp;
    private Vector3 camBasePos;
    private Quaternion camBaseRot;
    private float camBaseFov;
    private bool camControlled;
    private Vector3 camPos, camLook;
    private float camFov, camRoll;
    private float shakeTime, shakeDuration, shakeStrength;

    // 경고 표시
    private readonly List<GameObject> telegraphs = new List<GameObject>();
    private readonly List<Renderer> teleFills = new List<Renderer>();
    private readonly List<Renderer> teleEdges = new List<Renderer>();

    // 깃털 탄
    private class Feather { public Transform t; public Vector3 vel; public GameObject mark; }

    // 추격전 바닥: 새 아래 모래 언덕 + 그 위를 지나가는 선인장·바위
    private Transform dunes, propRoot;
    private Mesh duneMesh;
    private Vector3[] duneVerts;
    private float groundRise;          // 0 → 1: 보스 등장과 함께 아래에서 차오름
    private readonly List<Transform> groundProps = new List<Transform>();
    private readonly List<Feather> feathers = new List<Feather>();

    // 컷씬 UI
    private Canvas cineCanvas;
    private RectTransform barTop, barBottom;
    private TextMeshProUGUI title, subtitle;

    private bool Dead { get { return bird == null || bird.IsGameOver; } }
    private Vector3 BirdLocal { get { return new Vector3(bird.transform.position.x, bird.transform.position.y, 0f); } }
    private Vector3 ToWorld(Vector3 local) { return arena.TransformPoint(local); }

    // BirdController가 스토리 모드 500m에서 호출 (또는 메뉴의 보스 바로 하기)
    public void Begin()
    {
        if (running) return;
        running = true;

        arena = new GameObject("EagleArena").transform;
        arena.position = new Vector3(0f, 0f, bird.transform.position.z);

        GameObject e = new GameObject("SandEagle");
        e.transform.SetParent(arena, false);
        e.transform.localScale = Vector3.one * eagleScale;
        eagle = e.AddComponent<EagleModel>();
        eagle.Build(bodyMat, wingMat, covertMat, primaryMat, whiteMat, beakMat, eyeMat, pupilMat, clawMat);
        eagleT = e.transform;
        eaglePos = new Vector3(0f, 60f, -80f); // 처음엔 화면 밖
        eagleT.localPosition = eaglePos;
        BuildTrails();

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        float v = PlayerPrefs.GetFloat("JumpVolume", 1f);
        sfx.volume = v * v; // 효과음 슬라이더와 같은 크기

        camComp = Camera.main;
        cam = camComp.transform;
        camBasePos = cam.localPosition;
        camBaseRot = cam.localRotation;
        camBaseFov = camComp.fieldOfView;

        BuildCinemaUI();
        BuildGround();
        if (bossHud != null) bossHud.SetActive(false);
        if (obstacleSpawner != null) obstacleSpawner.StopAndScatter();
        StartCoroutine(Intro());
    }

    // ───────────────────────── 1. 등장 컷씬 ─────────────────────────

    IEnumerator Intro()
    {
        // 새는 컷씬 동안 조작 없이 일정하게 날아감 (속도·높이는 여기서 직접)
        bird.cutscene = true;
        Rigidbody rb = bird.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        bird.transform.rotation = Quaternion.identity;
        float birdSpeed = bird.forwardSpeed;
        float startY = bird.transform.position.y;
        float cineY = Mathf.Clamp(startY, 2f, 8f);
        steering = false;
        camControlled = true;
        if (bossBgm != null) StartCoroutine(SwapMusic(bossBgm));
        StartCoroutine(Letterbox(true, 0.5f));

        // 첫 장면 시작 자리 (카메라 뒤 하늘)에서 출발
        eaglePos = new Vector3(bird.transform.position.x + 6f, startY + 20f, -26f);
        eagleT.localPosition = eaglePos;
        eagleRot = Quaternion.Euler(30f, 0f, 0f);
        float t = 0f;
        float hoverWorldZ = 0f;
        bool screeched = false, swooshed = false, titled = false, flyoverSound = false;
        Vector3 diveFrom = Vector3.zero;

        while (t < 6.4f)
        {
            float dt = Time.deltaTime;
            t += dt;

            // ── 새 이동: 독수리를 보고 속도를 늦췄다가(1.6~4.2초), 덤벼들면 다시 도망치듯 가속 ──
            float speed = t < 1.6f ? birdSpeed : t < 2.1f ? Mathf.Lerp(birdSpeed, 4f, (t - 1.6f) / 0.5f) : t < 4.3f ? 4f : Mathf.Lerp(4f, birdSpeed, Mathf.Clamp01((t - 4.3f) / 0.7f));
            Vector3 bp = bird.transform.position;
            bp.z += speed * dt;
            bp.y = Mathf.Lerp(bp.y, cineY, dt * 1.5f);
            bird.transform.position = bp;
            arena.position = new Vector3(0f, 0f, bp.z);
            Vector3 me = BirdLocal;

            if (t < 1.6f)
            {
                // ── 장면 A: 카메라 뒤 하늘에서 머리 위로 휙 지나가 앞으로 ──
                float u = t / 1.6f;
                Vector3 p0 = new Vector3(me.x + 6f, me.y + 20f, -26f), c = new Vector3(me.x + 2.5f, me.y - 3f, -1f), p2 = new Vector3(me.x - 3f, me.y + 8f, 30f);
                SetEagle(Bezier(p0, c, p2, u));
                eagle.tuck = Mathf.Lerp(0.9f, 0.2f, u);
                eagle.glide = 0.8f;
                eagle.lookTarget = ToWorld(me);
                if (!flyoverSound && u > 0.35f) { flyoverSound = true; Play(whooshSound, 1f); Shake(0.3f, 0.15f); }

                camPos = ToWorld(me + new Vector3(0f, 2f, -7f));
                camLook = ToWorld(me + new Vector3(0f, 2.5f, 10f));
                camFov = camBaseFov; camRoll = 0f;
                if (u > 0.45f) hoverWorldZ = arena.position.z + 30f;
            }
            else if (t < 4.3f)
            {
                // ── 장면 B: 앞쪽에서 돌아서서 제자리 날갯짓 → 울부짖기 + 이름 ──
                float u = (t - 1.6f) / 2.7f;
                Vector3 hover = new Vector3(me.x * 0.3f, cineY + 3.5f + Mathf.Sin(t * 3f) * 0.3f, hoverWorldZ - arena.position.z);
                float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 1.6f) / 0.8f));
                Vector3 pos = Vector3.Lerp(eaglePos, hover, 1f - Mathf.Exp(-dt * 4f));
                eaglePos = pos;
                eagleT.localPosition = pos;
                // 크게 기울며 U턴해서 새를 마주봄
                eagleRot = Quaternion.Euler(-10f * turn, 180f * turn, Mathf.Sin(turn * Mathf.PI) * 55f);
                eagleT.localRotation = eagleRot;
                eagle.tuck = Mathf.MoveTowards(eagle.tuck, 0f, dt * 3f);
                eagle.glide = Mathf.MoveTowards(eagle.glide, 0f, dt * 2f);
                eagle.flapRate = 1.3f; eagle.flapAmp = 48f;
                eagle.talons = Mathf.MoveTowards(eagle.talons, 1f, dt);
                eagle.tailFan = 1f;
                eagle.lookTarget = ToWorld(me);

                float screechT = t - 2.6f;
                eagle.spread = screechT > 0f ? Mathf.Clamp01(screechT / 0.25f) * Mathf.Clamp01((1.5f - screechT) / 0.3f) : 0f;
                if (!screeched && screechT > 0f) { screeched = true; Play(screechSound, 1f); Shake(0.9f, 0.18f); }
                if (!titled && t > 2.75f) { titled = true; StartCoroutine(ShowTitle("SAND EAGLE", "Boss of the Desert", 1.5f)); }

                // 카메라: 새 옆 아래에서 독수리를 올려다보며 천천히 다가감
                camPos = ToWorld(me + new Vector3(-2.1f, -0.9f, -5.2f));
                camLook = ToWorld(Vector3.Lerp(me, pos, 0.75f));
                camFov = Mathf.Lerp(55f, 40f, Mathf.SmoothStep(0f, 1f, u));
                camRoll = 6f;
                diveFrom = pos;
            }
            else
            {
                // ── 장면 C: 새에게 내리꽂으며 머리 위를 스치고 뒤로 → 카메라가 새 앞쪽으로 돌아감 ──
                float u = (t - 4.3f) / 2.1f;
                Vector3 pass = me + new Vector3(1.2f, 2.6f, 0f);
                Vector3 behind = me + new Vector3(-6f, 4f, -30f);
                Vector3 slot = ChaseSlot(me, t);
                Vector3 pos;
                if (u < 0.3f) pos = Vector3.Lerp(diveFrom, pass, Mathf.Pow(u / 0.3f, 1.4f));
                else if (u < 0.65f) pos = Vector3.Lerp(pass, behind, Mathf.SmoothStep(0f, 1f, (u - 0.3f) / 0.35f));
                else pos = Vector3.Lerp(behind, slot, Mathf.SmoothStep(0f, 1f, (u - 0.65f) / 0.35f));
                // 뒤에서 크게 돌아 다시 새를 향함
                if (u < 0.65f) SetEagle(pos);
                else
                {
                    eagleVel = (pos - eaglePos) / Mathf.Max(dt, 0.001f);
                    eaglePos = pos;
                    eagleT.localPosition = pos;
                    float turn = Mathf.SmoothStep(0f, 1f, (u - 0.65f) / 0.35f);
                    eagleRot = Quaternion.Euler(0f, Mathf.Lerp(180f, 360f, turn), Mathf.Sin(turn * Mathf.PI) * -60f);
                    eagleT.localRotation = eagleRot;
                }
                eagle.tuck = u < 0.55f ? 1f : Mathf.MoveTowards(eagle.tuck, 0f, dt * 2.5f);
                eagle.talons = u < 0.4f ? 1f : Mathf.MoveTowards(eagle.talons, 0f, dt * 2f);
                eagle.flapRate = 1.8f; eagle.flapAmp = 40f;
                eagle.lookTarget = ToWorld(me);
                if (!swooshed && u > 0.2f) { swooshed = true; Play(whooshSound, 1f); Shake(0.35f, 0.3f); SetTrails(true); }
                if (u > 0.6f) SetTrails(false);

                // 카메라: 새 뒤쪽 → 새 둘레를 반 바퀴 돌아 새 앞쪽(추격전 시점)으로
                float orbit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.2f) / 0.6f));
                camPos = ToWorld(me + OrbitOffset(orbit));
                camLook = ToWorld(me + Vector3.Lerp(new Vector3(0f, 1.5f, 10f), chaseCamLook, orbit));
                camFov = Mathf.Lerp(camBaseFov, chaseFov, orbit);
                camRoll = 0f;
            }
            yield return null;
        }

        // ── 추격전 시작 ──
        StartCoroutine(ShowTitle("SURVIVE!", Mathf.RoundToInt(surviveSeconds) + " seconds", 1.3f));
        StartCoroutine(Letterbox(false, 0.5f));
        rb.isKinematic = false;
        rb.linearVelocity = new Vector3(0f, 0f, birdSpeed);
        bird.cutscene = false;
        bird.mirrorControls = true;
        steering = true;
        if (bossHud != null) bossHud.SetActive(true);
        elapsed = 0f;
        attackLoop = StartCoroutine(AttackLoop());
    }

    // 카메라가 새 둘레를 도는 위치: 0 = 뒤(원래 시점), 1 = 앞(추격전 시점)
    Vector3 OrbitOffset(float k)
    {
        float ang = k * Mathf.PI;
        float radius = Mathf.Lerp(7f, chaseCamOffset.z, k);
        return new Vector3(Mathf.Sin(ang) * radius * 0.7f, Mathf.Lerp(2f, chaseCamOffset.y, k), -Mathf.Cos(ang) * radius);
    }

    // ───────────────────────── 2. 추격전 패턴 ─────────────────────────

    IEnumerator AttackLoop()
    {
        yield return new WaitForSeconds(1.4f); // 시점에 익숙해질 시간
        int last = -1;
        while (!Dead && !finished)
        {
            float k = Mathf.Clamp01(elapsed / surviveSeconds);
            // 처음엔 급강하·날개 베기만, 3분의 1 지나면 깃털, 3분의 2부터는 연속 공격
            int choices = k < 0.3f ? 3 : 4;
            int pick;
            do { pick = Random.Range(0, choices); } while (pick == last && choices > 1);
            last = pick;

            switch (pick)
            {
                case 0: yield return Dive(); break;
                case 1: yield return Sweep(false); break;
                case 2: yield return Sweep(true); break;
                case 3: yield return Volley(k < 0.65f ? 5 : 8); break;
            }
            if (Dead || finished) yield break;
            if (k > 0.65f && Random.value < 0.6f)
            {
                yield return new WaitForSeconds(0.15f);
                yield return Dive(); // 연속 급강하
                if (Dead || finished) yield break;
            }
            yield return new WaitForSeconds(Mathf.Lerp(0.9f, 0.35f, k));
        }
    }

    // 급강하: 내 위치에 붉은 원 → 뒤로 물러나며 힘 모음 → 일직선으로 내리꽂고 머리 위로 빠져나감
    IEnumerator Dive()
    {
        Vector3 target = ClampTarget(BirdLocal);
        GameObject mark = MakeCircleTelegraph(target, 2.1f * attackRange);
        Vector3 wind = new Vector3(Mathf.Lerp(eaglePos.x, target.x, 0.6f), target.y + 3.5f, -20f);
        // 힘 모으기 + 돌진해 오는 시간 = 경고 시간 (경고는 독수리가 닿을 때까지 그대로)
        yield return Windup(wind, target, ImpactWindup(wind, target, dashSpeed), 0.6f, 0f);
        if (Dead || finished) { RemoveTelegraph(mark); yield break; }

        Vector3 dir = (target - eaglePos).normalized;
        Vector3 end = target + dir * (0.8f / Mathf.Max(0.2f, dir.z)); // 새를 막 지나친 자리에서 바로 솟구침 (카메라를 가리지 않게)
        yield return Dash(end, 1f, 0f, mark, (prev, cur) => DistanceToSegment(BirdLocal, prev, cur) < 1.7f * attackRange);
        if (Dead || finished) yield break;
        yield return Recover();
    }

    // 날개 베기: 날개를 활짝 편 채 가로(또는 몸을 90도 세워 세로) 띠 전체를 쓸고 지나감 → 띠 밖으로 피해야 함
    IEnumerator Sweep(bool vertical)
    {
        Vector3 target = ClampTarget(BirdLocal);
        Vector2 size = (vertical ? new Vector2(3.4f, 19f) : new Vector2(19f, 3.4f)) * attackRange;
        GameObject mark = MakeRectTelegraph(target, size);
        float roll = vertical ? 90f : 0f;
        Vector3 start = new Vector3(target.x, target.y, -21f);
        yield return Windup(start, target, ImpactWindup(start, target, dashSpeed), 0f, roll);
        if (Dead || finished) { RemoveTelegraph(mark); yield break; }

        eagle.spread = 0f;
        Vector3 end = new Vector3(target.x, target.y, 0.8f);
        Vector2 half = size * 0.5f - new Vector2(0.45f, 0.45f); // 가장자리 스치면 봐줌
        yield return Dash(end, 0f, roll, mark, (prev, cur) =>
        {
            if (prev.z > 0f || cur.z < 0f) return false; // 새가 있는 면(z=0)을 지나는 순간만 판정
            Vector3 b = BirdLocal;
            return Mathf.Abs(b.x - target.x) < half.x && Mathf.Abs(b.y - target.y) < half.y;
        });
        if (Dead || finished) yield break;
        yield return Recover();
    }

    // 깃털 날리기: 뒤에서 몸을 세우고 크게 날갯짓 → 붉은 점들로 날카로운 깃털이 날아옴 (하나는 꼭 내 위치)
    IEnumerator Volley(int count)
    {
        Vector3 me = ClampTarget(BirdLocal);
        var targets = new List<Vector3> { me };
        for (int i = 1; i < count; i++)
        {
            Vector2 off = Random.insideUnitCircle.normalized * Random.Range(2.6f, 7f) * attackRange;
            targets.Add(ClampTarget(me + new Vector3(off.x, off.y * 0.7f, 0f)));
        }
        var marks = new List<GameObject>();
        foreach (Vector3 t in targets) marks.Add(MakeCircleTelegraph(t, 1.15f * attackRange));

        // 뒤에서 멈춰 몸을 세움 (경고 시간 동안)
        steering = false;
        Vector3 from = eaglePos, perch = new Vector3(me.x, me.y + 3f, -15f);
        float hold = ImpactWindup(perch, me, featherSpeed);
        float w = 0f;
        while (w < hold)
        {
            if (Dead || finished) { foreach (GameObject m in marks) RemoveTelegraph(m); yield break; }
            w += Time.deltaTime;
            float e = Mathf.SmoothStep(0f, 1f, w / hold);
            MoveEagle(Vector3.Lerp(from, perch, e), -25f * e, 0f);
            eagle.spread = e * 0.7f;
            eagle.tailFan = 1f;
            eagle.lookTarget = ToWorld(BirdLocal);
            PulseTelegraphs(w / telegraphTime);
            yield return null;
        }

        eagle.spread = 0f;
        eagle.Burst();
        Play(whooshSound, 1.3f);
        for (int i = 0; i < targets.Count; i++)
        {
            Vector3 t = targets[i];
            Vector3 start = eaglePos + new Vector3(Random.Range(-3f, 3f), Random.Range(-0.5f, 1f), 1f);
            Transform f = MakeFeatherShot(start);
            Vector3 vel = (t - start).normalized * featherSpeed;
            f.localRotation = Quaternion.LookRotation(vel);
            feathers.Add(new Feather { t = f, vel = vel, mark = marks[i] });
        }
        // 날갯짓 반동으로 살짝 뒤로 밀렸다가 다시 쫓아옴
        float r = 0f;
        Vector3 p0 = eaglePos;
        while (r < 0.5f && !Dead && !finished)
        {
            r += Time.deltaTime;
            MoveEagle(p0 + new Vector3(0f, 0.8f, -3f) * Mathf.Sin(r / 0.5f * Mathf.PI * 0.5f), Mathf.Lerp(-25f, 0f, r / 0.5f), 0f);
            yield return null;
        }
        steering = true;
    }

    // 공격 시작 자리로 이동 + 경고 깜빡임. 시간이 지날수록 날개를 접고 발톱을 내밂
    IEnumerator Windup(Vector3 to, Vector3 target, float duration, float tuckTo, float roll)
    {
        steering = false;
        Vector3 from = eaglePos;
        float w = 0f;
        while (w < duration)
        {
            if (Dead || finished) yield break;
            w += Time.deltaTime;
            float e = Mathf.SmoothStep(0f, 1f, w / duration);
            Vector3 p = Vector3.Lerp(from, to, e) + Random.insideUnitSphere * 0.15f * e; // 힘 모으며 부르르
            Vector3 aim = target - p;
            float pitch = -Mathf.Atan2(aim.y, aim.z) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            eaglePos = p;
            eagleT.localPosition = p;
            eagleRot = Quaternion.Slerp(eagleRot, Quaternion.Euler(pitch * e, yaw * e, roll * e), 1f - Mathf.Exp(-Time.deltaTime * 8f));
            eagleT.localRotation = eagleRot;
            eagle.tuck = tuckTo * e;
            eagle.glide = roll != 0f || tuckTo == 0f ? e : 0f;
            eagle.talons = tuckTo > 0f ? e : 0f;
            eagle.spread = tuckTo == 0f ? e * 0.5f : 0f; // 날개 베기: 날개를 쫙 펴 보임
            eagle.flapRate = Mathf.Lerp(1.8f, 3f, e);
            eagle.lookTarget = ToWorld(target);
            PulseTelegraphs(w / duration);
            yield return null;
        }
    }

    // 일직선 돌진. hit(이전 위치, 현재 위치)이 참이면 게임오버
    IEnumerator Dash(Vector3 end, float tuck, float roll, GameObject mark, System.Func<Vector3, Vector3, bool> hit)
    {
        Play(whooshSound, 1f);
        SetTrails(true);
        eagle.tuck = tuck;
        eagle.glide = 1f;
        bool shook = false;
        while ((end - eaglePos).sqrMagnitude > 0.0001f)
        {
            if (Dead || finished) break;
            Vector3 prev = eaglePos;
            eaglePos = Vector3.MoveTowards(prev, end, dashSpeed * Time.deltaTime);
            eagleT.localPosition = eaglePos;
            Vector3 dir = end - prev;
            if (dir.sqrMagnitude > 0.001f) eagleRot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, roll);
            eagleT.localRotation = eagleRot;
            if (!shook && eaglePos.z > -1f) { shook = true; Shake(0.3f, 0.35f); }
            if (mark != null && eaglePos.z >= -0.6f) { RemoveTelegraph(mark); mark = null; }
            else PulseTelegraphs(1f);
            if (hit(prev, eaglePos)) { bird.GameOver(); break; }
            yield return null;
        }
        if (mark != null) RemoveTelegraph(mark);
        SetTrails(false);
    }

    // 힘 모으는 시간: 경고 시간에서 from → target까지 날아오는 시간을 뺀 만큼 (그래야 경고 후 딱 그 시간에 닿음)
    float ImpactWindup(Vector3 from, Vector3 target, float speed)
    {
        return Mathf.Max(0.3f, telegraphTime - Vector3.Distance(from, target) / speed);
    }

    // 공격 후: 새를 막 지나친 자리에서 위로 솟구치며 바깥쪽으로 크게 돌아(윙오버) 다시 뒤쪽 쫓는 자리로
    // 끊김 없이 한 번에 이어지는 곡선이라 화면에서 사라졌다 나타나는 일이 없음
    IEnumerator Recover()
    {
        steering = false;
        Vector3 start = eaglePos;
        float dx = start.x - BirdLocal.x;
        float side = Mathf.Abs(dx) > 0.5f ? Mathf.Sign(dx) : (Random.value < 0.5f ? -1f : 1f); // 새와 먼 쪽으로 돎
        const float dur = 1.6f;
        float t = 0f;
        Vector3 vel = Vector3.forward;
        while (t < dur)
        {
            if (Dead || finished) yield break;
            float dt = Time.deltaTime;
            t += dt;
            float u = Mathf.Clamp01(t / dur);
            Vector3 slot = ChaseSlot(BirdLocal, Time.time);
            // 화면 밖으로 안 나가도록 위보다는 옆으로 넓게 돎
            Vector3 c1 = start + new Vector3(side * 9f, 7f, -6f);
            Vector3 c2 = slot + new Vector3(side * 10f, 6f, -10f);
            Vector3 p = Cubic(start, c1, c2, slot, u);
            if (dt > 0f) vel = (p - eaglePos) / dt;
            eaglePos = p;
            eagleT.localPosition = p;
            // 날아가는 방향을 보며, 도는 쪽으로 크게 기울였다가 자리에 오면 수평
            if (vel.sqrMagnitude > 1f)
            {
                Quaternion want = Quaternion.LookRotation(vel.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, -side * 60f * Mathf.Sin(u * Mathf.PI));
                eagleRot = Quaternion.Slerp(eagleRot, want, 1f - Mathf.Exp(-dt * 9f));
                eagleT.localRotation = eagleRot;
            }
            // 솟구칠 땐 힘차게 날갯짓 → 꼭대기에선 날개 펴고 활공 → 내려오며 다시 날갯짓
            eagle.tuck = Mathf.Clamp01(1f - u * 4f);
            eagle.talons = Mathf.Clamp01(1f - u * 3f);
            eagle.spread = 0f;
            eagle.glide = Mathf.Sin(Mathf.Clamp01((u - 0.25f) / 0.5f) * Mathf.PI) * 0.9f;
            eagle.flapRate = Mathf.Lerp(2.8f, 1.8f, u);
            eagle.flapAmp = 46f;
            eagle.tailFan = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI);
            eagle.lookTarget = bird.transform.position;
            yield return null;
        }
        eagleVel = vel * 0.3f; // 쫓아오기 움직임으로 부드럽게 이어지게
        steering = true;
    }

    // ───────────────────────── 3. 클리어 ─────────────────────────

    // 40초 버팀 → 컷씬 (위아래 검은 띠)
    // 1. 마지막으로 발톱을 뻗어 덮치지만, 새가 힘껏 가속해서 점점 멀어짐 (앞쪽 시점 그대로)
    // 2. 독수리 등 뒤 시점: 새는 저 멀리 점이 되고, 독수리는 속도를 줄여 멈춰서 분해서 울부짖음
    // 3. 포기하고 옆으로 돌아 하늘로 날아가 버림 → 새 뒤 시점으로 돌아와 클리어
    IEnumerator Victory()
    {
        finished = true;
        if (attackLoop != null) StopCoroutine(attackLoop);
        ClearTelegraphs();
        foreach (Feather f in feathers) if (f.t != null) Destroy(f.t.gameObject);
        feathers.Clear();
        SetTrails(false);
        bird.mirrorControls = false;
        steering = false;
        if (bossHud != null) bossHud.SetActive(false);
        StartCoroutine(Letterbox(true, 0.4f));

        // 새: 조작을 멈추고 힘껏 가속해서 도망
        bird.cutscene = true;
        Rigidbody rb = bird.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        float birdSpeed = bird.forwardSpeed;
        float escapeY = Mathf.Clamp(bird.transform.position.y, 1f, 9f);

        // 독수리는 아레나에서 떼어 내 세상에 둠 (새가 멀어지는 게 그대로 보이게)
        eagleT.SetParent(null, true);
        Vector3 ew = eagleT.position;
        Quaternion er = eagleT.rotation;
        float eagleSpeed = birdSpeed * 1.2f;
        bool lunged = false, screeched = false, titled = false, music = false;
        Vector3 standCam = Vector3.zero;

        float t = 0f;
        while (t < 6f)
        {
            float dt = Time.deltaTime;
            t += dt;

            // 새 이동 (컷씬 내내 계속 앞으로)
            Vector3 bp = bird.transform.position;
            bp.z += Mathf.Lerp(birdSpeed, birdSpeed * 2.2f, Mathf.Clamp01(t / 1f)) * dt;
            bp.x = Mathf.Lerp(bp.x, 0f, dt * 0.8f);
            bp.y = Mathf.Lerp(bp.y, escapeY, dt * 2f);
            bird.transform.position = bp;
            bird.transform.rotation = Quaternion.Slerp(bird.transform.rotation, Quaternion.identity, dt * 5f);
            arena.position = new Vector3(0f, 0f, bp.z);

            if (t < 1.8f)
            {
                // ── 장면 1: 덮치려다 놓침 ──
                float u = t / 1.8f;
                eagleSpeed = Mathf.Lerp(birdSpeed * 1.25f, birdSpeed * 0.9f, u);
                ew.z += eagleSpeed * dt;
                ew.x = Mathf.Lerp(ew.x, bp.x, dt * 3f);
                ew.y = Mathf.Lerp(ew.y, bp.y + 1.2f, dt * 3f);
                er = Quaternion.Slerp(er, Quaternion.Euler(-8f, 0f, 0f), 1f - Mathf.Exp(-dt * 6f));
                eagle.talons = u < 0.7f ? 1f : Mathf.MoveTowards(eagle.talons, 0f, dt * 3f);
                eagle.tuck = 0f; eagle.glide = 0f; eagle.spread = 0f;
                eagle.flapRate = 3.2f; eagle.flapAmp = 52f; // 따라잡으려고 미친 듯이 날갯짓
                eagle.beakOpen = 1f;
                eagle.lookTarget = bp;
                if (!lunged && u > 0.15f) { lunged = true; Play(whooshSound, 0.9f); }

                Vector3 me = bp;
                camPos = me + chaseCamOffset;
                camLook = me + chaseCamLook;
                camFov = chaseFov; camRoll = 0f;
            }
            else if (t < 4.2f)
            {
                // ── 장면 2: 독수리 등 뒤에서 – 새는 점이 되어 사라지고, 독수리는 멈춰 울부짖음 ──
                float u = (t - 1.8f) / 2.4f;
                float brake = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.6f));
                eagleSpeed = Mathf.Lerp(birdSpeed * 0.9f, 0f, brake);
                ew.z += eagleSpeed * dt;
                ew.y += Mathf.Sin(t * 4f) * 0.4f * dt;
                // 급제동: 몸을 세우고 꼬리·날개를 펴서 공기를 받음
                er = Quaternion.Slerp(er, Quaternion.Euler(-28f * brake, 0f, 0f), 1f - Mathf.Exp(-dt * 5f));
                eagle.flapRate = Mathf.Lerp(3f, 1.3f, brake);
                eagle.flapAmp = 50f;
                eagle.tailFan = 1f;
                eagle.talons = 0.4f * brake;
                eagle.beakOpen = 0f;
                eagle.lookTarget = bp;

                float screechT = t - 3.0f;
                eagle.spread = screechT > 0f ? Mathf.Clamp01(screechT / 0.25f) * Mathf.Clamp01((1.1f - screechT) / 0.3f) : 0f;
                if (!screeched && screechT > 0f) { screeched = true; Play(screechSound, 0.85f); Shake(0.8f, 0.12f); }
                if (!titled && t > 3.1f) { titled = true; StartCoroutine(ShowTitle("ESCAPED!", "The eagle lost you", 1.6f)); }

                // 카메라: 독수리 오른쪽 뒤 위 (독수리 너머로 새가 멀어지는 게 보이게)
                camPos = ew + new Vector3(7.5f, 3.5f, -17f);
                camLook = ew + new Vector3(0f, 1f, 16f);
                camFov = 50f; camRoll = -4f;
                standCam = camPos;
            }
            else
            {
                // ── 장면 3: 포기하고 옆으로 돌아 하늘로 날아가 버림 (카메라는 그 자리에서 지켜봄) ──
                float u = (t - 4.2f) / 1.8f;
                float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.4f));
                er = Quaternion.Euler(Mathf.Lerp(-28f, -18f, turn), 150f * turn, -55f * Mathf.Sin(turn * Mathf.PI));
                ew += er * Vector3.forward * Mathf.Lerp(2f, 16f, turn) * dt + Vector3.up * 5f * dt;
                eagle.spread = 0f; eagle.talons = 0f;
                eagle.flapRate = 2f; eagle.flapAmp = 44f;
                eagle.glide = Mathf.Clamp01((u - 0.6f) * 3f) * 0.7f;
                eagle.tailFan = 0.6f;
                eagle.lookTarget = null;
                if (!music) { music = true; if (bossBgm != null) StartCoroutine(SwapMusic(bird.StoryMusic)); }

                camPos = standCam;
                camLook = Vector3.Lerp(camLook, ew, 1f - Mathf.Exp(-dt * 4f));
            }

            eagleT.position = ew;
            eagleT.rotation = er;
            yield return null;
        }

        // 새 뒤 시점으로 돌아와 클리어
        StartCoroutine(Letterbox(false, 0.4f));
        camControlled = false;
        cam.localPosition = camBasePos;
        cam.localRotation = camBaseRot;
        camComp.fieldOfView = camBaseFov;
        Destroy(eagleT.gameObject);
        DestroyGround();
        bird.cutscene = false;
        rb.isKinematic = false; // StageClear가 속도를 0으로 맞춘 뒤 다시 고정함
        if (!Dead) bird.StageClear();
    }

    // ───────────────────────── 매 프레임 ─────────────────────────

    void Update()
    {
        if (!running || finished || Dead || bird.cutscene || attackLoop == null) return;
        elapsed += Time.deltaTime;
        float left = Mathf.Max(0f, surviveSeconds - elapsed);
        if (timeFill != null) timeFill.anchorMax = new Vector2(left / surviveSeconds, 1f);
        if (bossName != null) bossName.text = "SAND EAGLE   " + Mathf.CeilToInt(left) + "s";
        if (left <= 0f) StartCoroutine(Victory());
    }

    void LateUpdate()
    {
        if (!running || arena == null) return;
        arena.position = new Vector3(0f, 0f, bird.transform.position.z);

        // 평소: 새 뒤에서 좌우로 흔들리며 쫓아옴 (몸은 움직이는 방향으로 기울임)
        if (steering && !finished && !Dead)
        {
            Vector3 want = ChaseSlot(BirdLocal, Time.time);
            Vector3 p = Vector3.SmoothDamp(eaglePos, want, ref eagleVel, 0.55f, 60f);
            eaglePos = p;
            eagleT.localPosition = p;
            float bank = Mathf.Clamp(-eagleVel.x * 3.5f, -50f, 50f);
            float pitch = Mathf.Clamp(-eagleVel.y * 2.5f, -30f, 30f);
            eagleRot = Quaternion.Slerp(eagleRot, Quaternion.Euler(pitch, eagleVel.x * 1.2f, bank), 1f - Mathf.Exp(-Time.deltaTime * 5f));
            eagleT.localRotation = eagleRot;
            eagle.tuck = Mathf.MoveTowards(eagle.tuck, 0f, Time.deltaTime * 2f);
            eagle.talons = Mathf.MoveTowards(eagle.talons, 0f, Time.deltaTime * 2f);
            eagle.spread = Mathf.MoveTowards(eagle.spread, 0f, Time.deltaTime * 3f);
            // 따라잡느라 바쁠 땐 힘차게, 거리가 맞으면 활공 섞어서
            float catchUp = Mathf.Clamp01((want - p).magnitude / 10f);
            eagle.flapRate = Mathf.Lerp(1.5f, 2.6f, catchUp);
            eagle.flapAmp = Mathf.Lerp(34f, 46f, catchUp);
            eagle.glide = Mathf.MoveTowards(eagle.glide, Mathf.PerlinNoise(Time.time * 0.4f, 3.1f) > 0.6f && catchUp < 0.2f ? 0.8f : 0f, Time.deltaTime * 1.5f);
            eagle.tailFan = 0.4f + Mathf.Abs(bank) / 60f;
            eagle.lookTarget = bird.transform.position;
        }

        // 깃털 탄 이동 + 판정
        for (int i = feathers.Count - 1; i >= 0; i--)
        {
            Feather f = feathers[i];
            if (f.t == null) { feathers.RemoveAt(i); continue; }
            Vector3 prev = f.t.localPosition;
            Vector3 cur = prev + f.vel * Time.deltaTime;
            f.t.localPosition = cur;
            f.t.localRotation = Quaternion.LookRotation(f.vel) * Quaternion.Euler(0f, 0f, Time.time * 720f);
            if (!Dead && !finished && DistanceToSegment(BirdLocal, prev, cur) < 0.85f * attackRange) bird.GameOver();
            if (f.mark != null && cur.z >= -0.6f) { RemoveTelegraph(f.mark); f.mark = null; }
            if (cur.z > 14f) { if (f.mark != null) RemoveTelegraph(f.mark); Destroy(f.t.gameObject); feathers.RemoveAt(i); }
        }
        if (feathers.Count > 0) PulseTelegraphs(1f);

        UpdateGround();
        UpdateCamera();
    }

    // 평소 쫓아오는 자리: 새 뒤쪽 조금 위, 좌우로 천천히 흔들
    Vector3 ChaseSlot(Vector3 me, float time)
    {
        return new Vector3(me.x + Mathf.Sin(time * 0.7f) * 3f, me.y + 2.6f + Mathf.Sin(time * 1.3f) * 0.6f, -chaseDistance);
    }

    void UpdateCamera()
    {
        if (cam == null || !camControlled) return;
        if (bird.mirrorControls && !finished)
        {
            // 추격전: 새 앞쪽 위에서 새 얼굴과 뒤에 쫓아오는 독수리를 같이 봄 (새가 기울어도 화면은 수평 유지)
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
        cam.position = pos;
        cam.rotation = Quaternion.LookRotation(camLook - camPos) * Quaternion.Euler(0f, 0f, camRoll);
        camComp.fieldOfView = camFov;
    }

    // ───────────────────────── 바닥 (추격전 속도감) ─────────────────────────
    // 사막 배경 모형의 땅은 새보다 한참 아래라 추격전 시점에선 안 보임
    // → 보스전 동안 새 바로 아래에 모래 언덕 바닥을 깔고, 그 위로 선인장·바위가 계속 지나가게 함 (판정 없음, 보기만)

    const int DuneCellsX = 36, DuneCellsZ = 44;
    const float DuneHalfWidth = 170f, DuneNearZ = -330f, DuneFarZ = 140f;

    // 언덕 높이: 세상 좌표 기준이라 새가 날아가면 언덕이 뒤로 흘러감
    float DuneHeight(float x, float worldZ)
    {
        return groundY - 18f * (1f - groundRise)
             + Mathf.Sin(worldZ * 0.045f + x * 0.02f) * 1.4f
             + Mathf.Sin(worldZ * 0.11f - x * 0.07f) * 0.7f
             + (Mathf.PerlinNoise(x * 0.03f + 7.3f, worldZ * 0.03f) - 0.5f) * 2.2f;
    }

    // 가운데(새 근처)일수록 촘촘한 격자
    static float DuneGridX(int i)
    {
        float u = i / (float)DuneCellsX * 2f - 1f;
        return Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), 1.7f) * DuneHalfWidth;
    }

    static float DuneGridZ(int i)
    {
        float u = i / (float)DuneCellsZ * 2f - 1f;                // -1(뒤) ~ 1(앞), 0 = 새
        float k = Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), 1.6f);
        return k < 0f ? -k * DuneNearZ : k * DuneFarZ;            // 뒤쪽 330m, 앞쪽 140m
    }

    void BuildGround()
    {
        if (sandMat == null) return;
        GameObject g = new GameObject("ChaseDunes");
        g.transform.SetParent(arena, false);
        duneMesh = new Mesh { name = "ChaseDunes" };
        duneMesh.MarkDynamic();
        duneVerts = new Vector3[DuneCellsX * DuneCellsZ * 6];
        var tris = new int[duneVerts.Length];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;
        duneMesh.vertices = duneVerts;
        duneMesh.triangles = tris;
        g.AddComponent<MeshFilter>().sharedMesh = duneMesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = sandMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dunes = g.transform;

        propRoot = new GameObject("ChaseProps").transform; // 소품은 세상에 고정 (새가 지나쳐 감)
        float bz = bird.transform.position.z, bx = bird.transform.position.x;
        for (int i = 0; i < groundPropCount; i++)
        {
            bool tall = i % 3 != 2; // 선인장 2 : 바위 1
            Transform p = tall ? MakeCactusProp(i) : MakeRockProp(i);
            float z = bz + Mathf.Lerp(-320f, 130f, i / (float)groundPropCount) + Random.Range(-3f, 3f);
            PlaceProp(p, bx + PropX(tall), z);
            groundProps.Add(p);
        }
        UpdateGround();
    }

    // 새 기준 좌우 거리: 선인장은 새가 다니는 가운데를 비워 멀찍이, 낮은 바위는 가까이도
    static float PropX(bool tall)
    {
        float side = Random.value < 0.5f ? -1f : 1f;
        return tall ? side * Random.Range(6f, 70f) : side * Random.Range(2f, 60f);
    }

    void PlaceProp(Transform p, float x, float worldZ)
    {
        p.position = new Vector3(x, DuneHeight(x, worldZ) - 0.3f, worldZ);
    }

    // 몸통 + 양쪽 팔 (사막 스테이지의 선인장과 같은 모양). 키는 새가 갈 수 있는 바닥보다 낮게
    Transform MakeCactusProp(int seed)
    {
        Transform c = new GameObject("ChaseCactus").transform;
        c.SetParent(propRoot, false);
        c.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        float height = Random.Range(3f, 5.5f);
        float w = height * 0.16f;
        PropPart(PrimitiveType.Capsule, c, cactusMat, new Vector3(0f, height * 0.5f, 0f), new Vector3(w, height * 0.5f, w), Vector3.zero);
        for (int side = -1; side <= 1; side += 2)
        {
            if (Random.value < 0.25f) continue; // 팔이 하나뿐인 것도
            float armY = height * Random.Range(0.35f, 0.6f);
            float armLen = height * Random.Range(0.18f, 0.26f);
            float armUp = height * Random.Range(0.18f, 0.3f);
            float aw = w * 0.75f;
            PropPart(PrimitiveType.Capsule, c, cactusMat, new Vector3(side * armLen * 0.5f, armY, 0f), new Vector3(aw, armLen * 0.5f, aw), new Vector3(0f, 0f, 90f));
            PropPart(PrimitiveType.Capsule, c, cactusMat, new Vector3(side * armLen, armY + armUp * 0.5f, 0f), new Vector3(aw, armUp * 0.5f + aw * 0.5f, aw), Vector3.zero);
        }
        return c;
    }

    // 붉은 사암 바위 몇 개를 겹친 낮은 바위 더미
    Transform MakeRockProp(int seed)
    {
        Transform r = new GameObject("ChaseRock").transform;
        r.SetParent(propRoot, false);
        int n = Random.Range(1, 4);
        for (int i = 0; i < n; i++)
        {
            float s = Random.Range(0.8f, 2.2f);
            PropPart(PrimitiveType.Cube, r, rockMat,
                new Vector3(Random.Range(-1.2f, 1.2f), s * 0.3f, Random.Range(-1.2f, 1.2f)),
                new Vector3(s * Random.Range(0.9f, 1.6f), s * Random.Range(0.5f, 0.9f), s * Random.Range(0.9f, 1.4f)),
                new Vector3(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f)));
        }
        return r;
    }

    void PropPart(PrimitiveType type, Transform parent, Material mat, Vector3 localPos, Vector3 localScale, Vector3 euler)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>()); // 보기만 (이름이 "Cube"라 게임오버 판정에 걸리는 것도 방지)
        g.name = "Part";
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = localScale;
        g.transform.localEulerAngles = euler;
        Renderer rd = g.GetComponent<Renderer>();
        rd.sharedMaterial = mat;
        rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // 매 프레임: 언덕 모양 갱신(아래에서 차오름 + 흘러감) + 한참 뒤로 지나간 소품은 새 앞쪽(카메라 등 뒤)으로 옮겨 반복
    void UpdateGround()
    {
        if (duneMesh == null) return;
        groundRise = Mathf.MoveTowards(groundRise, 1f, Time.deltaTime / 2.5f);
        float az = arena.position.z;
        int v = 0;
        for (int iz = 0; iz < DuneCellsZ; iz++)
        {
            for (int ix = 0; ix < DuneCellsX; ix++)
            {
                float x0 = DuneGridX(ix), x1 = DuneGridX(ix + 1);
                float z0 = DuneGridZ(iz), z1 = DuneGridZ(iz + 1);
                Vector3 a = new Vector3(x0, DuneHeight(x0, z0 + az), z0);
                Vector3 b = new Vector3(x1, DuneHeight(x1, z0 + az), z0);
                Vector3 c = new Vector3(x0, DuneHeight(x0, z1 + az), z1);
                Vector3 d = new Vector3(x1, DuneHeight(x1, z1 + az), z1);
                // 위를 보는 면: (p0, p1, p2)의 앞면은 Cross(p1-p0, p2-p0) 방향
                duneVerts[v++] = a; duneVerts[v++] = c; duneVerts[v++] = b;
                duneVerts[v++] = b; duneVerts[v++] = c; duneVerts[v++] = d;
            }
        }
        duneMesh.vertices = duneVerts;
        duneMesh.RecalculateNormals(); // 면마다 평평한 법선 → 각진 로우폴리
        duneMesh.RecalculateBounds();

        float bz = bird.transform.position.z, bx = bird.transform.position.x;
        foreach (Transform p in groundProps)
        {
            if (p == null) continue;
            Vector3 pos = p.position;
            if (pos.z < bz - 325f) PlaceProp(p, bx + PropX(p.name == "ChaseCactus"), bz + 130f + Random.Range(-3f, 3f));
            else if (groundRise < 1f) PlaceProp(p, pos.x, pos.z); // 차오르는 동안 땅을 따라 올라옴
        }
    }

    void DestroyGround()
    {
        if (dunes != null) Destroy(dunes.gameObject);
        if (propRoot != null) Destroy(propRoot.gameObject);
        groundProps.Clear();
    }

    // ───────────────────────── 도우미 ─────────────────────────

    // 위치를 옮기고 움직이는 방향을 보게 (방향 쪽으로 기울이며)
    void SetEagle(Vector3 pos)
    {
        Vector3 vel = (pos - eaglePos) / Mathf.Max(Time.deltaTime, 0.001f);
        eagleVel = vel;
        eaglePos = pos;
        eagleT.localPosition = pos;
        if (vel.sqrMagnitude > 1f)
        {
            float bank = Mathf.Clamp(-vel.x * 2f, -45f, 45f);
            Quaternion want = Quaternion.LookRotation(vel.normalized) * Quaternion.Euler(0f, 0f, bank);
            eagleRot = Quaternion.Slerp(eagleRot, want, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            eagleT.localRotation = eagleRot;
        }
    }

    void MoveEagle(Vector3 pos, float pitch, float roll)
    {
        eagleVel = (pos - eaglePos) / Mathf.Max(Time.deltaTime, 0.001f);
        eaglePos = pos;
        eagleT.localPosition = pos;
        eagleRot = Quaternion.Slerp(eagleRot, Quaternion.Euler(pitch, 0f, roll), 1f - Mathf.Exp(-Time.deltaTime * 8f));
        eagleT.localRotation = eagleRot;
    }

    Vector3 ClampTarget(Vector3 p)
    {
        return new Vector3(Mathf.Clamp(p.x, -32f, 32f), Mathf.Clamp(p.y, bird.fallLimitY + 1f, bird.ceilingY - 1f), 0f);
    }

    static Vector3 Cubic(Vector3 a, Vector3 c1, Vector3 c2, Vector3 b, float u)
    {
        float v = 1f - u;
        return v * v * v * a + 3f * v * v * u * c1 + 3f * v * u * u * c2 + u * u * u * b;
    }

    static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float u)
    {
        float v = 1f - u;
        return v * v * a + 2f * v * u * c + u * u * b;
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

    // 날개 끝에서 하얀 바람 줄기 (돌진할 때만)
    void BuildTrails()
    {
        var list = new List<TrailRenderer>();
        foreach (Transform t in eagleT.GetComponentsInChildren<Transform>())
        {
            if (t.name != "Primary0") continue;
            GameObject g = new GameObject("WingTrail");
            g.transform.SetParent(t, false);
            g.transform.localPosition = new Vector3(t.parent.parent.localPosition.x > 0f ? 1.3f : -1.3f, 0f, 0f);
            TrailRenderer tr = g.AddComponent<TrailRenderer>();
            tr.sharedMaterial = trailMat;
            tr.time = 0.35f;
            tr.startWidth = 0.25f;
            tr.endWidth = 0f;
            tr.emitting = false;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            list.Add(tr);
        }
        trails = list.ToArray();
    }

    void SetTrails(bool on)
    {
        if (trails == null) return;
        foreach (TrailRenderer t in trails) { if (on) t.Clear(); t.emitting = on; }
    }

    Transform MakeFeatherShot(Vector3 localPos)
    {
        GameObject g = new GameObject("FeatherShot");
        g.transform.SetParent(arena, false);
        g.transform.localPosition = localPos;
        var b = new MeshKit();
        // 길쭉한 깃털: 뾰족한 끝이 앞(+z)
        b.Prism(new[] { new Vector3(0f, 0f, 0.9f), new Vector3(0.16f, 0f, 0.2f), new Vector3(0.12f, 0f, -0.7f), new Vector3(0f, 0f, -0.8f), new Vector3(-0.12f, 0f, -0.7f), new Vector3(-0.16f, 0f, 0.2f) }, Vector3.up, 0.06f);
        g.AddComponent<MeshFilter>().sharedMesh = b.ToMesh("FeatherShot");
        g.AddComponent<MeshRenderer>().sharedMaterial = primaryMat;
        g.transform.localScale = Vector3.one * 1.3f;
        TrailRenderer tr = g.AddComponent<TrailRenderer>();
        tr.sharedMaterial = trailMat;
        tr.time = 0.15f;
        tr.startWidth = 0.18f;
        tr.endWidth = 0f;
        return g.transform;
    }

    // ── 경고 표시 (새가 있는 면 바로 뒤에 붉은 원/띠, 카메라 쪽을 향함) ──

    GameObject MakeCircleTelegraph(Vector3 center, float radius)
    {
        Transform root = new GameObject("EagleWarning").transform;
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
        // 가운데 십자 조준선
        var cross = new MeshKit();
        cross.Prism(new[] { new Vector3(-radius * 0.5f, -0.08f, 0f), new Vector3(radius * 0.5f, -0.08f, 0f), new Vector3(radius * 0.5f, 0.08f, 0f), new Vector3(-radius * 0.5f, 0.08f, 0f) }, Vector3.forward, 0.02f);
        cross.Prism(new[] { new Vector3(-0.08f, -radius * 0.5f, 0f), new Vector3(0.08f, -radius * 0.5f, 0f), new Vector3(0.08f, radius * 0.5f, 0f), new Vector3(-0.08f, radius * 0.5f, 0f) }, Vector3.forward, 0.02f);
        AddTelegraphMesh(root, cross.ToMesh("WarnCross"), false);
        telegraphs.Add(root.gameObject);
        PulseTelegraphs(0f);
        return root.gameObject;
    }

    GameObject MakeRectTelegraph(Vector3 center, Vector2 size)
    {
        Transform root = new GameObject("EagleWarning").transform;
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

    // progress 0 → 1: 공격이 다가올수록 빠르고 진하게 깜빡임
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

    // ── 컷씬 UI: 위아래 검은 띠 + 큰 제목 ──

    void BuildCinemaUI()
    {
        GameObject root = new GameObject("EagleCinema", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        cineCanvas = root.AddComponent<Canvas>();
        cineCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        cineCanvas.sortingOrder = 40;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1f;

        barTop = Bar("BarTop", new Vector2(0.5f, 1f));
        barBottom = Bar("BarBottom", new Vector2(0.5f, 0f));
        title = Text("Title", 84, new Vector2(0f, -150f));
        title.color = new Color(0.98f, 0.72f, 0.30f);
        subtitle = Text("Subtitle", 30, new Vector2(0f, -212f));
        subtitle.color = new Color(0.965f, 0.906f, 0.757f);
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

    // 제목: 크게 쾅 나타났다가 잠시 머물고 사라짐
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
            float a = appear * fade;
            title.alpha = a;
            subtitle.alpha = Mathf.Clamp01((t - 0.2f) / 0.2f) * fade;
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, Mathf.SmoothStep(0f, 1f, appear));
            yield return null;
        }
        title.text = subtitle.text = "";
    }

    // 기존 배경음을 줄였다가 새 곡으로 바꾸고 다시 키움
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
