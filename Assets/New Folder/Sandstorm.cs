using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// 🌪 모래 바람: 정해진 거리 구간들(기본 300m~350m, 400m~450m)에서만
// - 화면: 흐릿한 모래 줄기(멀고 가까운 3겹) + 흘러가는 먼지 구름 + 노란 테두리, 돌풍이 불 때마다 더 짙어짐
// - 세상: 모래 안개가 몰려와 먼 벽이 안 보이고 가까이 와야 보임
// - 새: 바람에 왼쪽으로 조금씩 밀림
// 점수판 등 UI보다 아래에 그려짐
public class Sandstorm : MonoBehaviour
{
    public BirdController bird;
    // 구간마다 x = 시작 거리, y = 끝 거리 (m)
    public Vector2[] zones = { new Vector2(300f, 350f), new Vector2(400f, 450f) };
    public float rampMeters = 8f;            // 구간 시작/끝에서 서서히 세지고 약해지는 거리

    [Header("돌풍 (세기가 오르내림)")]
    [Range(0f, 1f)] public float calmLevel = 0.55f; // 돌풍 사이 가장 약할 때
    public float gustSpeed = 0.45f;                  // 클수록 돌풍이 자주 옴

    [Header("모래 입자")]
    public int particleCount = 260;
    public Color sandColor = new Color(0.93f, 0.78f, 0.48f);
    public Vector2 speedRange = new Vector2(700f, 2000f); // 화면 픽셀/초 (오른쪽 → 왼쪽), 가까운 입자일수록 빠름

    [Header("먼지 구름 / 색")]
    [Range(0f, 1f)] public float hazeAlpha = 0.5f;   // 돌풍 최고일 때 먼지 구름 진하기
    [Range(0f, 1f)] public float tintAlpha = 0.18f;  // 화면 전체를 모래색으로 물들이는 정도
    public Color edgeColor = new Color(1f, 0.80f, 0.30f);
    [Range(0f, 1f)] public float edgeAlpha = 0.7f;

    [Header("모래 안개 (3D 세상)")]
    public float stormFogStart = 4f;
    public Vector2 stormFogEnd = new Vector2(75f, 38f); // 돌풍 약할 때 / 셀 때 이 거리 너머는 안 보임
    public Color stormFogColor = new Color(0.86f, 0.70f, 0.45f);

    [Header("바람에 밀림")]
    public float windPush = 3f;              // 돌풍 최고일 때 왼쪽으로 미는 속도 (m/s)

    private Canvas canvas;
    private RectTransform area;
    private Image edge, tint;
    private RawImage[] haze;
    private CanvasGroup grainGroup;
    private readonly List<RectTransform> grains = new List<RectTransform>();
    private float[] speed, drift;
    private float intensity;

    private bool fogSaved;
    private bool origFog;
    private FogMode origFogMode;
    private Color origFogColor;
    private float origFogStart, origFogEnd;

    void Start()
    {
        if (bird == null) bird = FindAnyObjectByType<BirdController>();
        Build();
        canvas.enabled = false;
    }

    void Update()
    {
        // 구간 안에서 1, 바깥에서 0, 경계에서는 rampMeters 동안 부드럽게
        float z = bird != null ? bird.transform.position.z : 0f;
        float target = 0f;
        // 보스전 중엔 안 붐 (메뉴의 보스 바로 하기는 0m에서 시작해서 구간을 지나감)
        if (bird == null || !bird.BossStarted)
            foreach (Vector2 zone in zones)
                target = Mathf.Max(target, Mathf.Clamp01(Mathf.Min((z - zone.x) / rampMeters, (zone.y - z) / rampMeters)));
        intensity = Mathf.MoveTowards(intensity, target, Time.deltaTime * 2f);

        if (intensity <= 0.001f)
        {
            if (canvas.enabled) { canvas.enabled = false; RestoreFog(); if (bird != null) bird.windX = 0f; }
            return;
        }
        canvas.enabled = true;

        // 돌풍: 천천히 출렁이는 잡음으로 calmLevel ~ 1 사이를 오감
        float gust = Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(Time.time * gustSpeed, 0.37f));
        float level = intensity * Mathf.Lerp(calmLevel, 1f, gust);

