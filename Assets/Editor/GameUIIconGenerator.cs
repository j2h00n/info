using UnityEngine;
using UnityEditor;
using System.IO;

// 게임 분위기(노을 지는 로우폴리 숲)에 맞춘 UI 아이콘/패널 스프라이트를 코드로 그려서 PNG로 저장
// 메뉴: Tools > Generate Game UI Icons
public static class GameUIIconGenerator
{
    const string OutDir = "Assets/UI/Icons";

    // 아이콘은 흰색으로 그려서 Image.color로 색을 입힘. 패널만 색을 직접 구움
    static readonly Color PanelFill = new Color32(0x1C, 0x21, 0x3F, 0xE6);   // 밤하늘 남색
    static readonly Color PanelBorder = new Color32(0xF6, 0xE7, 0xC1, 0xFF); // 별빛 크림색

    delegate float Sdf(Vector2 p); // 0~1 좌표(아래가 0), 음수 = 도형 안

    [MenuItem("Tools/Generate Game UI Icons")]
    public static void Generate()
    {
        Directory.CreateDirectory(OutDir);

        // ── 아이콘 (128px) ──
        SaveIcon("Icon_Play", p => Round(Poly(p, V(0.34f, 0.22f), V(0.34f, 0.78f), V(0.80f, 0.5f)), 0.04f));

        SaveIcon("Icon_Restart", p =>
        {
            Vector2 c = V(0.5f, 0.5f); float r = 0.27f, a0 = 30f;
            float arc = Arc(p, c, r, 0.10f, a0, 300f);
            float t = a0 * Mathf.Deg2Rad;
            Vector2 n = new Vector2(Mathf.Cos(t), Mathf.Sin(t)), tan = new Vector2(Mathf.Sin(t), -Mathf.Cos(t));
            Vector2 at = c + n * r;
            float head = Round(Poly(p, at + n * 0.13f, at + tan * 0.15f, at - n * 0.13f), 0.015f);
            return Mathf.Min(arc, head);
        });

        SaveIcon("Icon_Music", p => Min(
            Circle(p, V(0.33f, 0.28f), 0.11f),
            Circle(p, V(0.69f, 0.36f), 0.11f),
            Box(p, V(0.405f, 0.56f), V(0.035f, 0.28f), 0.01f),
            Box(p, V(0.765f, 0.64f), V(0.035f, 0.28f), 0.01f),
            Poly(p, V(0.37f, 0.72f), V(0.80f, 0.80f), V(0.80f, 0.93f), V(0.37f, 0.85f))));

        SaveIcon("Icon_Sound", p => Min(
            Box(p, V(0.24f, 0.5f), V(0.08f, 0.11f), 0.02f),
            Round(Poly(p, V(0.28f, 0.39f), V(0.50f, 0.20f), V(0.50f, 0.80f), V(0.28f, 0.61f)), 0.02f),
            Arc(p, V(0.50f, 0.5f), 0.17f, 0.07f, -45f, 45f),
            Arc(p, V(0.50f, 0.5f), 0.31f, 0.07f, -45f, 45f)));

        SaveIcon("Icon_Trophy", p =>
        {
            float cup = Mathf.Min(Box(p, V(0.5f, 0.68f), V(0.22f, 0.14f), 0.03f), Circle(p, V(0.5f, 0.58f), 0.22f));
            float handles = Mathf.Min(Ring(p, V(0.26f, 0.66f), 0.10f, 0.055f), Ring(p, V(0.74f, 0.66f), 0.10f, 0.055f));
            float star = Poly(p, Star(V(0.5f, 0.63f), 0.11f, 0.045f));
            float body = Min(cup, handles, Box(p, V(0.5f, 0.30f), V(0.05f, 0.08f), 0f), Box(p, V(0.5f, 0.17f), V(0.18f, 0.05f), 0.02f));
            return Mathf.Max(body, -star); // 컵에 별 모양 구멍
        });

        // 로우폴리 바위 3개를 쌓은 벽 아이콘 (면을 나누는 가는 틈으로 각진 느낌)
        SaveIcon("Icon_Wall", p =>
        {
            float a = Poly(p, V(0.10f, 0.20f), V(0.46f, 0.16f), V(0.52f, 0.30f), V(0.45f, 0.47f), V(0.20f, 0.49f), V(0.08f, 0.35f)) + 0.012f;
            float b = Poly(p, V(0.55f, 0.16f), V(0.90f, 0.19f), V(0.93f, 0.36f), V(0.82f, 0.49f), V(0.58f, 0.47f), V(0.53f, 0.31f)) + 0.012f;
            float c = Poly(p, V(0.28f, 0.54f), V(0.66f, 0.53f), V(0.77f, 0.67f), V(0.65f, 0.84f), V(0.40f, 0.86f), V(0.25f, 0.71f)) + 0.012f;
            float rocks = Min(a, b, c);
            float facets = Min(Seg(p, V(0.20f, 0.49f), V(0.30f, 0.30f), 0.022f), Seg(p, V(0.30f, 0.30f), V(0.46f, 0.16f), 0.022f),
                               Seg(p, V(0.82f, 0.49f), V(0.72f, 0.30f), 0.022f), Seg(p, V(0.72f, 0.30f), V(0.90f, 0.19f), 0.022f),
                               Seg(p, V(0.40f, 0.86f), V(0.50f, 0.66f), 0.022f), Seg(p, V(0.50f, 0.66f), V(0.77f, 0.67f), 0.022f));
            return Mathf.Max(rocks, -facets);
        });

        // 갈매기 (점수 = 날아간 거리)
        // 갈매기: 몸통 쪽은 두껍고 날개 끝은 뾰족하게 가늘어지는 곡선 두 개
        SaveIcon("Icon_Seagull", Gull);

        // 얼굴 인식 모션 조작
        SaveIcon("Icon_Motion", p =>
        {
            float head = Min(Ring(p, V(0.5f, 0.5f), 0.22f, 0.06f), Circle(p, V(0.42f, 0.55f), 0.035f), Circle(p, V(0.58f, 0.55f), 0.035f),
                             Arc(p, V(0.5f, 0.50f), 0.11f, 0.045f, 205f, 335f));
            float k = 0.05f, lo = 0.10f, hi = 0.90f, len = 0.16f;
            float corners = Min(Seg(p, V(lo, hi), V(lo + len, hi), k), Seg(p, V(lo, hi), V(lo, hi - len), k),
                                Seg(p, V(hi, hi), V(hi - len, hi), k), Seg(p, V(hi, hi), V(hi, hi - len), k),
                                Seg(p, V(lo, lo), V(lo + len, lo), k), Seg(p, V(lo, lo), V(lo, lo + len), k),
                                Seg(p, V(hi, lo), V(hi - len, lo), k), Seg(p, V(hi, lo), V(hi, lo + len), k));
            return Mathf.Min(head, corners);
        });

        // 스토리 모드: 바람에 날리는 목표 깃발
        SaveIcon("Icon_Flag", p =>
        {
            const int n = 16;
            var pts = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float x = Mathf.Lerp(0.31f, 0.84f, t);
                float wave = Mathf.Sin(t * Mathf.PI * 2f) * 0.035f;
                pts[i] = V(x, 0.87f + wave - t * 0.04f);              // 윗변 (왼→오)
                pts[n * 2 - 1 - i] = V(x, 0.55f + wave - t * 0.04f);  // 아랫변 (오→왼)
            }
            return Mathf.Min(Seg(p, V(0.27f, 0.10f), V(0.27f, 0.90f), 0.075f), Round(Poly(p, pts), 0.015f));
        });

