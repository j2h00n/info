using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// 🧊 얼음 동굴 스테이지: 벽을 하나 지날 때마다 일정 확률(기본 40%)로 새가 얼음에 갇힘
// - 그 자리에 딱 멈추고, 알 깨기와 같은 방식으로 ← → (모션: 고개 좌우)를 2번 흔들면 얼음이 깨지며 다시 출발
// - 2초 안에 못 깨면: 새가 갇힌 얼음 덩어리가 아래로 떨어져 바닥에 부딪혀 산산조각 → 게임오버
// 스토리 모드는 동굴 붕괴(300m) 전까지만, 엔드리스는 계속
public class IceFreeze : MonoBehaviour
{
    [Header("연결")]
    public BirdController bird;

    [Header("머티리얼")]
    public Material iceMat;     // 반투명 얼음 (새가 비쳐 보임)
    public Material shardMat;   // 깨진 조각
    public Material crackMat;   // 금 선 (흰색)

    [Header("사운드")]
    public AudioClip freezeSound, crackSound, shatterSound, thudSound;

    [Header("설정")]
    [Range(0f, 1f)] public float chance = 0.4f;
    public int pressesToBreak = 2;
    public float breakTime = 2f;

    private int checkedWalls;       // 이미 확률을 굴린 벽 수
    private bool active;
    private Transform block, cam, seagull;
    private Vector3 seagullPos;
    private Quaternion seagullRot;
    private Rigidbody rb;
    private SeagullFlap flap;
    private AudioSource sfx;
    private readonly List<LineRenderer> cracks = new List<LineRenderer>();
    private int presses, lastMotionSign;
    private float tilt, tiltVel, hop;

    // UI
    private Canvas canvas;
    private Text prompt, title;
    private Image timerFill, frost;
    private RectTransform barTop, barBottom;

    public bool Active { get { return active; } }

    void Start()
    {
        if (bird == null) bird = FindAnyObjectByType<BirdController>();
        rb = bird.GetComponent<Rigidbody>();
        flap = bird.GetComponentInChildren<SeagullFlap>();
        seagull = bird.transform.Find("Seagull");
        if (seagull != null) { seagullPos = seagull.localPosition; seagullRot = seagull.localRotation; }
        cam = Camera.main.transform;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        float v = PlayerPrefs.GetFloat("JumpVolume", 1f);
        sfx.volume = v * v; // 효과음 슬라이더와 같은 크기
        BuildUI();
    }

    void Update()
    {
        if (active || bird == null || !bird.IsPlaying || bird.BossStarted) return;

        // 벽을 3m 지나친 순간마다 한 번씩 확률을 굴림 (벽 사이 빈 공간에서 멈추게)
        float z = bird.transform.position.z;
        int passed = Mathf.Max(0, Mathf.FloorToInt((z - NewMonoBehaviourScript.FirstChunkZ - 3f) / NewMonoBehaviourScript.Spacing) + 1);
        if (passed <= checkedWalls) return;
        checkedWalls = passed;
        if (bird.IsStory && z > bird.storyGoalMeters - 15f) return; // 붕괴 직전엔 안 얼림
        if (Random.value < chance) StartCoroutine(Freeze());
    }

    // ───────────────────────── 얼어붙기 ─────────────────────────

