using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// 스테이지 넘어갈 때: 3초 페이드아웃 → 밤하늘 화면에 다음 스테이지 이름 + 팁 카드 4초 → 바로 페이드인
// 씬이 바뀌어도 사라지지 않도록 DontDestroyOnLoad로 혼자 살아남아 진행
// 이미지 파일 없이 둥근 판/빛 알갱이/배경 그라데이션을 코드로 만들어 씀 (UI 팔레트는 GameUIStyler와 같은 색)
public class StageTransition : MonoBehaviour
{
    public const float FadeOutTime = 3f;
    public const float TipTime = 4f;
    public const float FadeInTime = 1f;

    public static bool IsRunning { get; private set; }

    static readonly Color Cream = new Color(0.965f, 0.906f, 0.757f);
    static readonly Color Gold = new Color(0.98f, 0.72f, 0.30f);
    static readonly Color Navy = new Color(0.11f, 0.13f, 0.25f);

    const int MoteCount = 28;

    private RawImage background;     // 페이드되는 배경 (가운데 남색 → 가장자리 검정)
    private CanvasGroup content;     // 스테이지 이름 + 팁 카드 + 로딩 바 전체
    private RectTransform header;    // 위쪽: NEXT STAGE / STAGE 2 / DESERT
    private RectTransform card;      // 가운데: 팁 카드
    private Image barFill;           // 아래: 로딩 바
    private readonly List<Mote> motes = new List<Mote>();
    private readonly List<Object> generated = new List<Object>(); // 코드로 만든 텍스처/스프라이트 (끝나면 정리)
    private Font font;

    class Mote { public RectTransform rt; public Image img; public float speed, sway, phase, alpha; }

    // autoStartStory: 도착한 스테이지에서 메뉴 없이 바로 스토리 시작 (보스 클리어 후 이어서 진행할 때)
    public static void Go(string scene, string tipText, bool autoStartStory)
    {
        if (IsRunning || string.IsNullOrEmpty(scene)) return;
        GameObject go = new GameObject("StageTransition");
        DontDestroyOnLoad(go);
        StageTransition t = go.AddComponent<StageTransition>();
        t.StartCoroutine(t.Run(scene, tipText, autoStartStory));
    }

    IEnumerator Run(string scene, string tipText, bool autoStartStory)
    {
        IsRunning = true;
        Build(scene, tipText);

        // 1. 3초 동안 서서히 어둡게 (메뉴에서 눌렀을 땐 게임이 멈춰 있으니 unscaled 시간)
        yield return Fade(0f, 1f, FadeOutTime, SetBackground);

        // 2. 어두운 화면 뒤에서 다음 씬 불러오면서 스테이지 이름 + 팁 4초
        AsyncOperation load = SceneManager.LoadSceneAsync(scene);
        float shown = 0f;
        while (shown < TipTime || !load.isDone)
        {
            float dt = Step();
            shown += dt;
            AnimateContent(shown);
            AnimateMotes(dt, 1f);
            // 로딩 바: 팁 시간과 실제 로딩 중 느린 쪽을 따라감
            float progress = Mathf.Min(shown / TipTime, load.isDone ? 1f : load.progress / 0.9f);
            barFill.fillAmount = Mathf.Lerp(barFill.fillAmount, progress, 1f - Mathf.Exp(-12f * dt));
            yield return null;
        }
        content.alpha = 0f;

        // 3. 바로 페이드인 (빛 알갱이도 같이 사라짐)
        yield return Fade(1f, 0f, FadeInTime, a => { SetBackground(a); AnimateMotes(Step(), a); });

        IsRunning = false;
        BirdController bird = FindAnyObjectByType<BirdController>();
        if (bird != null)
        {
            if (autoStartStory) bird.StartStory();                                     // 보스 클리어 후: 바로 이어서 스토리
            else if (bird.startMenuPanel != null) bird.startMenuPanel.SetActive(true);  // 메뉴 버튼으로 왔을 땐 메뉴부터
        }
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        IsRunning = false;
        foreach (Object o in generated) if (o != null) Destroy(o);
    }