        // 엔드리스 모드: 무한(∞) — 8자 곡선을 촘촘히 찍어서 선 굵기만큼 칠함
        SaveIcon("Icon_Infinity", p =>
        {
            float best = float.MaxValue;
            for (int i = 0; i < 160; i++)
            {
                float t = i / 160f * Mathf.PI * 2f;
                float s = Mathf.Sin(t), c = Mathf.Cos(t), k = 1f + s * s;
                Vector2 q = V(0.5f + 0.36f * c / k, 0.5f + 0.36f * s * c / k); // 베르누이 렘니스케이트
                best = Mathf.Min(best, (p - q).magnitude);
            }
            return best - 0.05f;
        });

        // 보스 체력바: S자 뱀 (꼬리는 가늘게, 머리에 눈 구멍)
        SaveIcon("Icon_Snake", p =>
        {
            float upper = Arc(p, V(0.5f, 0.66f), 0.16f, 0.11f, 30f, 270f);
            float lower = TaperArc(p, V(0.5f, 0.34f), 0.16f, 90f, -150f, 0.11f, 0.03f);
            float headBall = Circle(p, V(0.66f, 0.77f), 0.095f);
            float eye = Circle(p, V(0.68f, 0.80f), 0.025f);
            float tongue = Mathf.Min(Seg(p, V(0.74f, 0.83f), V(0.84f, 0.88f), 0.025f), Seg(p, V(0.84f, 0.88f), V(0.88f, 0.84f), 0.02f));
            return Mathf.Min(Mathf.Max(Min(upper, lower, headBall), -eye), tongue);
        });

