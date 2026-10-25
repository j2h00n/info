using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// 🐍 스토리 모드 300m 보스: 풍선에 매달린 뱀
// 패턴: [한 줄(세로 | 또는 가로 ㅡ) → 십자(+) → 한 줄] → 체력 50% 깎는 아이템 (5초) → 반복
// 체력 0 → 3, 2, 1 카운트다운 → 플레이어 위치로 돌진 → 피하면 클리어
//
// 새는 계속 앞으로 날아가므로, 보스와 공격은 전부 새를 따라 움직이는 "아레나" 안에서 계산
// (아레나 로컬 좌표: x/y = 월드와 같음, z = 새로부터 앞쪽 거리)
public class BossFight : MonoBehaviour
{
    [Header("연결")]
    public BirdController bird;
    public NewMonoBehaviourScript obstacleSpawner;

    [Header("UI")]
    public GameObject bossHud;
    public RectTransform hpFill;
    public TextMeshProUGUI countdownText;

    [Header("머티리얼")]
    public Material snakeMat;   // 머리
    public Material skinMat;    // 몸통 (줄무늬 텍스처)
    public Material eyeMat;
    public Material pupilMat;
    public Material tongueMat;
    public Material stringMat;
    public Material sludgeMat;
    public Material telegraphMat;
    public Material pinMat;
    public Material trailMat;   // 돌진할 때 남는 잔상
    public Material poisonMat;  // 바닥 독바다
    public Material[] balloonMats;

    [Header("사운드")]
    public AudioClip bossBgm;   // 보스전 배경음 (시작하면 기존 BGM에서 바뀜, 클리어하면 돌아옴)

    [Header("패턴 설정")]
    public float bossDistance = 26f;   // 새 앞쪽 보스 위치
    public float bossHeight = -1.5f;   // 새 높이 기준 보스 머리 높이 (풍선이 체력바에 안 가리게 살짝 아래)
    public float bossScale = 1.6f;
    public float laneWidth = 5f;       // 오물 한 줄의 두께
    public float laneLength = 26f;     // 세로 줄의 길이 (새 높이 기준 위아래)
    public float arenaHalfWidth = 35f; // 가로 줄이 덮는 좌우 범위 (새가 갈 수 있는 끝까지)
    public float telegraphTime = 1.0f; // 붉은 경고선 보여주는 시간
    public float sludgeSpeed = 26f;    // 오물이 날아오는 속도
    public float itemLifetime = 5f;    // 아이템 유지 시간
    public float dashSpeed = 70f;      // 마지막 돌진 속도
    public float skyDiveHeight = 18f;  // 2차 돌진: 새 머리 위 얼마나 높이서 내리꽂는지
    public float skyDiveAhead = 16f;   // 2차 돌진: 새보다 얼마나 앞쪽 하늘에 떠 있는지

    [Header("2차 돌진 때 카메라 (새 기준 로컬)")]
    public Vector3 lookUpCameraPos = new Vector3(0f, 1f, -11f); // 뒤로 빠져서 새와 하늘의 보스가 같이 보이게
    public float lookUpCameraPitch = -22f;                       // 위로 올려다보는 각도
    public float lookUpFov = 75f;                                // 시야를 넓혀서 아래의 새와 위의 보스가 한 화면에
    public Vector2 lookUpCountdownPos = new Vector2(-380f, 0f);  // 이때 3,2,1 숫자는 왼쪽 옆으로 (위의 보스, 아래의 새를 가리지 않게)

    const int BalloonCount = 2;        // 풍선 1개 = 체력 50% (2대 맞으면 죽음)
    const float BodyLength = 11f;      // 몸통 경로 길이 (PathPos의 s 범위)
    const int BodyRings = 30;          // 몸통 메쉬: 길이 방향 고리 수
    const int BodySides = 16;          // 몸통 메쉬: 둘레 방향 칸 수
    const float StripeRepeat = 7f;     // 몸통 줄무늬 개수

    // 공격 한 줄: 세로(|)는 x 위치, 가로(ㅡ)는 y 위치를 지나감
    struct Lane
    {
        public bool horizontal;
        public Vector2 center;
        public Vector2 size;
    }

    private float hp = 1f;
    private float shownHp = 1f;
    private bool running;
    private bool aiming;
    private bool dashing;
    private float bossX, bossY, bossTargetX;
    private float coil;      // 0 → 1: 카운트다운 동안 몸을 점점 세게 꼬며 힘을 모음
    private float dashPose;  // 0 → 1: 돌진할 때 몸이 창처럼 뒤로 쭉 펴짐
    private TrailRenderer trail;
    private Vector3 headBaseScale;

    // 카메라: 0 = 원래 자리, 1 = 위로 올려다보는 자리. 흔들림은 그 위에 더해짐
    private Transform cam;
    private Camera camComp;
    private Vector3 camBasePos;
    private Quaternion camBaseRot;
    private float camBaseFov;
    private float camLook;
    private float shakeTime, shakeDuration, shakeStrength;

    // 독바다: 새가 더 못 내려가는 바닥 바로 밑에서 출렁임
    // 이전(240 x 176)의 약 3배 면적: 가로·세로 각각 √3배 → 416 x 305
    // 칸은 새 근처는 촘촘하고 멀수록 크게 (꼭짓점 수를 아끼면서 가까운 물결은 자연스럽게)
    const int SeaCellsX = 56, SeaCellsZ = 52;
    const float SeaHalfWidth = 208f, SeaNearZ = -16f, SeaFarZ = 289f;
    private Transform sea;
    private Mesh seaMesh;
    private Vector3[] seaVerts;
    private float seaRise;                   // 0 → 1: 보스 등장과 함께 아래에서 차오름
    private readonly List<Transform> bubbles = new List<Transform>();
    private float[] bubbleAge, bubbleLife;

    private Transform arena, boss, head, tongue;
    private Mesh bodyMesh;
    private Vector3[] bodyVerts, bodyNormals;
    private readonly List<Transform> balloons = new List<Transform>();
    private readonly List<LineRenderer> strings = new List<LineRenderer>();
    private readonly List<Transform> sludge = new List<Transform>();
    private readonly List<GameObject> telegraphs = new List<GameObject>();
    private readonly List<Renderer> telegraphFills = new List<Renderer>();
    private readonly List<Renderer> telegraphEdges = new List<Renderer>();
    private readonly List<Renderer> bossRenderers = new List<Renderer>();
    private SphereCollider headHitbox;