    void SetBackground(float a) { background.color = new Color(1f, 1f, 1f, a); }

    // 처음 0.5초 동안 위아래에서 미끄러지며 나타나고, 4초 끝나기 0.4초 전부터 사라짐 (카드는 0.15초 늦게)
    void AnimateContent(float t)
    {
        float appear = EaseOut(Mathf.Clamp01(t / 0.5f));
        float cardAppear = EaseOut(Mathf.Clamp01((t - 0.15f) / 0.5f));
        float leave = Mathf.Clamp01((TipTime - t) / 0.4f);
        content.alpha = Mathf.Min(appear, leave);
        header.anchoredPosition = new Vector2(0f, 150f + (1f - appear) * 24f);
        card.anchoredPosition = new Vector2(0f, -40f - (1f - cardAppear) * 24f);
        float s = Mathf.Lerp(0.96f, 1f, cardAppear);
        card.localScale = new Vector3(s, s, 1f);
    }

    // 반딧불처럼 천천히 떠오르며 깜빡이는 빛 알갱이
    void AnimateMotes(float dt, float visibility)
    {
        foreach (Mote m in motes)
        {
            m.phase += dt;
            Vector2 p = m.rt.anchoredPosition;
            p.y += m.speed * dt;
            p.x += Mathf.Sin(m.phase * 0.8f) * m.sway * dt;
            if (p.y > 380f) p = new Vector2(Random.Range(-660f, 660f), -380f);
            m.rt.anchoredPosition = p;
            float flicker = 0.6f + 0.4f * Mathf.Sin(m.phase * 2.3f);
            Color c = m.img.color;
            c.a = m.alpha * flicker * background.color.a * visibility;
            m.img.color = c;
        }
    }

    IEnumerator Fade(float from, float to, float duration, System.Action<float> apply)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Step();
            apply(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        apply(to);
    }

    // 씬 로딩 등으로 한 프레임이 멈추면 그 시간이 한꺼번에 더해져 페이드/팁이 건너뛰어지므로, 한 프레임에 최대 0.05초만 셈
    static float Step() { return Mathf.Min(Time.unscaledDeltaTime, 0.05f); }

    static float EaseOut(float x) { return 1f - (1f - x) * (1f - x) * (1f - x); }

    // ───────────────────────── 화면 만들기 ─────────────────────────

    // 모든 UI 위를 덮는 배경 + 빛 알갱이 + 스테이지 이름 + 팁 카드 + 로딩 바
    void Build(string scene, string tipText)
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>(); // 페이드 중 뒤의 버튼이 눌리지 않게 막음