        SaveIcon("Icon_Check", p => Mathf.Min(Seg(p, V(0.22f, 0.52f), V(0.42f, 0.30f), 0.13f), Seg(p, V(0.42f, 0.30f), V(0.80f, 0.74f), 0.13f)));

        // 사막 보스: 독수리 옆얼굴 (둥근 머리 + 아래로 휜 갈고리 부리 + 눈 구멍, 목깃은 뾰족뾰족)
        SaveIcon("Icon_Eagle", p =>
        {
            float headD = Circle(p, V(0.42f, 0.58f), 0.22f);
            float beak = Round(Poly(p, V(0.58f, 0.70f), V(0.80f, 0.64f), V(0.90f, 0.50f), V(0.84f, 0.40f), V(0.80f, 0.50f), V(0.60f, 0.50f)), 0.015f);
            float neck = Poly(p, V(0.24f, 0.50f), V(0.56f, 0.46f), V(0.52f, 0.12f), V(0.44f, 0.24f), V(0.36f, 0.10f), V(0.30f, 0.24f), V(0.20f, 0.14f));
            float eye = Circle(p, V(0.53f, 0.63f), 0.045f);
            float mouth = Seg(p, V(0.62f, 0.55f), V(0.78f, 0.55f), 0.02f);
            return Mathf.Max(Min(headD, beak, neck), -Mathf.Min(eye, mouth));
        });

        HomeIcon();
        SnowflakeIcon();

        // 펼치기 화살표 (아래 방향 V, 펼치면 뒤집어서 위로)
        SaveIcon("Icon_Arrow", p => Mathf.Min(Seg(p, V(0.22f, 0.62f), V(0.5f, 0.34f), 0.12f), Seg(p, V(0.5f, 0.34f), V(0.78f, 0.62f), 0.12f)));

        // 동그라미 버튼 (BGM 설정): 패널처럼 남색 + 크림색 테두리
        SaveSprite("UI_Circle", 128, 0, (p, px) =>
        {
            float outer = Circle(p, V(0.5f, 0.5f), 0.5f - 2f * px);
            float inner = outer + 6f * px;
            Color c = Color.Lerp(PanelBorder, PanelFill, Cov(inner, px));
            c.a *= Cov(outer, px);
            return c;
        });

        // ── 9-slice UI 판 ──
        // 패널: 남색 + 크림색 테두리
        SaveSprite("UI_Panel", 96, 28, (p, px) =>
        {
            float outer = Box(p, V(0.5f, 0.5f), V(0.5f - 1.5f * px, 0.5f - 1.5f * px), 24f * px);
            float inner = outer + 4f * px;
            Color c = Color.Lerp(PanelBorder, PanelFill, Cov(inner, px));
            c.a *= Cov(outer, px);
            return c;
        });

        // 버튼: 둥근 알약 + 아래쪽 그림자 띠로 살짝 입체감 (흰색 → Image.color로 색칠)
        SaveSprite("UI_Button", 96, 30, (p, px) =>
        {
            float shape = Box(p, V(0.5f, 0.5f), V(0.5f - 1.5f * px, 0.5f - 1.5f * px), 28f * px);
            float top = Box(p, V(0.5f, 0.5f + 3f * px), V(0.5f - 1.5f * px, 0.5f - 4.5f * px), 28f * px);
            float shade = Mathf.Lerp(0.72f, 1f, Cov(top, px));
            return new Color(shade, shade, shade, Cov(shape, px));
        });

        SaveSprite("UI_Track", 32, 15, (p, px) => new Color(1, 1, 1, Cov(Box(p, V(0.5f, 0.5f), V(0.5f - px, 0.5f - px), 15f * px), px)));

        // 슬라이더 손잡이: 테두리 있는 동그라미
        SaveSprite("UI_Knob", 64, 0, (p, px) =>
        {
            float outer = Circle(p, V(0.5f, 0.5f), 0.5f - 2f * px);
            float inner = outer + 5f * px;
            float shade = Mathf.Lerp(0.55f, 1f, Cov(inner, px));
            return new Color(shade, shade, shade, Cov(outer, px));
        });

