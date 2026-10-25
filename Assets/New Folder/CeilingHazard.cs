using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// 🚁 도시 스테이지: 천장은 없지만 높이 날면 위험 (신호등 위로 훌쩍 넘어가며 피하지 못하게)
// - dangerHeight(10.7) 위로 올라가면 화면 가장자리가 빨갛게 번쩍이며 "너무 높아요!" 경고 + 남은 시간 막대·초읽기 (삑 소리)
// - 내려가면 바로 풀림. 계속 limitSeconds(2.5초) 머물면 컷씬:
//   위아래 검은 띠 → 카메라가 새 옆으로 빠짐 → 방송국 헬기가 앞에서 서치라이트를 비추며 날아옴 → 회전 날개에 부딪혀 빙글빙글 추락 → 도로에 쿵 → 게임오버
public class CeilingHazard : MonoBehaviour
{
    [Header("연결")]
    public BirdController bird;

    [Header("머티리얼")]
    public Material bodyMat;    // 헬기 몸통 (빨강)
    public Material stripeMat;  // 흰 띠·꼬리 날개
    public Material glassMat;   // 조종석 유리
    public Material darkMat;    // 날개·다리 (짙은 회색)
    public Material lightMat;   // 서치라이트·경광등 (발광)

    [Header("설정")]
    public float dangerHeight = 10.7f;  // 이 높이 위면 위험
    public float limitSeconds = 2.5f;
    public float roadY = -6.5f;

    // 차가 안 다니는 x: 중앙선, 차선 경계(±17), 인도 쪽 끝 (차선 가운데 ±8.5·±25.5, 차 폭 11)
    static readonly float[] FallGaps = { -33f, -17f, 0f, 17f, 33f };

    private float timer;
    private bool dead;
    private Transform cam;
    private Rigidbody rb;
    private AudioSource sfx, rotorSrc;
    private AudioClip beep, thud;
    private int lastBeep = -1;

    // UI
    private Canvas canvas;
    private Image vignette, barFill;
    private Text warnText, countText, title;
    private GameObject warnGroup;
    private RectTransform barTop, barBottom;

    void Start()
    {
        if (bird == null) bird = FindAnyObjectByType<BirdController>();
        rb = bird.GetComponent<Rigidbody>();
        cam = Camera.main.transform;
        float v = PlayerPrefs.GetFloat("JumpVolume", 1f);
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.volume = v * v; // 효과음 슬라이더와 같은 크기
        rotorSrc = gameObject.AddComponent<AudioSource>();
        rotorSrc.playOnAwake = false;
        rotorSrc.loop = true;
        rotorSrc.spatialBlend = 0f;
        rotorSrc.volume = v * v * 0.8f;
        beep = Tone(880f, 0.12f, 0.35f);
        thud = Thud();
        rotorSrc.clip = RotorClip();
        BuildUI();
    }