        edge.color = new Color(edgeColor.r, edgeColor.g, edgeColor.b, edgeAlpha * level);
        tint.color = new Color(sandColor.r, sandColor.g, sandColor.b, tintAlpha * level);
        grainGroup.alpha = Mathf.Lerp(0.5f, 1f, level) * intensity;

        // 먼지 구름: 두 겹이 서로 다른 빠르기로 흘러감 (가까운 겹이 더 빠르고 진함)
        for (int i = 0; i < haze.Length; i++)
        {
            Rect uv = haze[i].uvRect;
            uv.x += Time.deltaTime * (0.10f + i * 0.12f) * (0.6f + level);
            uv.y = Mathf.Sin(Time.time * (0.3f + i * 0.2f)) * 0.03f;
            haze[i].uvRect = uv;
            haze[i].color = new Color(sandColor.r, sandColor.g, sandColor.b, hazeAlpha * level * (i == 0 ? 0.7f : 1f));
        }

        Rect r = area.rect;
        float gustSpeedUp = 0.7f + level * 0.6f;
        for (int i = 0; i < grains.Count; i++)
        {
            RectTransform g = grains[i];
            Vector2 p = g.anchoredPosition + new Vector2(-speed[i] * gustSpeedUp, drift[i]) * Time.deltaTime;
            if (p.x < r.xMin - g.sizeDelta.x) p = new Vector2(r.xMax + Random.Range(0f, 300f), Random.Range(r.yMin, r.yMax)); // 왼쪽으로 나가면 오른쪽에서 다시
            g.anchoredPosition = p;
        }