        // 게임 기본 글꼴엔 한글이 없어서, 윈도우에 깔린 한글 글꼴을 사용 (게임에 포함하지 않아도 됨)
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 34);

        Sprite rounded = RoundedSprite();
        Sprite dot = SoftDotSprite();

        background = NewChild<RawImage>("Background", transform);
        background.texture = RadialTexture();
        background.color = new Color(1f, 1f, 1f, 0f);
        background.raycastTarget = true;
        Stretch(background.rectTransform);

        for (int i = 0; i < MoteCount; i++)
        {
            Mote m = new Mote();
            m.img = NewChild<Image>("Mote", transform);
            m.img.sprite = dot;
            m.img.raycastTarget = false;
            m.img.color = new Color(Gold.r, Gold.g, Gold.b, 0f);
            m.rt = m.img.rectTransform;
            float size = Random.Range(6f, 18f);
            m.rt.sizeDelta = new Vector2(size, size);
            m.rt.anchoredPosition = new Vector2(Random.Range(-660f, 660f), Random.Range(-380f, 380f));
            m.speed = Random.Range(12f, 36f);
            m.sway = Random.Range(6f, 22f);
            m.phase = Random.Range(0f, 10f);
            m.alpha = Random.Range(0.15f, 0.5f);
            motes.Add(m);
        }

        RectTransform root = NewRect("Content", transform);
        Stretch(root);
        content = root.gameObject.AddComponent<CanvasGroup>();
        content.alpha = 0f;
        content.blocksRaycasts = false;

        BuildHeader(root, scene);
        BuildCard(root, tipText, rounded);
        BuildLoadingBar(root, rounded);
    }

    // "NEXT STAGE" / "STAGE 2" / ─◆─ / "DESERT" (씬 이름이 Stage2_Desert 꼴일 때만 번호·이름 표시)
    void BuildHeader(RectTransform root, string scene)
    {
        header = NewRect("Header", root);
        header.sizeDelta = new Vector2(900f, 170f);

        Match m = Regex.Match(scene, @"^Stage(\d+)_?(.*)$", RegexOptions.IgnoreCase);
        string number = m.Success ? "STAGE " + m.Groups[1].Value : "NEXT STAGE";
        // Stage3_IceCave → "ICE CAVE" (붙어 있는 단어·밑줄은 띄어 씀)
        string raw = m.Success ? Regex.Replace(m.Groups[2].Value.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2") : "";
        string name = Spaced(raw.ToUpperInvariant());

        if (m.Success) Label(header, "Kicker", "N E X T   S T A G E", 18, Gold, new Vector2(0f, 62f), 40f, false);
        Text title = Label(header, "Title", number, 64, Cream, new Vector2(0f, 14f), 80f, true);
        title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);

        // 가운데 마름모를 두고 양옆으로 가늘어지는 금색 줄
        for (int side = -1; side <= 1; side += 2)
        {
            Image line = NewChild<Image>("Line", header);
            line.color = new Color(Gold.r, Gold.g, Gold.b, 0.7f);
            line.raycastTarget = false;
            line.rectTransform.sizeDelta = new Vector2(120f, 2f);
            line.rectTransform.anchoredPosition = new Vector2(side * 76f, -32f);
        }
        Image diamond = NewChild<Image>("Diamond", header);
        diamond.color = Gold;
        diamond.raycastTarget = false;
        diamond.rectTransform.sizeDelta = new Vector2(9f, 9f);
        diamond.rectTransform.anchoredPosition = new Vector2(0f, -32f);
        diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        if (name.Length > 0) Label(header, "Name", name, 24, new Color(Cream.r, Cream.g, Cream.b, 0.85f), new Vector2(0f, -66f), 40f, true);
    }

    // 금색 테두리의 남색 둥근 카드, 왼쪽에 TIP 배지, 오른쪽에 팁 문장
    void BuildCard(RectTransform root, string tipText, Sprite rounded)
    {
        card = NewRect("TipCard", root);
        card.sizeDelta = new Vector2(820f, 150f);

        Image border = NewChild<Image>("Border", card);
        Sliced(border, rounded, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 1f);
        Stretch(border.rectTransform);

        Image panel = NewChild<Image>("Panel", card);
        Sliced(panel, rounded, new Color(Navy.r, Navy.g, Navy.b, 0.92f), 1f);
        Stretch(panel.rectTransform);
        panel.rectTransform.offsetMin = new Vector2(2f, 2f);
        panel.rectTransform.offsetMax = new Vector2(-2f, -2f);

        Image badge = NewChild<Image>("Badge", card);
        Sliced(badge, rounded, Gold, 1.5f);
        badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        badge.rectTransform.sizeDelta = new Vector2(84f, 36f);
        badge.rectTransform.anchoredPosition = new Vector2(70f, 0f);
        Text badgeText = Label(badge.rectTransform, "Label", "TIP", 20, Navy, Vector2.zero, 36f, true);
        badgeText.rectTransform.sizeDelta = new Vector2(84f, 36f);

        Text body = Label(card, "Text", CleanTip(tipText), 27, Cream, Vector2.zero, 0f, false);
        RectTransform rt = body.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(132f, 18f);
        rt.offsetMax = new Vector2(-36f, -18f);
        body.alignment = TextAnchor.MiddleLeft;
        body.lineSpacing = 1.25f;
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.resizeTextForBestFit = true;
        body.resizeTextMinSize = 18;
        body.resizeTextMaxSize = 27;
    }

    // 화면 아래 가는 로딩 바 + "LOADING"
    void BuildLoadingBar(RectTransform root, Sprite rounded)
    {
        RectTransform bar = NewRect("LoadingBar", root);
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(360f, 6f);
        bar.anchoredPosition = new Vector2(0f, 70f);

        Image track = NewChild<Image>("Track", bar);
        Sliced(track, rounded, new Color(1f, 1f, 1f, 0.12f), 8f);
        Stretch(track.rectTransform);

        barFill = NewChild<Image>("Fill", bar);
        barFill.sprite = rounded;
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.fillAmount = 0f;
        barFill.color = Gold;
        barFill.raycastTarget = false;
        Stretch(barFill.rectTransform);

        Label(bar, "Loading", "L O A D I N G", 14, new Color(Cream.r, Cream.g, Cream.b, 0.6f), new Vector2(0f, 22f), 24f, false);
    }

    // "tip. 내용" → "내용" (TIP 배지가 따로 있으니 앞의 tip. 은 뺌)
    static string CleanTip(string tip)
    {
        if (string.IsNullOrEmpty(tip)) return "";
        return Regex.Replace(tip.Trim(), @"^tip\s*[.:]\s*", "", RegexOptions.IgnoreCase);
    }

    static string Spaced(string s) { return string.Join(" ", s.ToCharArray()); }

    Text Label(RectTransform parent, string name, string text, int size, Color color, Vector2 pos, float height, bool bold)
    {
        Text t = NewChild<Text>(name, parent);
        t.font = font;
        t.fontSize = size;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = text;
        t.color = color;
        t.raycastTarget = false;
        t.rectTransform.sizeDelta = new Vector2(900f, height);
        t.rectTransform.anchoredPosition = pos;
        return t;
    }

    static void Sliced(Image i, Sprite sprite, Color color, float ppu)
    {
        i.sprite = sprite;
        i.type = Image.Type.Sliced;
        i.pixelsPerUnitMultiplier = ppu;
        i.color = color;
        i.raycastTarget = false;
    }

    T NewChild<T>(string name, Transform parent) where T : Graphic
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return g.AddComponent<T>();
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ───────────────────────── 코드로 만드는 그림 ─────────────────────────

    Texture2D NewTexture(int w, int h)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        generated.Add(tex);
        return tex;
    }

    // 가장자리가 부드러운 둥근 사각형 (9-slice로 늘려 씀)
    Sprite RoundedSprite()
    {
        const int size = 64, radius = 24;
        Texture2D tex = NewTexture(size, size);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d + 0.5f));
            }
        tex.SetPixels(px);
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        generated.Add(s);
        return s;
    }

    // 가운데가 밝고 바깥으로 흐려지는 빛 알갱이
    Sprite SoftDotSprite()
    {
        const int size = 32;
        Texture2D tex = NewTexture(size, size);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                px[y * size + x] = new Color(1f, 1f, 1f, a * a);
            }
        tex.SetPixels(px);
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        generated.Add(s);
        return s;
    }

    // 가운데는 깊은 밤하늘 남색, 가장자리로 갈수록 검정 (비네트)
    Texture2D RadialTexture()
    {
        const int size = 128;
        Texture2D tex = NewTexture(size, size);
        Color center = new Color(0.10f, 0.12f, 0.24f);
        Color edge = new Color(0.01f, 0.01f, 0.03f);
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.55f)) / (size * 0.62f);
                px[y * size + x] = Color.Lerp(center, edge, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d)));
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