    void Update()
    {
        if (dead || bird == null) return;
        bool danger = bird.IsPlaying && bird.transform.position.y >= dangerHeight;
        if (danger) timer += Time.deltaTime;
        else timer = 0f;

        bool show = timer > 0f;
        if (warnGroup.activeSelf != show) warnGroup.SetActive(show);
        if (!show) { SetVignette(0f); lastBeep = -1; return; }

        float left = Mathf.Max(0f, limitSeconds - timer);
        barFill.fillAmount = left / limitSeconds;
        countText.text = left.ToString("0.0");
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 22f, timer / limitSeconds));
        SetVignette(Mathf.Lerp(0.35f, 0.75f, pulse) * Mathf.Clamp01(timer * 3f));
        warnText.transform.localScale = Vector3.one * (1f + pulse * 0.06f);
        // 1초마다 삑 (마지막 1초는 더 높게)
        int sec = Mathf.FloorToInt(timer);
        if (sec != lastBeep) { lastBeep = sec; Play(beep, sec >= limitSeconds - 1f ? 1.4f : 1f); }

        if (timer >= limitSeconds) StartCoroutine(Cutscene());
    }

    // 보스전 동안 끔 (경고 화면도 바로 지움)
    public void Stop()
    {
        timer = 0f;
        if (warnGroup != null) warnGroup.SetActive(false);
        if (vignette != null) SetVignette(0f);
        enabled = false;
    }

    // ───────────────────────── 컷씬: 헬기에 부딪혀 추락 ─────────────────────────

    IEnumerator Cutscene()
    {
        dead = true;
        warnGroup.SetActive(false);
        SetVignette(0f);
        bird.cutscene = true;          // 조작 멈춤
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        StartCoroutine(Letterbox(true, 0.4f));

        // 카메라: 새에서 떼어 내서 옆 아래에서 새와 앞에서 오는 헬기를 함께 봄 (양옆·천장 쉴드는 숨김)
        cam.SetParent(null, true);
        BoundaryWall walls = bird.GetComponent<BoundaryWall>();
        if (walls != null) walls.HideShields();
        Vector3 b0 = bird.transform.position;
        Vector3 camTo = b0 + new Vector3(-11f, -3f, -5f);
        Vector3 camFrom = cam.position;

        Transform heli = BuildHelicopter();
        Vector3 hStart = b0 + new Vector3(18f, 7f, 70f);
        Vector3 hHit = b0 + new Vector3(0f, 1.2f, 0f);
        heli.position = hStart;
        heli.rotation = Quaternion.LookRotation(hHit - hStart);
        Transform rotor = heli.Find("MainRotor"), tail = heli.Find("TailRotor");
        Light spot = heli.GetComponentInChildren<Light>();
        rotorSrc.Play();

        title.text = "TOO HIGH!";
        StartCoroutine(TitlePop());

        // 1) 헬기가 서치라이트로 새를 비추며 다가옴 (새는 제자리에서 놀라 파닥임)
        const float approach = 1.8f;
        for (float t = 0f; t < approach; t += Time.deltaTime)
        {
            float u = t / approach;
            float e = u * u * (3f - 2f * u);
            heli.position = Vector3.Lerp(hStart, hHit + new Vector3(0f, 0f, 3f), e);
            heli.rotation = Quaternion.Slerp(heli.rotation, Quaternion.LookRotation(b0 - heli.position) * Quaternion.Euler(12f, 0f, 0f), Time.deltaTime * 4f);
            SpinRotors(rotor, tail);
            if (spot != null) spot.transform.rotation = Quaternion.LookRotation(bird.transform.position - spot.transform.position);
            bird.transform.position = b0 + Vector3.up * Mathf.Sin(t * 14f) * 0.15f;
            cam.position = Vector3.Lerp(camFrom, camTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.7f)));
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation((bird.transform.position + heli.position) * 0.5f - cam.position), Time.deltaTime * 6f);
            rotorSrc.volume = Mathf.Lerp(0.2f, 1f, e) * sfx.volume;
            yield return null;
        }

        // 2) 회전 날개에 쾅 → 깃털 + 화면 흔들림, 새는 빙글빙글 튕겨 나가며 추락, 헬기는 위로 빠져나감
        Play(thud, 1.3f);
        Feathers(bird.transform.position, 18);
        Vector3 pos = bird.transform.position;
        // 차가 안 다니는 틈(중앙선·차선 경계·인도 쪽)으로 떨어지게 → 커다란 차에 가려지지 않음
        float gx = 0f;
        foreach (float g in FallGaps) if (Mathf.Abs(g - pos.x) < Mathf.Abs(gx - pos.x)) gx = g;
        // 위로 4만큼 튕겼다가 중력 28로 떨어지는 시간 → 그동안 틈까지 옆으로 이동
        float drop = pos.y - (roadY + 0.6f);
        float fallTime = (4f + Mathf.Sqrt(16f + 56f * drop)) / 28f;
        Vector3 vel = new Vector3((gx - pos.x) / fallTime, 4f, -4f);
        Vector3 spin = new Vector3(420f, 260f, 520f);
        Vector3 hVel = (heli.forward + Vector3.up * 0.8f).normalized * 22f;
        // 카메라: 떨어질 틈 바로 위 뒤쪽, 차 지붕보다 한참 높은 곳에서 비스듬히 내려다봄 (차가 카메라를 뚫고 지나가지 않게)
        Vector3 camTarget = new Vector3(gx, roadY + 16f, pos.z - 14f);
        float shake = 0.5f;
        Vector3 camBase = cam.position;
        bool landed = false;
        float after = 0f;
        while (after < 1.2f)
        {
            float dt = Time.deltaTime;
            if (!landed)
            {
                vel += Vector3.down * 28f * dt;
                pos += vel * dt;
                bird.transform.Rotate(spin * dt, Space.World);
                if (pos.y <= roadY + 0.6f)
                {
                    pos.y = roadY + 0.6f;
                    landed = true;
                    Play(thud, 0.7f);
                    Feathers(pos, 10);
                    shake = 0.6f;
                }
                bird.transform.position = pos;
            }
            else after += dt;

            if (heli != null)
            {
                heli.position += hVel * dt;
                heli.rotation *= Quaternion.Euler(-20f * dt, 30f * dt, 0f);
                SpinRotors(rotor, tail);
            }
            rotorSrc.volume = Mathf.MoveTowards(rotorSrc.volume, 0f, dt * 0.4f);

            // 카메라: 떨어질 자리 쪽으로 옮기며 새를 따라 고개를 숙임 + 살짝 흔들림
            shake = Mathf.MoveTowards(shake, 0f, dt * 1.2f);
            camBase = Vector3.Lerp(camBase, camTarget, dt * 2.5f);
            cam.position = camBase + Random.insideUnitSphere * shake;
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(bird.transform.position - cam.position), dt * 6f);
            yield return null;
        }

        rotorSrc.Stop();
        if (heli != null) Destroy(heli.gameObject);
        bird.GameOver();
        yield return new WaitForSeconds(0.4f);
        StartCoroutine(Letterbox(false, 0.4f));
    }

    void SpinRotors(Transform rotor, Transform tail)
    {
        if (rotor != null) rotor.localRotation *= Quaternion.Euler(0f, 1500f * Time.deltaTime, 0f);
        if (tail != null) tail.localRotation *= Quaternion.Euler(1800f * Time.deltaTime, 0f, 0f);
    }

    // 방송국 헬기 (빨간 몸통 + 흰 띠, 앞이 +z): 몸통·조종석·꼬리·꼬리 날개·다리·회전 날개·서치라이트
    Transform BuildHelicopter()
    {
        Transform root = new GameObject("NewsHelicopter").transform;
        Part(root, PrimitiveType.Sphere, new Vector3(0f, 0f, 0f), new Vector3(3.2f, 2.8f, 5.6f), bodyMat);
        Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.25f, 1.7f), new Vector3(2.6f, 2.0f, 2.8f), glassMat);
        Part(root, PrimitiveType.Cube, new Vector3(0f, -0.1f, 0f), new Vector3(3.25f, 0.5f, 4.4f), stripeMat);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 0.4f, -5.2f), new Vector3(0.6f, 0.6f, 6f), bodyMat);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 1.3f, -8f), new Vector3(0.25f, 2.2f, 1.2f), stripeMat);
        for (int s = -1; s <= 1; s += 2)
        {
            Part(root, PrimitiveType.Cube, new Vector3(s * 1.3f, -1.9f, 0f), new Vector3(0.18f, 0.18f, 4.6f), darkMat);
            Part(root, PrimitiveType.Cube, new Vector3(s * 1.1f, -1.5f, 1f), new Vector3(0.12f, 0.8f, 0.12f), darkMat);
            Part(root, PrimitiveType.Cube, new Vector3(s * 1.1f, -1.5f, -1f), new Vector3(0.12f, 0.8f, 0.12f), darkMat);
        }
        Part(root, PrimitiveType.Cube, new Vector3(0f, 1.6f, 0f), new Vector3(0.35f, 0.5f, 0.35f), darkMat);

        Transform rotor = new GameObject("MainRotor").transform;
        rotor.SetParent(root, false);
        rotor.localPosition = new Vector3(0f, 1.9f, 0f);
        Part(rotor, PrimitiveType.Cube, Vector3.zero, new Vector3(12f, 0.08f, 0.5f), darkMat);
        Part(rotor, PrimitiveType.Cube, Vector3.zero, new Vector3(0.5f, 0.08f, 12f), darkMat);

        Transform tail = new GameObject("TailRotor").transform;
        tail.SetParent(root, false);
        tail.localPosition = new Vector3(0.4f, 1.0f, -8.1f);
        Part(tail, PrimitiveType.Cube, Vector3.zero, new Vector3(0.08f, 2.6f, 0.3f), darkMat);

        // 서치라이트 (몸통 아래 앞) + 빨간 경광등
        Transform lamp = Part(root, PrimitiveType.Sphere, new Vector3(0f, -1.3f, 2.2f), new Vector3(0.6f, 0.6f, 0.6f), lightMat).transform;
        Light l = new GameObject("Searchlight").AddComponent<Light>();
        l.transform.SetParent(lamp, false);
        l.type = LightType.Spot;
        l.range = 80f;
        l.spotAngle = 22f;
        l.intensity = 40f;
        l.color = new Color(1f, 0.97f, 0.88f);
        return root;
    }

    GameObject Part(Transform parent, PrimitiveType shape, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(shape);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
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
        for (float t = 0f; t < 2.5f && f != null; t += Time.deltaTime)
        {
            v = Vector3.Lerp(v, new Vector3(Mathf.Sin(t * 4f) * 1.5f, -1.2f, 0f), Time.deltaTime * 2f);
            f.position += v * Time.deltaTime;
            f.Rotate(spin * Time.deltaTime, Space.World);
            yield return null;
        }
        if (f != null) Destroy(f.gameObject);
    }

    // ───────────────────────── 소리 (코드로 만듦) ─────────────────────────

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
            data[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * amp * 0.5f * env + Mathf.Sin(2f * Mathf.PI * freq * t) * amp * 0.5f * env;
        }
        AudioClip c = AudioClip.Create("WarnBeep", n, 1, rate, false);
        c.SetData(data, 0);
        return c;
    }

    // 쿵: 낮게 떨어지는 사인 + 잡음
    static AudioClip Thud()
    {
        int rate = 44100, n = rate / 2;
        var data = new float[n];
        var rnd = new System.Random(3);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float f = Mathf.Lerp(120f, 40f, t * 2f);
            phase += 2f * Mathf.PI * f / rate;
            float env = Mathf.Exp(-t * 9f);
            data[i] = (Mathf.Sin(phase) * 0.8f + ((float)rnd.NextDouble() * 2f - 1f) * 0.35f * Mathf.Exp(-t * 25f)) * env;
        }
        AudioClip c = AudioClip.Create("Thud", n, 1, rate, false);
        c.SetData(data, 0);
        return c;
    }

    // 헬기 소리: 잡음을 초당 13번 두두두 끊어 줌 (1초 반복)
    static AudioClip RotorClip()
    {
        int rate = 22050, n = rate;
        var data = new float[n];
        var rnd = new System.Random(7);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float noise = (float)rnd.NextDouble() * 2f - 1f;
            lp += (noise - lp) * 0.08f; // 낮은 소리만 남김
            float chop = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * 13f * t), 3f);
            data[i] = lp * (0.3f + 1.6f * chop);
        }
        AudioClip c = AudioClip.Create("Rotor", n, 1, rate, false);
        c.SetData(data, 0);
        return c;
    }

    // ───────────────────────── UI ─────────────────────────

    // 화면 가장자리 빨간 번쩍임 + 위쪽 경고 문구·초읽기·남은 시간 막대 + 컷씬용 검은 띠·제목
    void BuildUI()
    {
        GameObject root = new GameObject("CeilingHazardUI", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1f;
        Font font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 40);

        vignette = NewUI<Image>("Vignette", root.transform);
        vignette.sprite = EdgeSprite();
        vignette.raycastTarget = false;
        Stretch(vignette.rectTransform);
        SetVignette(0f);

        barTop = Bar(root.transform, "BarTop", 1f);
        barBottom = Bar(root.transform, "BarBottom", 0f);

        warnGroup = new GameObject("Warning", typeof(RectTransform));
        warnGroup.transform.SetParent(root.transform, false);
        RectTransform wg = (RectTransform)warnGroup.transform;
        wg.anchorMin = wg.anchorMax = new Vector2(0.5f, 1f);
        wg.anchoredPosition = new Vector2(0f, -150f);
        wg.sizeDelta = new Vector2(700f, 160f);

        warnText = NewText(warnGroup.transform, font, "너무 높아요! 내려가세요", 38, new Color(1f, 0.35f, 0.3f), new Vector2(0f, 40f), new Vector2(900f, 60f));
        countText = NewText(warnGroup.transform, font, limitSeconds.ToString("0.0"), 30, Color.white, new Vector2(0f, -8f), new Vector2(200f, 44f));

        Image back = NewUI<Image>("TimerBack", warnGroup.transform);
        back.color = new Color(0.1f, 0.02f, 0.02f, 0.75f);
        back.raycastTarget = false;
        RectTransform brt = back.rectTransform;
        brt.anchoredPosition = new Vector2(0f, -46f);
        brt.sizeDelta = new Vector2(380f, 16f);
        back.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.6f, 0.55f, 0.8f);
        barFill = NewUI<Image>("TimerFill", back.transform);
        barFill.sprite = WhiteSprite();
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.color = new Color(1f, 0.25f, 0.2f);
        barFill.raycastTarget = false;
        Stretch(barFill.rectTransform);
        barFill.rectTransform.offsetMin = new Vector2(3f, 3f);
        barFill.rectTransform.offsetMax = new Vector2(-3f, -3f);
        warnGroup.SetActive(false);

        title = NewText(root.transform, font, "", 84, new Color(1f, 0.4f, 0.3f), new Vector2(0f, 150f), new Vector2(1000f, 120f));
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    }

    Text NewText(Transform parent, Font font, string text, int size, Color color, Vector2 pos, Vector2 box)
    {
        Text t = NewUI<Text>("Text", parent);
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        Outline o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.15f, 0.02f, 0.02f, 0.95f);
        o.effectDistance = new Vector2(2f, -2f);
        t.rectTransform.anchoredPosition = pos;
        t.rectTransform.sizeDelta = box;
        return t;
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

    void SetVignette(float a) { vignette.color = new Color(1f, 0.12f, 0.08f, a); }

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
        if (!on) title.text = "";
    }

    // 가장자리로 갈수록 진해지는 테두리 (가운데는 투명)
    static Sprite EdgeSprite()
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
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.1f, d)));
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