        ApplyFog(gust);
        if (bird != null) bird.windX = -windPush * level;
    }

    void ApplyFog(float gust)
    {
        if (!fogSaved)
        {
            fogSaved = true;
            origFog = RenderSettings.fog;
            origFogMode = RenderSettings.fogMode;
            origFogColor = RenderSettings.fogColor;
            origFogStart = RenderSettings.fogStartDistance;
            origFogEnd = RenderSettings.fogEndDistance;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        float t = Mathf.SmoothStep(0f, 1f, intensity);
        float stormEnd = Mathf.Lerp(stormFogEnd.x, stormFogEnd.y, gust);
        RenderSettings.fogColor = Color.Lerp(origFogColor, stormFogColor, t);
        RenderSettings.fogStartDistance = Mathf.Lerp(origFogStart, stormFogStart, t);
        RenderSettings.fogEndDistance = Mathf.Lerp(origFogEnd, stormEnd, t);
    }

    void RestoreFog()
    {
        if (!fogSaved) return;
        fogSaved = false;
        RenderSettings.fog = origFog;
        RenderSettings.fogMode = origFogMode;
        RenderSettings.fogColor = origFogColor;
        RenderSettings.fogStartDistance = origFogStart;
        RenderSettings.fogEndDistance = origFogEnd;
    }

    void OnDisable()
    {
        RestoreFog();
        if (bird != null) bird.windX = 0f;
    }

    void Build()
    {
        GameObject root = new GameObject("SandstormOverlay", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -10; // 게임 UI(0)보다 아래 → 점수·체력바는 가리지 않음
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        area = (RectTransform)root.transform;

        // 뒤에서부터: 모래색 물들임 → 먼 먼지 구름 → 모래 줄기 → 가까운 먼지 구름 → 노란 테두리
        tint = NewImage("Tint", area);
        Stretch(tint.rectTransform);

        Texture2D cloud = MakeCloudTexture();
        haze = new RawImage[2];
        haze[0] = NewHaze("HazeFar", cloud, new Rect(0f, 0f, 1.6f, 1f));

        GameObject grainRoot = new GameObject("Grains", typeof(RectTransform));
        grainRoot.transform.SetParent(area, false);
        Stretch((RectTransform)grainRoot.transform);
        grainGroup = grainRoot.AddComponent<CanvasGroup>();
        grainGroup.blocksRaycasts = false;
        grainGroup.interactable = false;

        haze[1] = NewHaze("HazeNear", cloud, new Rect(0.37f, 0.2f, 0.9f, 0.6f));

        edge = NewImage("Edge", area);
        edge.sprite = MakeVignette();
        Stretch(edge.rectTransform);

        // 모래 줄기: 부드러운 꼬리가 달린 흐릿한 선. depth 0 = 멀고 작고 느리고 옅음, 1 = 가깝고 크고 빠르고 진함
        Sprite streak = MakeStreak();
        speed = new float[particleCount];
        drift = new float[particleCount];
        Rect rect = new Rect(-640, -360, 1280, 720);
        for (int i = 0; i < particleCount; i++)
        {
            float d = Mathf.Pow(Random.value, 1.6f); // 먼 입자가 더 많음
            Image img = NewImage("Grain", grainRoot.transform);
            img.sprite = streak;
            Color c = Color.Lerp(sandColor, sandColor * 0.8f, Random.value);
            c.a = Mathf.Lerp(0.18f, 0.6f, d);
            img.color = c;
            RectTransform g = img.rectTransform;
            g.anchorMin = g.anchorMax = new Vector2(0.5f, 0.5f);
            float len = Mathf.Lerp(30f, 160f, d) * Random.Range(0.7f, 1.3f);
            float thick = Mathf.Lerp(3f, 14f, d) * Random.Range(0.8f, 1.2f);
            g.sizeDelta = new Vector2(len, thick);
            g.localRotation = Quaternion.Euler(0, 0, Random.Range(-7f, 3f));
            g.anchoredPosition = new Vector2(Random.Range(rect.xMin, rect.xMax), Random.Range(rect.yMin, rect.yMax));
            speed[i] = Mathf.Lerp(speedRange.x, speedRange.y, d) * Random.Range(0.85f, 1.15f);
            drift[i] = Random.Range(-70f, 15f) * Mathf.Lerp(0.5f, 1.2f, d); // 살짝 아래로 흩날림
            grains.Add(g);
        }
    }

    RawImage NewHaze(string name, Texture2D tex, Rect uv)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(area, false);
        RawImage img = g.AddComponent<RawImage>();
        img.texture = tex;
        img.uvRect = uv;
        img.raycastTarget = false;
        Stretch(img.rectTransform);
        return img;
    }

    // 모래 줄기 한 가닥: 왼쪽(날아가는 쪽) 머리가 진하고 오른쪽으로 꼬리가 옅어짐, 위아래는 흐릿하게
    static Sprite MakeStreak()
    {
        const int w = 64, h = 16;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                float v = (y + 0.5f) / h * 2f - 1f;
                float head = Mathf.SmoothStep(0f, 1f, u / 0.15f);          // 머리 끝은 둥글게
                float tail = Mathf.Pow(1f - u, 1.3f);                      // 꼬리는 서서히 사라짐
                float across = Mathf.Exp(-v * v * 4f);                     // 가운데 진하고 가장자리 흐림
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, head * tail * across));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }

    // 먼지 구름: 가로로 이어 붙여도 이음새 없는 뭉게뭉게 잡음 (흰색 + 알파)
    static Texture2D MakeCloudTexture()
    {
        const int w = 256, h = 128;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        float seed = Random.Range(0f, 100f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                // 가로 이음새 없애기: 오른쪽 끝 값과 왼쪽 끝 값을 섞음
                float n = Mathf.Lerp(Fbm(u, v, seed), Fbm(u - 1f, v, seed), u);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, n));
                a *= 0.55f + 0.45f * Mathf.Sin(v * Mathf.PI); // 위아래 가장자리는 조금 옅게
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    static float Fbm(float u, float v, float seed)
    {
        float sum = 0f, amp = 0.5f, f = 3f;
        for (int o = 0; o < 4; o++)
        {
            sum += amp * Mathf.PerlinNoise(seed + u * f, seed * 0.5f + v * f * 0.6f);
            amp *= 0.5f; f *= 2f;
        }
        return sum / 0.9375f;
    }

    Sprite MakeVignette()
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 화면 비율과 상관없이 테두리 쪽으로 진해지도록 (가로·세로 각각 0~1)
                float dx = Mathf.Abs(x / (float)(size - 1) * 2f - 1f);
                float dy = Mathf.Abs(y / (float)(size - 1) * 2f - 1f);
                float d = Mathf.Max(Mathf.Pow(dx, 2.5f), Mathf.Pow(dy, 2.5f)) * 0.6f + new Vector2(dx, dy).sqrMagnitude * 0.25f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.85f, d));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static Image NewImage(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        Image img = g.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