    private bool Dead { get { return bird == null || bird.IsGameOver; } }
    private Vector3 BirdLocal { get { return new Vector3(bird.transform.position.x, bird.transform.position.y, 0f); } }

    // BirdController가 스토리 모드 300m에서 호출
    public void Begin()
    {
        if (running) return;
        running = true;

        arena = new GameObject("BossArena").transform;
        arena.position = new Vector3(0f, 0f, bird.transform.position.z);
        BuildBoss();
        BuildSea();
        if (Camera.main != null)
        {
            camComp = Camera.main;
            cam = camComp.transform;
            camBasePos = cam.localPosition;
            camBaseRot = cam.localRotation;
            camBaseFov = camComp.fieldOfView;
        }
        if (bossBgm != null) StartCoroutine(SwapMusic(bossBgm));

        hp = shownHp = 1f;
        if (bossHud != null) bossHud.SetActive(true);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (obstacleSpawner != null) obstacleSpawner.StopAndScatter(); // 바위들 양옆으로 날려버리기

        StartCoroutine(FightRoutine());
    }

    // ───────────────────────── 전투 흐름 ─────────────────────────

    IEnumerator FightRoutine()
    {
        yield return Entry();

        while (hp > 0.001f)
        {
            yield return SludgeWave(false); if (Dead) yield break; // 한 줄
            yield return SludgeWave(true);  if (Dead) yield break; // 십자
            yield return SludgeWave(false); if (Dead) yield break; // 한 줄
            yield return ItemPhase();       if (Dead) yield break;
            yield return new WaitForSeconds(0.8f);
        }

        yield return FinalDash();
    }