    IEnumerator Freeze()
    {
        active = true;
        presses = 0;
        tilt = tiltVel = hop = 0f;
        bird.cutscene = true;              // 조작·물리 이동 멈춤
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        if (flap != null) flap.enabled = false; // 날개도 그대로 굳음
        Play(freezeSound, 1f);

        // 고개를 이미 기울인 채로 얼었으면 그건 한 번으로 안 침 (다시 움직여야 함)
        float m0 = bird.MoveInput;
        lastMotionSign = m0 > 0.6f ? 1 : m0 < -0.6f ? -1 : 0;

        BuildBlock();
        canvas.enabled = true;
        prompt.text = bird.UsesMotionControl ? "고개를 좌우로 2번 흔들어 얼음을 깨요!" : "← → 를 2번 눌러 얼음을 깨요!";
        prompt.color = new Color(0.75f, 0.93f, 1f, 1f);
        title.text = "";

        // 얼음이 빠르게 자라며 새를 감쌈 + 몸이 수평으로
        Quaternion fromRot = bird.transform.rotation;
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            float e = Mathf.SmoothStep(0f, 1f, t / 0.25f);
            block.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, e);
            bird.transform.rotation = Quaternion.Slerp(fromRot, Quaternion.identity, e);
            SetFrost(e * 0.55f);
            yield return null;
        }
        block.localScale = Vector3.one;

        float left = breakTime;
        while (left > 0f && presses < pressesToBreak)
        {
            if (bird.IsGameOver) { Cleanup(); yield break; }
            left -= Time.deltaTime;
            int dir = ReadPress();
            if (dir != 0)
            {
                presses++;
                tiltVel += dir * 300f;
                hop = 1f;
                Play(crackSound, Random.Range(0.95f, 1.15f));
                AddCracks();
            }
            AnimateBlock(left);
            float k = left / breakTime;
            timerFill.fillAmount = k;
            timerFill.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.55f, 0.88f, 1f), Mathf.Clamp01((k - 0.15f) / 0.5f));
            prompt.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * (k < 0.4f ? 12f : 6f)) * 0.06f);
            SetFrost(Mathf.Lerp(0.95f, 0.55f, k)); // 시간이 갈수록 화면 가장자리가 하얗게 얼어붙음
            yield return null;
        }

        if (presses >= pressesToBreak) yield return Break();
        else yield return FallToDeath();
    }

    // 왼쪽 = +1, 오른쪽 = -1 (알 깨기와 같은 방식). 모션이면 고개 기울기가 바뀔 때마다 한 번
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

    // 흔들흔들: 누른 쪽으로 기울었다 스프링처럼 돌아옴 + 통 튐 + 시간이 얼마 안 남으면 부들부들
    // 카메라가 새 몸(Cube)에 붙어 있어서, 새 전체가 아니라 얼음 덩어리와 갈매기 모델만 기울임 (화면은 그대로)
    void AnimateBlock(float left)
    {
        float dt = Time.deltaTime;
        tiltVel += (-tilt * 90f - tiltVel * 9f) * dt;
        tilt += tiltVel * dt;
        hop = Mathf.MoveTowards(hop, 0f, dt * 5f);
        float shiver = left < 1.5f ? Mathf.Sin(Time.time * 60f) * 1.5f : 0f;
        // 카메라가 새 뒤에 있어서 화면 왼쪽 = 세상 -x → 왼쪽을 누르면 화면 왼쪽으로 기울게 (z축 +회전)
        Quaternion rot = Quaternion.Euler(0f, 0f, tilt + shiver);
        Vector3 up = Vector3.up * Mathf.Sin(hop * Mathf.PI) * 0.1f;
        block.localRotation = rot;
        block.localPosition = BlockPos + up;
        if (seagull != null)
        {
            // 얼음 가운데를 축으로 같이 돎
            seagull.localPosition = BlockPos + rot * (seagullPos - BlockPos) + up;
            seagull.localRotation = rot * seagullRot;
        }
    }
    static readonly Vector3 BlockPos = new Vector3(0f, 0.05f, 0f);

    void ResetSeagull()
    {
        if (seagull == null) return;
        seagull.localPosition = seagullPos;
        seagull.localRotation = seagullRot;
    }

    // ───────────────────────── 깨고 나옴 ─────────────────────────

    IEnumerator Break()
    {
        Play(shatterSound, 1f);
        Shatter(block.position, 26, 4f);
        Destroy(block.gameObject);
        block = null;
        ResetSeagull();
        bird.transform.rotation = Quaternion.identity;
        if (flap != null) { flap.enabled = true; flap.Flap(); }

        rb.isKinematic = false;
        rb.linearVelocity = new Vector3(0f, 0f, bird.forwardSpeed);
        rb.AddForce(Vector3.up * bird.flapForce * 0.6f, ForceMode.Impulse); // 얼음을 박차고 나옴
        bird.cutscene = false;

        prompt.text = "";
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            SetFrost(Mathf.Lerp(0.6f, 0f, t / 0.4f));
            yield return null;
        }
        Cleanup();
    }

    // ───────────────────────── 못 깨면: 얼음째 추락 ─────────────────────────

    IEnumerator FallToDeath()
    {
        prompt.text = "";
        timerFill.transform.parent.gameObject.SetActive(false);
        StartCoroutine(Letterbox(true, 0.35f));

        // 카메라는 그 자리에 남아서 떨어지는 얼음을 내려다봄 (새를 따라가지 않게 떼어 냄)
        cam.SetParent(null, true);
        // 배경은 원래 새 높이를 따라다니므로, 떨어지는 동안은 따라오지 않게 멈춤
        foreach (FollowEnvironment f in FindObjectsByType<FollowEnvironment>()) f.enabled = false;

        // 쩍 금 가는 소리 + 부르르
        Play(crackSound, 0.8f);
        AddCracks();
        Vector3 start = bird.transform.position;
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            bird.transform.position = start + Random.insideUnitSphere * 0.06f;
            yield return null;
        }

        // 떨어짐: 점점 빨라지며 앞으로 살짝 고꾸라짐
        title.text = "FROZEN!";
        StartCoroutine(TitlePop());
        Vector3 pos = start;
        float vy = 0f;
        float spinZ = Random.value < 0.5f ? -1f : 1f;
        float floor = bird.fallLimitY - 22f;
        Vector3 lookFrom = cam.position;
        while (pos.y > floor)
        {
            float dt = Time.deltaTime;
            vy -= 30f * dt;
            pos.y += vy * dt;
            bird.transform.position = pos;
            bird.transform.rotation *= Quaternion.Euler(60f * dt, 0f, spinZ * 40f * dt);
            // 카메라: 제자리에서 고개만 숙여 따라봄 + 살짝 내려감
            cam.position = Vector3.Lerp(cam.position, lookFrom + Vector3.down * 4f, dt * 1.5f);
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(pos - cam.position), dt * 8f);
            SetFrost(0.95f);
            yield return null;
        }

        // 바닥에 쾅 → 산산조각 + 화면 흔들림
        Play(thudSound, 1f);
        Play(shatterSound, 0.85f);
        Shatter(pos + Vector3.up * 0.3f, 40, 7f);
        bird.transform.Find("Seagull")?.gameObject.SetActive(false);
        if (block != null) block.gameObject.SetActive(false);
        Vector3 camHold = cam.position;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            cam.position = camHold + Random.insideUnitSphere * 0.35f * (1f - t / 0.5f);
            yield return null;
        }
        cam.position = camHold;
        bird.GameOver();
        yield return new WaitForSeconds(0.6f);
        StartCoroutine(Letterbox(false, 0.4f));
    }

    void Cleanup()
    {
        active = false;
        if (block != null) { Destroy(block.gameObject); block = null; }
        foreach (LineRenderer l in cracks) if (l != null) Destroy(l.gameObject);
        cracks.Clear();
        canvas.enabled = false;
        timerFill.transform.parent.gameObject.SetActive(true);
        SetFrost(0f);
    }

    // ───────────────────────── 얼음 덩어리 ─────────────────────────

    // 울퉁불퉁한 얼음 덩어리 + 바깥으로 삐죽 튀어나온 결정 기둥 몇 개 (새 몸에 붙어서 같이 움직임)
    void BuildBlock()
    {
        block = new GameObject("IceBlock").transform;
        block.SetParent(bird.transform, false);
        block.localPosition = BlockPos;

        var kit = new MeshKit();
        // 몸통: 꼭짓점을 흐트러뜨린 상자 (면마다 따로 → 각진 얼음)
        Vector3 h = new Vector3(1.05f, 0.85f, 1.0f);
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3 s = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
            c[i] = Vector3.Scale(s, h) + Random.insideUnitSphere * 0.18f;
        }
        int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 } };
        foreach (int[] f in faces)
        {
            Vector3 mid = (c[f[0]] + c[f[1]] + c[f[2]] + c[f[3]]) * 0.25f;
            // 면 가운데를 살짝 부풀려 4조각 → 더 울퉁불퉁
            Vector3 bulge = mid * Random.Range(1.04f, 1.12f);
            for (int k = 0; k < 4; k++) kit.Tri(c[f[k]], c[f[(k + 1) % 4]], bulge, mid);
        }
        // 결정 기둥: 육각 기둥 + 뾰족한 끝
        for (int i = 0; i < 6; i++)
        {
            Vector3 dir = Quaternion.Euler(Random.Range(-60f, 60f), Random.Range(0f, 360f), 0f) * Vector3.up;
            if (i < 2) dir = Quaternion.Euler(0f, 0f, i == 0 ? 50f : -50f) * Vector3.up; // 양 어깨 위로 하나씩
            Crystal(kit, dir * 0.55f, dir, Random.Range(0.6f, 1.0f), Random.Range(0.14f, 0.22f));
        }
        GameObject g = new GameObject("IceMesh");
        g.transform.SetParent(block, false);
        g.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("IceBlock");
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = iceMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Crystal(MeshKit kit, Vector3 basePos, Vector3 dir, float length, float radius)
    {
        Vector3 right = Vector3.Cross(dir, Vector3.forward);
        if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(dir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, dir).normalized;
        const int sides = 6;
        Vector3 top = basePos + dir * length;
        Vector3 tip = top + dir * radius * 1.8f;
        for (int k = 0; k < sides; k++)
        {
            float a0 = Mathf.PI * 2f * k / sides, a1 = Mathf.PI * 2f * (k + 1) / sides;
            Vector3 o0 = right * Mathf.Cos(a0) * radius + up * Mathf.Sin(a0) * radius;
            Vector3 o1 = right * Mathf.Cos(a1) * radius + up * Mathf.Sin(a1) * radius;
            Vector3 outDir = (o0 + o1) * 0.5f;
            kit.Tri(basePos + o0, basePos + o1, top + o1, outDir);
            kit.Tri(basePos + o0, top + o1, top + o0, outDir);
            kit.Tri(top + o0, top + o1, tip, outDir + dir * radius);
        }
    }

    // 누를 때마다 카메라 쪽 면(뒤)과 윗면에 금이 늘어남
    void AddCracks()
    {
        if (block == null) return;
        int lines = presses <= 1 ? 3 : 4;
        for (int i = 0; i < lines; i++)
        {
            var pts = new List<Vector3>();
            Vector3 p = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.4f, 0.5f), -1.08f);
            Vector2 dir = Random.insideUnitCircle.normalized;
            for (int s = 0; s < 6; s++)
            {
                pts.Add(p);
                dir = (dir + Random.insideUnitCircle * 0.8f).normalized;
                p += new Vector3(dir.x, dir.y, 0f) * Random.Range(0.12f, 0.22f);
                p.x = Mathf.Clamp(p.x, -1f, 1f);
                p.y = Mathf.Clamp(p.y, -0.8f, 0.8f);
            }
            GameObject g = new GameObject("IceCrack");
            g.transform.SetParent(block, false);
            LineRenderer l = g.AddComponent<LineRenderer>();
            l.useWorldSpace = false;
            l.sharedMaterial = crackMat;
            l.widthMultiplier = 0.035f;
            l.numCornerVertices = 2;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.positionCount = pts.Count;
            l.SetPositions(pts.ToArray());
            cracks.Add(l);
        }
    }

    // 얼음 조각이 사방으로 튀었다 떨어짐 (보기만, 1.5초 뒤 정리)
    void Shatter(Vector3 at, int count, float power)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject s = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>()); // 이름이 "Cube"라도 판정 안 걸리게
            s.name = "IceShard";
            s.GetComponent<Renderer>().sharedMaterial = shardMat;
            s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.transform.position = at + Random.insideUnitSphere * 0.6f;
            s.transform.localScale = new Vector3(Random.Range(0.1f, 0.35f), Random.Range(0.08f, 0.25f), Random.Range(0.1f, 0.3f));
            s.transform.rotation = Random.rotation;
            Vector3 v = Random.insideUnitSphere * power + Vector3.up * power * 0.5f;
            StartCoroutine(FlyShard(s.transform, v));
        }
    }

    IEnumerator FlyShard(Transform s, Vector3 v)
    {
        Vector3 spin = Random.insideUnitSphere * 600f;
        for (float t = 0f; t < 1.5f && s != null; t += Time.deltaTime)
        {
            v += Physics.gravity * Time.deltaTime;
            s.position += v * Time.deltaTime;
            s.Rotate(spin * Time.deltaTime, Space.World);
            s.localScale *= 1f - Time.deltaTime * 0.6f;
            yield return null;
        }
        if (s != null) Destroy(s.gameObject);
    }

    void Play(AudioClip clip, float pitch)
    {
        if (clip == null || sfx == null) return;
        sfx.pitch = pitch;
        sfx.PlayOneShot(clip);
    }

    // ───────────────────────── UI ─────────────────────────

    // 아래 가운데 안내 문구 + 남은 시간 막대, 화면 가장자리 성에, 컷씬용 위아래 검은 띠 + 큰 제목
    void BuildUI()
    {
        GameObject root = new GameObject("IceFreezeUI", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1f;
        Font font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 40);

        // 성에: 가장자리로 갈수록 하얗게 (코드로 만든 둥근 그라데이션)
        frost = NewUI<Image>("Frost", root.transform);
        frost.sprite = FrostSprite();
        frost.color = new Color(0.85f, 0.95f, 1f, 0f);
        frost.raycastTarget = false;
        Stretch(frost.rectTransform);

        barTop = Bar(root.transform, "BarTop", 1f);
        barBottom = Bar(root.transform, "BarBottom", 0f);

        prompt = NewUI<Text>("Prompt", root.transform);
        prompt.font = font;
        prompt.fontSize = 40;
        prompt.fontStyle = FontStyle.Bold;
        prompt.alignment = TextAnchor.MiddleCenter;
        prompt.horizontalOverflow = HorizontalWrapMode.Overflow;
        prompt.raycastTarget = false;
        Outline o = prompt.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.05f, 0.12f, 0.25f, 0.95f);
        o.effectDistance = new Vector2(2f, -2f);
        RectTransform prt = prompt.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 140f);
        prt.sizeDelta = new Vector2(1100f, 70f);

        // 남은 시간 막대 (얼음색 → 빨강)
        Image back = NewUI<Image>("TimerBack", root.transform);
        back.color = new Color(0.05f, 0.1f, 0.2f, 0.75f);
        back.raycastTarget = false;
        RectTransform brt = back.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0f, 92f);
        brt.sizeDelta = new Vector2(420f, 18f);
        back.gameObject.AddComponent<Outline>().effectColor = new Color(0.8f, 0.95f, 1f, 0.8f);
        timerFill = NewUI<Image>("TimerFill", back.transform);
        timerFill.sprite = WhiteSprite();
        timerFill.type = Image.Type.Filled;
        timerFill.fillMethod = Image.FillMethod.Horizontal;
        timerFill.raycastTarget = false;
        Stretch(timerFill.rectTransform);
        timerFill.rectTransform.offsetMin = new Vector2(3f, 3f);
        timerFill.rectTransform.offsetMax = new Vector2(-3f, -3f);

        title = NewUI<Text>("Title", root.transform);
        title.font = font;
        title.fontSize = 84;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.color = new Color(0.7f, 0.92f, 1f);
        title.raycastTarget = false;
        Outline to = title.gameObject.AddComponent<Outline>();
        to.effectColor = new Color(0.03f, 0.08f, 0.2f, 1f);
        to.effectDistance = new Vector2(3f, -3f);
        RectTransform trt = title.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0f, 150f);
        trt.sizeDelta = new Vector2(1000f, 120f);

        canvas.enabled = false;
    }

    IEnumerator TitlePop()
    {
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, Mathf.SmoothStep(0f, 1f, t / 0.25f));
            yield return null;
        }
        title.rectTransform.localScale = Vector3.one;
    }

    void SetFrost(float a) { frost.color = new Color(0.85f, 0.95f, 1f, a); }

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
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float hgt = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, hgt);
            yield return null;
        }
        barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, to);
    }

    static Sprite FrostSprite()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * 0.6f + Mathf.Sqrt(dx * dx + dy * dy) * 0.4f;
                float a = Mathf.SmoothStep(0.55f, 1.05f, d);
                // 얼음 결정 무늬처럼 살짝 얼룩지게
                a *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static Sprite WhiteSprite()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
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