        SaveSprite("UI_CheckBox", 48, 14, (p, px) => new Color(1, 1, 1, Cov(Box(p, V(0.5f, 0.5f), V(0.5f - px, 0.5f - px), 12f * px), px)));

        AssetDatabase.Refresh();
        Debug.Log("UI 아이콘 생성 완료: " + OutDir);
    }

    // 실행 파일(exe) 아이콘: 노을 하늘 둥근 사각형 + 해 + 흰 갈매기 (512px), 플레이어 설정의 기본 아이콘으로 등록
    [MenuItem("Tools/Generate App Icon")]
    public static void GenerateAppIcon()
    {
        Directory.CreateDirectory(OutDir);
        Color skyTop = new Color32(0x2B, 0x35, 0x6E, 0xFF);    // 밤하늘 남색
        Color skyBottom = new Color32(0xF4, 0x9A, 0x5A, 0xFF); // 노을 주황
        Color sun = new Color32(0xFF, 0xD8, 0x8A, 0xFF);
        Color sea = new Color32(0x3A, 0x4E, 0x8C, 0xFF);
        Color gullShadow = new Color(0.10f, 0.08f, 0.20f);

        SaveSprite("App_Icon", 512, 0, (p, px) =>
        {
            float frame = Cov(Box(p, V(0.5f, 0.5f), V(0.46f, 0.46f), 0.12f), px);
            Color c = Color.Lerp(skyBottom, skyTop, Mathf.SmoothStep(0.25f, 1f, p.y));
            c = Color.Lerp(c, sun, Cov(Circle(p, V(0.5f, 0.27f), 0.15f), px));
            c = Color.Lerp(c, sea, Cov(p.y - 0.24f, px)); // 수평선 아래 바다 (해가 반쯤 잠김)
            // 갈매기 모양은 UI 아이콘과 같고, 조금 줄여서 위쪽 가운데로
            const float scale = 0.85f;
            Vector2 g = (p - V(0.5f, 0.62f)) / scale + V(0.5f, 0.47f);
            c = Color.Lerp(c, gullShadow, Cov(Gull(g + V(-0.012f, 0.018f)) * scale, px) * 0.35f); // 살짝 그림자
            c = Color.Lerp(c, Color.white, Cov(Gull(g) * scale, px));
            c.a = frame;
            return c;
        });

        string path = OutDir + "/App_Icon.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.SaveAndReimport();
        PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { AssetDatabase.LoadAssetAtPath<Texture2D>(path) }, IconKind.Any);
        AssetDatabase.SaveAssets();
        Debug.Log("앱 아이콘 생성 및 등록 완료: " + path);
    }

    // 메뉴로 돌아가기: 지붕 + 몸통에 문 구멍 난 집
    public static void HomeIcon()
    {
        Directory.CreateDirectory(OutDir);
        SaveIcon("Icon_Home", p =>
        {
            float roof = Round(Poly(p, V(0.14f, 0.52f), V(0.5f, 0.85f), V(0.86f, 0.52f)), 0.03f);
            float body = Box(p, V(0.5f, 0.35f), V(0.25f, 0.19f), 0.03f);
            float door = Box(p, V(0.5f, 0.24f), V(0.075f, 0.11f), 0.02f);
            return Mathf.Max(Mathf.Min(roof, body), -door);
        });
    }

    // 얼음 동굴 탈출 버튼: 여섯 갈래 눈송이 (갈래마다 작은 V 가지)
    public static void SnowflakeIcon()
    {
        Directory.CreateDirectory(OutDir);
        SaveIcon("Icon_Snowflake", p =>
        {
            Vector2 c = V(0.5f, 0.5f);
            float d = 1f;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f + Mathf.PI / 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                d = Mathf.Min(d, Seg(p, c, c + dir * 0.4f, 0.045f));
                for (int b = 0; b < 2; b++)
                {
                    Vector2 at = c + dir * (b == 0 ? 0.2f : 0.3f);
                    float len = b == 0 ? 0.11f : 0.08f;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        float ba = a + k * Mathf.PI / 4f;
                        d = Mathf.Min(d, Seg(p, at, at + new Vector2(Mathf.Cos(ba), Mathf.Sin(ba)) * len, 0.035f));
                    }
                }
            }
            return d;
        });
    }

    // 새총: Y자 나무 + 고무줄 + 돌멩이 (도시 보스 HUD·바로 하기 버튼)
    public static void SlingshotIcon()
    {
        Directory.CreateDirectory(OutDir);
        SaveIcon("Icon_Slingshot", p =>
        {
            Vector2 fork = V(0.5f, 0.46f), l = V(0.28f, 0.84f), r = V(0.72f, 0.84f), pouch = V(0.5f, 0.62f);
            float d = Seg(p, V(0.5f, 0.1f), fork, 0.09f);
            d = Mathf.Min(d, Seg(p, fork, l, 0.075f));
            d = Mathf.Min(d, Seg(p, fork, r, 0.075f));
            d = Mathf.Min(d, Seg(p, l, pouch, 0.025f));
            d = Mathf.Min(d, Seg(p, r, pouch, 0.025f));
            d = Mathf.Min(d, Circle(p, pouch, 0.065f));
            return d;
        });
    }

    static float Gull(Vector2 p)
    {
        return Mathf.Min(
            TaperArc(p, V(0.32f, 0.36f), 0.20f, 20f, 152f, 0.11f, 0.025f),
            TaperArc(p, V(0.68f, 0.36f), 0.20f, 160f, 28f, 0.11f, 0.025f));
    }

    // ───────── 저장 ─────────

    static void SaveIcon(string name, Sdf shape)
    {
        SaveSprite(name, 128, 0, (p, px) => new Color(1, 1, 1, Cov(shape(p), px)));
    }

    static void SaveSprite(string name, int size, int border, System.Func<Vector2, float, Color> shade)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float px = 1f / size;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = shade(new Vector2((x + 0.5f) * px, (y + 0.5f) * px), px);
        tex.SetPixels(pixels);
        tex.Apply();

        string path = OutDir + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.SaveAndReimport();
    }

    // ───────── 도형 거리 함수 (SDF) ─────────

    static Vector2 V(float x, float y) { return new Vector2(x, y); }

    // 거리 → 안티앨리어싱된 채움 정도
    static float Cov(float d, float px) { return Mathf.Clamp01(0.5f - d / px); }

    static float Min(params float[] d) { float m = float.MaxValue; foreach (float v in d) m = Mathf.Min(m, v); return m; }

    static float Round(float d, float r) { return d - r; }

    static float Circle(Vector2 p, Vector2 c, float r) { return (p - c).magnitude - r; }

    static float Ring(Vector2 p, Vector2 c, float r, float thick) { return Mathf.Abs((p - c).magnitude - r) - thick * 0.5f; }

    static float Box(Vector2 p, Vector2 c, Vector2 half, float r)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
        return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
    }

    static float Seg(Vector2 p, Vector2 a, Vector2 b, float thick)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - thick * 0.5f;
    }

    // 각도 a0~a1(도, 반시계) 사이의 둥근 끝 호
    static float Arc(Vector2 p, Vector2 c, float r, float thick, float a0, float a1)
    {
        Vector2 d = p - c;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        while (ang < a0) ang += 360f;
        if (ang <= a1) return Mathf.Abs(d.magnitude - r) - thick * 0.5f;
        Vector2 e0 = c + r * new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad), Mathf.Sin(a0 * Mathf.Deg2Rad));
        Vector2 e1 = c + r * new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad));
        return Mathf.Min((p - e0).magnitude, (p - e1).magnitude) - thick * 0.5f;
    }

    // 두께가 시작각(from)에서 thickFrom → 끝각(to)에서 thickTo로 변하는 호 (from > to면 시계방향)
    static float TaperArc(Vector2 p, Vector2 c, float r, float from, float to, float thickFrom, float thickTo)
    {
        float lo = Mathf.Min(from, to), hi = Mathf.Max(from, to);
        Vector2 d = p - c;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        while (ang < lo) ang += 360f;
        float ends = Mathf.Min(Circle(p, c + r * Dir(from), thickFrom * 0.5f), Circle(p, c + r * Dir(to), thickTo * 0.5f));
        if (ang > hi) return ends;
        float thick = Mathf.Lerp(thickFrom, thickTo, Mathf.InverseLerp(from, to, ang));
        return Mathf.Min(Mathf.Abs(d.magnitude - r) - thick * 0.5f, ends);
    }

    static Vector2 Dir(float deg) { return new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)); }

    static Vector2[] Star(Vector2 c, float outer, float inner)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = (90f + i * 36f) * Mathf.Deg2Rad;
            float r = i % 2 == 0 ? outer : inner;
            pts[i] = c + r * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        return pts;
    }

    // 임의의 다각형까지의 부호 있는 거리
    static float Poly(Vector2 p, params Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]);
        float s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i], w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }
}