    // 하늘 위에서 내려오며 등장
    IEnumerator Entry()
    {
        float startY = BirdLocal.y + 20f;
        bossX = 0f; bossY = startY;
        Vector3 from = new Vector3(0f, startY, bossDistance + 30f);
        float t = 0f;
        while (t < 2.2f)
        {
            t += Time.deltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 2.2f), 3f); // 감속
            Vector3 to = new Vector3(0f, BirdLocal.y + bossHeight, bossDistance);
            boss.localPosition = Vector3.Lerp(from, to, e);
            bossY = boss.localPosition.y;
            yield return null;
        }
    }

    // 오물 공격: 붉은 경고선 → 독 오물 발사
    // cross = false: 내 위치를 지나는 한 줄 (세로 | 또는 가로 ㅡ 랜덤)
    // cross = true : 내 위치를 중심으로 십자(+) → 대각선으로 빠져나가야 함
    IEnumerator SludgeWave(bool cross)
    {
        Vector3 me = BirdLocal;
        List<Lane> lanes = new List<Lane>();
        bool vertical = cross || Random.value < 0.5f;
        bool horizontal = cross || !vertical;
        if (vertical) lanes.Add(VerticalLane(Mathf.Clamp(me.x, -30f, 30f), me.y));
        if (horizontal) lanes.Add(HorizontalLane(me.y));

        // 1. 경고선 (붉은 기둥 + 테두리, 발사 직전일수록 빠르게 깜빡임)
        foreach (Lane lane in lanes) telegraphs.Add(MakeTelegraph(lane.center, lane.size));

        aiming = true;
        bossTargetX = me.x; // 보스가 내 쪽으로 고개를 돌림

        float t = 0f;
        while (t < telegraphTime)
        {
            if (Dead) { ClearTelegraphs(); yield break; }
            PulseTelegraphs(t / telegraphTime);
            t += Time.deltaTime;
            yield return null;
        }
        ClearTelegraphs();
        aiming = false;

        // 2. 퉤! 오물 발사
        StartCoroutine(Puff(head, 1.2f));
        foreach (Lane lane in lanes) SpawnSludgeLine(lane);

        // 오물이 새를 지나갈 때까지 기다림
        yield return new WaitForSeconds(bossDistance / sludgeSpeed + 0.3f);
    }

    Lane VerticalLane(float x, float centerY)
    {
        return new Lane { horizontal = false, center = new Vector2(x, centerY), size = new Vector2(laneWidth, laneLength) };
    }

    Lane HorizontalLane(float y)
    {
        return new Lane { horizontal = true, center = new Vector2(0f, y), size = new Vector2(arenaHalfWidth * 2f, laneWidth) };
    }

    // 풍선 하나(체력 50%) 터뜨리는 아이템: 내 앞쪽 살짝 옆에 생김, 5초 안에 먹어야 함
    IEnumerator ItemPhase()
    {
        Vector3 me = BirdLocal;
        float side = me.x > 20f ? -1f : me.x < -20f ? 1f : (Random.value < 0.5f ? -1f : 1f);
        float ix = Mathf.Clamp(me.x + side * Random.Range(3f, 7f), -30f, 30f);
        float iy = Mathf.Max(bird.fallLimitY + 1.5f, me.y + Random.Range(-2.5f, 2.5f));

        bool collected = false;
        Transform pin = MakePin(new Vector3(ix, iy, 3f), () => collected = true);
        Renderer[] pinRenderers = pin.GetComponentsInChildren<Renderer>();

        float t = 0f;
        while (t < itemLifetime && !collected && !Dead)
        {
            t += Time.deltaTime;
            float appear = Mathf.Clamp01(t / 0.25f);
            pin.localScale = Vector3.one * 1.6f * appear;
            pin.localPosition = new Vector3(ix, iy + Mathf.Sin(t * 4f) * 0.25f, 3f);
            pin.localRotation = Quaternion.Euler(0f, 0f, t * 180f);
            // 마지막 1.5초는 깜빡이며 사라질 것을 알려줌
            bool visible = t < itemLifetime - 1.5f || Mathf.Repeat(t * 8f, 1f) < 0.6f;
            foreach (Renderer r in pinRenderers) r.enabled = visible;
            yield return null;
        }

        if (!collected || Dead)
        {
            Destroy(pin.gameObject);
            yield break;
        }

        // 먹으면 핀이 보스 풍선으로 날아가서 펑!
        Destroy(pin.GetComponentInChildren<Collider>());
        foreach (Renderer r in pinRenderers) r.enabled = true;
        Transform balloon = balloons.Count > 0 ? balloons[0] : head;
        Vector3 start = pin.localPosition;
        t = 0f;
        while (t < 0.3f)
        {
            t += Time.deltaTime;
            Vector3 target = arena.InverseTransformPoint(balloon.position);
            pin.localPosition = Vector3.Lerp(start, target, t / 0.3f);
            pin.LookAt(balloon.position);
            pin.Rotate(90f, 0f, 0f);
            yield return null;
        }
        Destroy(pin.gameObject);

        yield return PopBalloon();
    }

    // 체력 0: 3, 2, 1 → 내 위치로 돌진 → (시점이 위로) 3, 2, 1 → 하늘에서 내리꽂기 → 피하면 클리어
    IEnumerator FinalDash()
    {
        // ── 1차: 정면 돌진 ──
        yield return Countdown(() => new Vector3(bossX, bossY, bossDistance));
        if (Dead) yield break;

        // 돌진할 위치 고정 + 경고선
        Vector3 target = BirdLocal;
        telegraphs.Add(MakeTelegraph(target, new Vector2(4.5f, 4.5f)));
        aiming = true;
        bossTargetX = target.x;
        // 경고선 동안 목표 정면으로 이동하며 몸을 뒤로 젖힘 (그래야 일직선으로 정확히 그 자리를 지나감)
        yield return Windup(new Vector3(target.x, target.y, bossDistance + 3f), Quaternion.identity);
        ClearTelegraphs();

        Vector3 end = new Vector3(target.x, target.y, -25f);
        bool hit = false;
        yield return Charge(end, () => boss.localPosition.z < 2f, r => hit = r);
        if (hit || Dead) yield break;

        // ── 2차: 시점을 위로 돌리고, 하늘에서 내리꽂기 ──
        dashing = false;
        if (trail != null) trail.emitting = false;
        dashPose = 0f;
        head.localScale = headBaseScale;

        StartCoroutine(BlendCamera(1f, 0.8f)); // 카메라가 뒤로 빠지며 위를 올려다봄

        // 새 머리 위 하늘에서 내려와 자리 잡기 (머리는 새를 내려다봄)
        Vector3 me = BirdLocal;
        float skyY = me.y + skyDiveHeight;
        Vector3 from = new Vector3(me.x, skyY + 25f, skyDiveAhead);
        float t = 0f;
        while (t < 0.9f)
        {
            t += Time.deltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.9f), 3f);
            me = BirdLocal;
            Vector3 hover = new Vector3(me.x, me.y + skyDiveHeight, skyDiveAhead);
            boss.localPosition = Vector3.Lerp(from, hover, e);
            boss.localRotation = FaceToward(me - boss.localPosition);
            yield return null;
        }

        // 위에서 새를 따라다니며 3, 2, 1
        yield return Countdown(() =>
        {
            Vector3 b = BirdLocal;
            Vector3 hover = new Vector3(Mathf.Lerp(boss.localPosition.x, b.x, Time.deltaTime * 4f), b.y + skyDiveHeight, skyDiveAhead);
            boss.localRotation = FaceToward(b - hover);
            return hover;
        }, lookUpCountdownPos);
        if (Dead) yield break;

        // 내리꽂을 위치 고정 + 경고선 (보스 → 새 → 독바다까지 이어지는 빨간 기둥)
        target = BirdLocal;
        Vector3 start = boss.localPosition;
        Vector3 dir = (target - start).normalized;
        float toSea = (start.y - (SeaLevelNow - 1f)) / Mathf.Max(0.01f, -dir.y); // 바다 아래까지 가는 거리
        end = start + dir * toSea;
        telegraphs.Add(MakeBeamTelegraph(start, end, 4.5f));
        // 경고선 동안 살짝 더 높이 몸을 젖힘
        yield return Windup(start - dir * 3f, FaceToward(dir));
        ClearTelegraphs();
        if (Dead) yield break;

        StartCoroutine(BlendCamera(0f, 0.5f)); // 내리꽂는 순간 카메라도 원래대로 내려옴
        hit = false;
        yield return Charge(end, () => boss.localPosition.y < target.y + 1f, r => hit = r);
        if (hit || Dead) yield break;

        // 독바다에 첨벙! → 퇴장 → 클리어 (음악도 스테이지 BGM으로)
        // 실제로 떨어진 곳은 카메라 뒤쪽이라, 물보라는 새 바로 앞에 보이게
        StartCoroutine(Splash(new Vector3(boss.localPosition.x, SeaLevelNow, Mathf.Max(boss.localPosition.z, 6f))));
        if (bossHud != null) bossHud.SetActive(false);
        Destroy(boss.gameObject);
        if (bossBgm != null) StartCoroutine(SwapMusic(bird.StoryMusic));
        yield return new WaitForSeconds(0.8f);
        if (!Dead) bird.StageClear();
    }

    // 3, 2, 1: 숫자가 커졌다 작아지고, 갈수록 몸을 세게 꼬며 부들부들. hover = 매 프레임 보스가 있을 자리
    // textPos: 숫자의 화면 위치 (null이면 원래 자리)
    IEnumerator Countdown(System.Func<Vector3> hover, Vector2? textPos = null)
    {
        RectTransform textRt = countdownText.rectTransform;
        Vector2 basePos = textRt.anchoredPosition;
        if (textPos.HasValue) textRt.anchoredPosition = textPos.Value;
        coil = 0f;
        for (int n = 3; n >= 1; n--)
        {
            if (Dead) yield break;
            countdownText.gameObject.SetActive(true);
            countdownText.text = n.ToString();
            float t = 0f;
            while (t < 0.8f)
            {
                t += Time.deltaTime;
                countdownText.transform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, Mathf.Clamp01(t / 0.25f));
                coil = Mathf.Clamp01((3 - n + t / 0.8f) / 3f);
                boss.localPosition = hover() + Random.insideUnitSphere * (0.15f + 0.35f * coil);
                yield return null;
            }
        }
        countdownText.gameObject.SetActive(false);
        textRt.anchoredPosition = basePos;
    }

    // 경고선이 깜빡이는 동안 돌진 시작 자리로 이동하며 몸을 젖힘
    IEnumerator Windup(Vector3 to, Quaternion rotation)
    {
        Vector3 from = boss.localPosition;
        Quaternion rotFrom = boss.localRotation;
        float w = 0f;
        while (w < 0.45f)
        {
            PulseTelegraphs(w / 0.45f);
            w += Time.deltaTime;
            float e = Mathf.SmoothStep(0f, 1f, w / 0.45f);
            boss.localPosition = Vector3.Lerp(from, to, e);
            boss.localRotation = Quaternion.Slerp(rotFrom, rotation, e);
            yield return null;
        }
        boss.localPosition = to;
        boss.localRotation = rotation;
    }

    // 일직선 돌진. 몸이 창처럼 펴지고 잔상이 남음. shakeWhen이 참이 되는 순간 화면 흔들림
    IEnumerator Charge(Vector3 end, System.Func<bool> shakeWhen, System.Action<bool> result)
    {
        dashing = true;
        headHitbox.enabled = true;
        if (tongue != null) tongue.gameObject.SetActive(true);
        if (trail != null) { trail.Clear(); trail.emitting = true; }
        // 머리 판정 반지름(월드) + 새 몸 절반 (머리가 늘어나기 전 크기 기준)
        float hitRadius = headHitbox.radius * Mathf.Max(headBaseScale.x, headBaseScale.y, headBaseScale.z) * bossScale + 0.5f;
        bool shook = false;
        while ((end - boss.localPosition).sqrMagnitude > 0.0001f)
        {
            if (Dead) { result(true); yield break; } // 맞으면 보스는 그 자리에서 멈춤
            dashPose = Mathf.MoveTowards(dashPose, 1f, Time.deltaTime * 8f);
            head.localScale = Vector3.Scale(headBaseScale, new Vector3(Mathf.Lerp(1f, 0.85f, dashPose), Mathf.Lerp(1f, 0.85f, dashPose), Mathf.Lerp(1f, 1.35f, dashPose)));

            Vector3 prev = boss.localPosition;
            boss.localPosition = Vector3.MoveTowards(prev, end, dashSpeed * Time.deltaTime);

            if (!shook && shakeWhen()) { shook = true; Shake(0.35f, 0.45f); }

            // 돌진이 빨라서 프레임이 끊기면 판정을 건너뛸 수 있음 → 이번 프레임에 지나간 선 전체로 검사
            if (DistanceToSegment(BirdLocal, prev, boss.localPosition) < hitRadius)
            {
                bird.GameOver();
                result(true);
                yield break;
            }
            yield return null;
        }
        headHitbox.enabled = false;
        result(false);
    }

    // 머리(-z)가 dir 방향을 보게 하는 회전 (몸통은 반대쪽으로 뻗음)
    static Quaternion FaceToward(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return Quaternion.identity;
        return Quaternion.LookRotation(-dir.normalized, Vector3.up);
    }

    // 보스가 독바다에 떨어질 때 튀는 독 물방울 (판정 없음)
    IEnumerator Splash(Vector3 at)
    {
        var drops = new List<Transform>();
        var vel = new List<Vector3>();
        for (int i = 0; i < 14; i++)
        {
            drops.Add(Prim(PrimitiveType.Sphere, "SplashDrop", arena, sludgeMat, at, Vector3.one * Random.Range(0.8f, 1.6f)));
            Vector2 flat = Random.insideUnitCircle.normalized * Random.Range(3f, 8f);
            vel.Add(new Vector3(flat.x, Random.Range(8f, 16f), flat.y));
        }
        Shake(0.4f, 0.5f);
        float t = 0f;
        while (t < 1.2f)
        {
            t += Time.deltaTime;
            for (int i = 0; i < drops.Count; i++)
            {
                vel[i] += Physics.gravity * Time.deltaTime;
                drops[i].localPosition += vel[i] * Time.deltaTime;
                drops[i].localScale = Vector3.one * Mathf.Lerp(1.3f, 0.2f, t / 1.2f);
            }
            yield return null;
        }
        foreach (Transform d in drops) Destroy(d.gameObject);
    }

    // ───────────────────────── 매 프레임 ─────────────────────────

    void LateUpdate()
    {
        if (!running || arena == null) return;

        // 아레나가 새를 따라 앞으로 이동
        arena.position = new Vector3(0f, 0f, bird.transform.position.z);

        // 오물 이동 (새 쪽으로)
        for (int i = sludge.Count - 1; i >= 0; i--)
        {
            Transform s = sludge[i];
            if (s == null) { sludge.RemoveAt(i); continue; }
            Vector3 prev = s.localPosition;
            s.localPosition += Vector3.back * sludgeSpeed * Time.deltaTime;

            // 프레임이 끊겨도 통과하지 않도록 이번 프레임 이동 경로 전체로 판정 (반지름은 SphereCollider와 동일)
            float radius = 0.42f * Mathf.Max(s.localScale.x, s.localScale.y, s.localScale.z) + 0.5f;
            if (!Dead && DistanceToSegment(BirdLocal, prev, s.localPosition) < radius) bird.GameOver();

            s.Rotate(120f * Time.deltaTime, 80f * Time.deltaTime, 0f);
            if (s.localPosition.z < -15f) { Destroy(s.gameObject); sludge.RemoveAt(i); }
        }

        // 체력바 부드럽게 줄어들기
        shownHp = Mathf.MoveTowards(shownHp, hp, Time.deltaTime * 1.2f);
        if (hpFill != null)
        {
            hpFill.anchorMax = new Vector2(shownHp, 1f);
            hpFill.gameObject.SetActive(shownHp > 0.01f);
        }

        AnimateSea();
        UpdateCamera();

        // 독바다에 닿으면 죽음 (다 차오른 뒤부터). 바닥 높이 = 독바다 수면
        if (!Dead && seaRise >= 1f)
        {
            Vector3 b = bird.transform.position;
            float surface = sea.localPosition.y + SeaHeight(b.x, b.z, Time.time);
            if (b.y <= bird.fallLimitY + 0.05f || b.y - 0.45f <= surface) bird.GameOver();
        }

        if (boss == null) return;
        AnimateBoss();
    }

    // ───────────────────────── 독바다 ─────────────────────────

    float SeaLevel { get { return bird.fallLimitY - 0.6f; } } // 물결 꼭대기가 바닥 높이까지 → 바닥에 닿으면 독에 빠짐
    float SeaLevelNow { get { return sea != null ? sea.localPosition.y : SeaLevel; } }

    // 물결 높이: 월드 z 기준으로 계산 → 새가 앞으로 날아갈 때 바다가 뒤로 흘러가는 느낌
    float SeaHeight(float x, float worldZ, float t)
    {
        return 0.35f * Mathf.Sin(x * 0.35f + t * 1.7f)
             + 0.30f * Mathf.Sin(worldZ * 0.28f - t * 2.3f)
             + 0.15f * Mathf.Sin((x + worldZ) * 0.6f + t * 3.1f);
    }

    void BuildSea()
    {
        sea = new GameObject("PoisonSea").transform;
        sea.SetParent(arena, false);
        seaMesh = new Mesh { name = "PoisonSea" };
        seaMesh.MarkDynamic();

        // 로우폴리 느낌: 삼각형마다 꼭짓점을 따로 둬서 면이 각지게 보이도록
        seaVerts = new Vector3[SeaCellsX * SeaCellsZ * 6];
        int[] tris = new int[seaVerts.Length];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;
        seaMesh.vertices = seaVerts;
        seaMesh.triangles = tris;

        sea.gameObject.AddComponent<MeshFilter>().sharedMesh = seaMesh;
        MeshRenderer mr = sea.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = poisonMat != null ? poisonMat : sludgeMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 보글보글 올라오는 독 거품
        bubbleAge = new float[14];
        bubbleLife = new float[14];
        for (int i = 0; i < bubbleAge.Length; i++)
        {
            Transform b = Prim(PrimitiveType.Sphere, "Bubble", sea, sludgeMat, Vector3.zero, Vector3.zero);
            bubbles.Add(b);
            bubbleLife[i] = Random.Range(0.9f, 1.6f);
            bubbleAge[i] = -Random.Range(0f, 2f); // 처음엔 제각각 늦게 시작
            PlaceBubble(i);
        }

        seaRise = 0f;
        AnimateSea();
    }

    // 가운데(새 근처)일수록 촘촘한 격자 좌표
    static float SeaGridX(int i)
    {
        float u = i / (float)SeaCellsX * 2f - 1f; // -1 ~ 1
        return Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), 1.6f) * SeaHalfWidth;
    }

    static float SeaGridZ(int i)
    {
        float u = i / (float)SeaCellsZ;           // 0 ~ 1 (가까운 쪽 → 먼 쪽)
        return SeaNearZ + Mathf.Pow(u, 1.7f) * (SeaFarZ - SeaNearZ);
    }

    void PlaceBubble(int i)
    {
        bubbles[i].localPosition = new Vector3(Random.Range(-35f, 35f), 0f, Random.Range(2f, 45f));
    }

    void AnimateSea()
    {
        if (seaMesh == null) return;
        float t = Time.time;

        // 등장할 때 15만큼 아래에서 차오름
        seaRise = Mathf.MoveTowards(seaRise, 1f, Time.deltaTime / 2.5f);
        float rise = 1f - Mathf.Pow(1f - seaRise, 3f);
        sea.localPosition = new Vector3(0f, SeaLevel - 15f * (1f - rise), 0f);

        float arenaZ = arena.position.z;
        int v = 0;
        for (int iz = 0; iz < SeaCellsZ; iz++)
        {
            for (int ix = 0; ix < SeaCellsX; ix++)
            {
                float x0 = SeaGridX(ix), x1 = SeaGridX(ix + 1);
                float z0 = SeaGridZ(iz), z1 = SeaGridZ(iz + 1);
                Vector3 a = new Vector3(x0, SeaHeight(x0, z0 + arenaZ, t), z0);
                Vector3 b = new Vector3(x1, SeaHeight(x1, z0 + arenaZ, t), z0);
                Vector3 c = new Vector3(x0, SeaHeight(x0, z1 + arenaZ, t), z1);
                Vector3 d = new Vector3(x1, SeaHeight(x1, z1 + arenaZ, t), z1);
                // 위를 보는 면: 삼각형 (p0, p1, p2)의 앞면은 Cross(p1-p0, p2-p0) 방향
                seaVerts[v++] = a; seaVerts[v++] = c; seaVerts[v++] = b;
                seaVerts[v++] = b; seaVerts[v++] = c; seaVerts[v++] = d;
            }
        }
        seaMesh.vertices = seaVerts;
        seaMesh.RecalculateNormals(); // 꼭짓점이 공유되지 않아 면마다 평평한 법선 → 각진 로우폴리
        seaMesh.RecalculateBounds();

        // 거품: 수면에서 올라오며 커지다가 펑
        for (int i = 0; i < bubbles.Count; i++)
        {
            bubbleAge[i] += Time.deltaTime;
            float p = bubbleAge[i] / bubbleLife[i];
            Transform bub = bubbles[i];
            if (p < 0f) { bub.localScale = Vector3.zero; continue; }
            if (p >= 1f)
            {
                bubbleAge[i] = -Random.Range(0.2f, 1.2f);
                bubbleLife[i] = Random.Range(0.9f, 1.6f);
                PlaceBubble(i);
                bub.localScale = Vector3.zero;
                continue;
            }
            Vector3 lp = bub.localPosition;
            float surface = SeaHeight(lp.x, lp.z + arenaZ, t);
            float size = p < 0.85f ? Mathf.Lerp(0.3f, 1.1f, p / 0.85f) : Mathf.Lerp(1.1f, 1.6f, (p - 0.85f) / 0.15f);
            bub.localPosition = new Vector3(lp.x, surface + Mathf.Lerp(-0.3f, 0.5f, p), lp.z);
            bub.localScale = Vector3.one * size;
        }
    }

    // ───────────────────────── 음악 ─────────────────────────

    // 기존 배경음을 줄였다가 새 곡으로 바꾸고 다시 키움 (볼륨은 메뉴 슬라이더 값 그대로)
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

    void AnimateBoss()
    {
        float time = Time.time;

        // 좌우로 흔들흔들 떠다니기 (공격할 땐 내 쪽을 조준)
        if (!dashing)
        {
            if (!aiming) bossTargetX = Mathf.Sin(time * 0.6f) * 9f;
            bossX = Mathf.Lerp(bossX, bossTargetX, Time.deltaTime * 3f);
            bossY = Mathf.Lerp(bossY, BirdLocal.y + bossHeight + Mathf.Sin(time * 1.3f) * 1.2f, Time.deltaTime * 1.5f);
            if (hp > 0.001f && boss.localPosition.z <= bossDistance + 0.5f)
                boss.localPosition = new Vector3(bossX, bossY, bossDistance);
        }

        // 몸통이 S자로 매끈하게 꿈틀꿈틀
        UpdateBodyMesh(time);

        // 혀 날름날름
        if (tongue != null) tongue.gameObject.SetActive(dashing || Mathf.Repeat(time * 1.5f, 1f) < 0.25f);

        // 풍선 둥실둥실 + 줄을 꼬리 끝에 연결
        Vector3 knot = boss.TransformPoint(PathPos(BodyLength, time));
        for (int i = 0; i < balloons.Count; i++)
        {
            Transform b = balloons[i];
            float bx = (i - (balloons.Count - 1) * 0.5f) * 2.2f;
            b.localPosition = new Vector3(bx + Mathf.Sin(time * 1.1f + i) * 0.3f, 9f + Mathf.Sin(time * 1.7f + i * 1.3f) * 0.4f, 0.6f + (i % 2) * 0.5f);
            strings[i].SetPosition(0, b.position - Vector3.up * b.lossyScale.y * 0.5f);
            strings[i].SetPosition(1, knot);
        }
    }

    // 몸통 중심선: s = 0(머리 속) ~ BodyLength(꼬리 끝, 풍선 매듭)
    Vector3 PathPos(float s, float time)
    {
        float k = s / BodyLength;
        // 평소: 위로 매달려 S자로 꿈틀 / 힘 모을 때(coil): 더 빠르고 크게 꼬면서 몸을 움츠림
        float amp = Mathf.Lerp(1.2f, 2.2f, coil);
        float speed = Mathf.Lerp(3f, 11f, coil);
        float rise = Mathf.Lerp(0.6f, 0.45f, coil);
        Vector3 hang = new Vector3(Mathf.Sin(s * 0.75f - time * speed) * amp * k, 0.2f + s * rise, 0.4f + k * 0.8f);
        // 돌진: 몸이 머리 뒤로 창처럼 쭉 펴지고 꼬리만 빠르게 파르르
        Vector3 spear = new Vector3(Mathf.Sin(s * 1.4f - time * 25f) * 0.35f * k, 0.15f + s * 0.08f, 0.4f + s * 0.95f);
        return Vector3.Lerp(hang, spear, dashPose);
    }

    // 화면 흔들림 시작 (UpdateCamera에서 점점 약해지며 적용)
    void Shake(float duration, float strength)
    {
        shakeDuration = duration;
        shakeTime = duration;
        shakeStrength = strength;
    }

    // 카메라를 원래 자리(0) ↔ 위를 올려다보는 자리(1)로 부드럽게 전환
    IEnumerator BlendCamera(float to, float duration)
    {
        float from = camLook, t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            camLook = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        camLook = to;
    }

    void UpdateCamera()
    {
        if (cam == null) return;
        Vector3 pos = Vector3.Lerp(camBasePos, lookUpCameraPos, camLook);
        Quaternion rot = Quaternion.Slerp(camBaseRot, Quaternion.Euler(lookUpCameraPitch, 0f, 0f), camLook);
        if (shakeTime > 0f)
        {
            shakeTime -= Time.deltaTime;
            pos += Random.insideUnitSphere * shakeStrength * Mathf.Clamp01(shakeTime / shakeDuration);
        }
        cam.localPosition = pos;
        cam.localRotation = rot;
        camComp.fieldOfView = Mathf.Lerp(camBaseFov, lookUpFov, camLook);
    }

    // 몸통 굵기: 머리 쪽 굵고 꼬리로 갈수록 가늘게
    float BodyRadius(float k)
    {
        return Mathf.Lerp(0.95f, 0.08f, Mathf.Pow(k, 1.2f));
    }

    void UpdateBodyMesh(float time)
    {
        if (bodyMesh == null) return;
        for (int r = 0; r < BodyRings; r++)
        {
            float k = r / (float)(BodyRings - 1);
            float s = k * BodyLength;
            Vector3 p = PathPos(s, time);
            Vector3 tangent = (PathPos(s + 0.05f, time) - PathPos(s - 0.05f, time)).normalized;
            // 기준축은 x: 매달린 자세(위 방향)·돌진 자세(앞 방향) 어느 쪽과도 평행해지지 않아 이음새가 안 꼬임
            Vector3 n = Vector3.Cross(Vector3.right, tangent).normalized;
            Vector3 b = Vector3.Cross(tangent, n);
            float radius = BodyRadius(k);
            for (int j = 0; j <= BodySides; j++)
            {
                float a = j / (float)BodySides * Mathf.PI * 2f;
                Vector3 dir = n * Mathf.Cos(a) + b * Mathf.Sin(a);
                int v = r * (BodySides + 1) + j;
                bodyVerts[v] = p + dir * radius;
                bodyNormals[v] = dir;
            }
        }
        bodyMesh.vertices = bodyVerts;
        bodyMesh.normals = bodyNormals;
        bodyMesh.RecalculateBounds();
    }

    // ───────────────────────── 만들기 ─────────────────────────

    void BuildBoss()
    {
        boss = new GameObject("SnakeBoss").transform;
        boss.SetParent(arena, false);
        boss.localScale = Vector3.one * bossScale;

        // 머리 (플레이어 쪽 = -z를 바라봄)
        head = Prim(PrimitiveType.Sphere, "Head", boss, snakeMat, Vector3.zero, new Vector3(2.4f, 1.9f, 2.6f));
        headHitbox = head.gameObject.AddComponent<SphereCollider>();
        headHitbox.isTrigger = true;
        headHitbox.radius = 0.4f;
        headHitbox.enabled = false;
        head.gameObject.AddComponent<BossTrigger>().onPlayerEnter = () => { if (dashing) bird.GameOver(); };
        headBaseScale = head.localScale;

        // 돌진 잔상 (평소엔 꺼져 있음)
        trail = head.gameObject.AddComponent<TrailRenderer>();
        trail.sharedMaterial = trailMat != null ? trailMat : telegraphMat;
        trail.time = 0.3f;
        trail.startWidth = 3.2f;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.3f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.emitting = false;

        // 노란 눈 + 세로로 찢어진 동공
        for (int s = -1; s <= 1; s += 2)
        {
            Prim(PrimitiveType.Sphere, "Eye", head, eyeMat, new Vector3(s * 0.24f, 0.22f, -0.36f), new Vector3(0.26f, 0.3f, 0.2f));
            Prim(PrimitiveType.Sphere, "Pupil", head, pupilMat, new Vector3(s * 0.25f, 0.22f, -0.45f), new Vector3(0.07f, 0.22f, 0.05f));
        }
        tongue = Prim(PrimitiveType.Cube, "Tongue", boss, tongueMat, new Vector3(0f, -0.35f, -1.75f), new Vector3(0.12f, 0.05f, 0.9f));

        BuildBodyMesh();

        // 풍선 + 줄
        for (int i = 0; i < BalloonCount; i++)
        {
            Material m = balloonMats != null && balloonMats.Length > 0 ? balloonMats[i % balloonMats.Length] : snakeMat;
            balloons.Add(Prim(PrimitiveType.Sphere, "Balloon" + i, boss, m, Vector3.up * 9f, new Vector3(1.7f, 2f, 1.7f)));

            LineRenderer line = new GameObject("String" + i).AddComponent<LineRenderer>();
            line.transform.SetParent(boss, false);
            line.material = stringMat;
            line.startWidth = line.endWidth = 0.06f;
            line.positionCount = 2;
            line.useWorldSpace = true;
            strings.Add(line);
        }

        foreach (Renderer r in boss.GetComponentsInChildren<Renderer>()) bossRenderers.Add(r);
        boss.localPosition = new Vector3(0f, 40f, bossDistance + 30f);
    }

    // 동그라미를 이어 붙이는 대신, 중심선을 따라 매끈한 관(튜브) 메쉬 하나로 몸통을 만듦
    void BuildBodyMesh()
    {
        GameObject body = new GameObject("Body");
        body.transform.SetParent(boss, false);
        bodyMesh = new Mesh { name = "SnakeBody" };
        bodyMesh.MarkDynamic();

        int ringVerts = BodySides + 1; // 이음새용으로 한 칸 더 (UV가 0→1로 감김)
        bodyVerts = new Vector3[BodyRings * ringVerts];
        bodyNormals = new Vector3[bodyVerts.Length];
        Vector2[] uvs = new Vector2[bodyVerts.Length];
        int[] tris = new int[(BodyRings - 1) * BodySides * 6];

        int ti = 0;
        for (int r = 0; r < BodyRings; r++)
        {
            float k = r / (float)(BodyRings - 1);
            for (int j = 0; j <= BodySides; j++)
                uvs[r * ringVerts + j] = new Vector2(j / (float)BodySides, k * StripeRepeat);

            if (r == BodyRings - 1) continue;
            for (int j = 0; j < BodySides; j++)
            {
                int a = r * ringVerts + j, b = a + 1, c = a + ringVerts, d = c + 1;
                // 바깥쪽을 보는 면 (Unity는 Cross(b-a, c-a) 방향이 앞면)
                tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
            }
        }

        bodyMesh.vertices = bodyVerts;
        bodyMesh.normals = bodyNormals;
        bodyMesh.uv = uvs;
        bodyMesh.triangles = tris;
        body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
        body.AddComponent<MeshRenderer>().sharedMaterial = skinMat != null ? skinMat : snakeMat;
        UpdateBodyMesh(0f);
    }

    // 경고선: 반투명 붉은 기둥 + 진한 테두리 (긴 모서리 4개 + 앞면 사각 틀)
    // 새 바로 앞부터 보스 앞까지 (새 뒤에서 시작하면 카메라가 기둥 안에 들어가 화면 전체가 빨개짐)
    GameObject MakeTelegraph(Vector2 center, Vector2 size)
    {
        Transform root = new GameObject("AttackLine").transform;
        root.SetParent(arena, false);
        root.localPosition = new Vector3(center.x, center.y, 0f);
        root.localScale = Vector3.one;
        BuildTelegraphBox(root, size, 1.5f, bossDistance - 2f);
        return root.gameObject;
    }

    // 비스듬한 경고선 (하늘에서 내리꽂기): from에서 to까지 이어지는 빨간 기둥
    GameObject MakeBeamTelegraph(Vector3 from, Vector3 to, float width)
    {
        Transform root = new GameObject("AttackLine").transform;
        root.SetParent(arena, false);
        root.localPosition = from;
        root.localRotation = Quaternion.LookRotation(to - from, Vector3.up);
        root.localScale = Vector3.one;
        BuildTelegraphBox(root, new Vector2(width, width), 0f, Vector3.Distance(from, to));
        return root.gameObject;
    }

    // root의 로컬 z축(zStart ~ zEnd)을 따라 채움 + 테두리 상자를 만듦
    void BuildTelegraphBox(Transform root, Vector2 size, float zStart, float zEnd)
    {
        float len = zEnd - zStart, zMid = (zStart + zEnd) * 0.5f;
        telegraphFills.Add(Prim(PrimitiveType.Cube, "Fill", root, telegraphMat, new Vector3(0f, 0f, zMid), new Vector3(size.x, size.y, len)).GetComponent<Renderer>());

        const float e = 0.25f; // 테두리 굵기
        float hx = size.x * 0.5f, hy = size.y * 0.5f;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                telegraphEdges.Add(Prim(PrimitiveType.Cube, "Edge", root, telegraphMat, new Vector3(sx * hx, sy * hy, zMid), new Vector3(e, e, len)).GetComponent<Renderer>());
        // 앞면 사각 틀 (새 바로 앞에서 줄의 단면이 또렷하게 보임)
        telegraphEdges.Add(Prim(PrimitiveType.Cube, "Edge", root, telegraphMat, new Vector3(0f, hy, zStart), new Vector3(size.x + e, e, e)).GetComponent<Renderer>());
        telegraphEdges.Add(Prim(PrimitiveType.Cube, "Edge", root, telegraphMat, new Vector3(0f, -hy, zStart), new Vector3(size.x + e, e, e)).GetComponent<Renderer>());
        telegraphEdges.Add(Prim(PrimitiveType.Cube, "Edge", root, telegraphMat, new Vector3(hx, 0f, zStart), new Vector3(e, size.y + e, e)).GetComponent<Renderer>());
        telegraphEdges.Add(Prim(PrimitiveType.Cube, "Edge", root, telegraphMat, new Vector3(-hx, 0f, zStart), new Vector3(e, size.y + e, e)).GetComponent<Renderer>());

        PulseTelegraphs(0f);
    }

    // progress 0 → 1: 발사가 다가올수록 빠르고 진하게 깜빡임
    void PulseTelegraphs(float progress)
    {
        float speed = Mathf.Lerp(6f, 22f, progress);
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * speed);
        var fill = new MaterialPropertyBlock();
        fill.SetColor("_BaseColor", new Color(1f, 0.05f, 0.03f, Mathf.Lerp(0.30f, 0.55f, blink)));
        var edge = new MaterialPropertyBlock();
        edge.SetColor("_BaseColor", new Color(1f, Mathf.Lerp(0.05f, 0.35f, blink), 0.05f, 0.95f));
        foreach (Renderer r in telegraphFills) if (r != null) r.SetPropertyBlock(fill);
        foreach (Renderer r in telegraphEdges) if (r != null) r.SetPropertyBlock(edge);
    }

    void ClearTelegraphs()
    {
        foreach (GameObject g in telegraphs) if (g != null) Destroy(g);
        telegraphs.Clear();
        telegraphFills.Clear();
        telegraphEdges.Clear();
    }

    // 한 줄 = 줄을 따라 나란히 늘어선 독 오물 덩어리들
    void SpawnSludgeLine(Lane lane)
    {
        float half = (lane.horizontal ? lane.size.x : lane.size.y) * 0.5f - 1.5f;
        for (float d = -half; d <= half; d += 2.8f)
        {
            float size = Random.Range(2.6f, 3.4f);
            float jitter = Random.Range(-0.6f, 0.6f); // 줄 두께 방향으로 살짝 흩어짐
            Vector3 pos = lane.horizontal
                ? new Vector3(lane.center.x + d + Random.Range(-0.4f, 0.4f), lane.center.y + jitter, bossDistance - 2f)
                : new Vector3(lane.center.x + jitter, lane.center.y + d + Random.Range(-0.4f, 0.4f), bossDistance - 2f);
            Transform blob = Prim(PrimitiveType.Sphere, "Sludge", arena, sludgeMat, pos, new Vector3(size, size * 0.85f, size));
            SphereCollider c = blob.gameObject.AddComponent<SphereCollider>();
            c.isTrigger = true;
            c.radius = 0.42f; // 겉모습보다 살짝 작게 → 스치면 봐줌
            blob.gameObject.AddComponent<BossTrigger>().onPlayerEnter = () => bird.GameOver();
            sludge.Add(blob);
        }
    }

    // 풍선 터뜨리는 황금 핀 (머리 구슬 + 바늘)
    Transform MakePin(Vector3 pos, System.Action onCollect)
    {
        Transform pin = new GameObject("BalloonPin").transform;
        pin.SetParent(arena, false);
        pin.localPosition = pos;
        Prim(PrimitiveType.Sphere, "PinHead", pin, pinMat, new Vector3(0f, -0.55f, 0f), Vector3.one * 0.7f);
        Prim(PrimitiveType.Cylinder, "Needle", pin, pinMat, new Vector3(0f, 0.15f, 0f), new Vector3(0.12f, 0.7f, 0.12f));
        pin.localRotation = Quaternion.identity;
        pin.localScale = Vector3.zero; // ItemPhase에서 1.6배까지 커지며 등장

        // 판정은 새가 지나가는 면(z=0)까지 넉넉하게 덮음
        // 핀 크기(1.6배)를 따라가므로 실제 크기 ≈ 2.9 x 2.9, 앞뒤로 z -2 ~ +4
        GameObject pickup = new GameObject("Pickup");
        pickup.transform.SetParent(pin, false);
        pickup.transform.localPosition = new Vector3(0f, 0f, -1.2f);
        BoxCollider box = pickup.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1.8f, 1.8f, 4f);
        pickup.AddComponent<BossTrigger>().onPlayerEnter = onCollect;
        return pin;
    }

    IEnumerator PopBalloon()
    {
        if (balloons.Count > 0)
        {
            Transform b = balloons[0];
            LineRenderer line = strings[0];
            balloons.RemoveAt(0);
            strings.RemoveAt(0);
            bossRenderers.Remove(b.GetComponent<Renderer>());

            // 부풀었다가 펑
            Vector3 s0 = b.localScale;
            float t = 0f;
            while (t < 0.1f) { t += Time.deltaTime; b.localScale = s0 * (1f + t * 4f); yield return null; }
            Destroy(b.gameObject);
            Destroy(line.gameObject);
        }

        hp = Mathf.Max(0f, hp - 1f / BalloonCount);
        yield return Flash();
    }

    IEnumerator Flash()
    {
        // 줄무늬 텍스처도 하얗게 덮어야 몸통까지 번쩍임
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", Color.white);
        block.SetTexture("_BaseMap", Texture2D.whiteTexture);
        for (int k = 0; k < 3; k++)
        {
            foreach (Renderer r in bossRenderers) if (r != null) r.SetPropertyBlock(block);
            yield return new WaitForSeconds(0.07f);
            foreach (Renderer r in bossRenderers) if (r != null) r.SetPropertyBlock(null);
            yield return new WaitForSeconds(0.07f);
        }
    }

    IEnumerator Puff(Transform t, float amount)
    {
        Vector3 s0 = t.localScale;
        float time = 0f;
        while (time < 0.25f)
        {
            time += Time.deltaTime;
            t.localScale = s0 * Mathf.Lerp(amount, 1f, time / 0.25f);
            yield return null;
        }
        t.localScale = s0;
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len = ab.sqrMagnitude;
        float t = len > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    // 기본 도형 만들기 (기본 충돌체는 제거 → 이름이 "Cube"라 게임오버 판정에 걸리는 것도 방지)
    Transform Prim(PrimitiveType type, string name, Transform parent, Material mat, Vector3 localPos, Vector3 localScale)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        DestroyImmediate(g.GetComponent<Collider>());
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = localScale;
        if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
        return g.transform;
    }
}
