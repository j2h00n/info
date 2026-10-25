using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

// 4스테이지 도시 만들기 (4챕터: 얼음 동굴을 빠져나와 도시를 만남 → 빌딩 사이를 날아감)
// 2스테이지(사막) 씬을 복사해서: 독수리·모래바람·사막 배경을 빼고 → 도시 큰길 + 빌딩숲(CityStreet) + 신호등·자동차 장애물(CityTraffic)
// 400m 날면 클리어 (새총 쏘는 아이 보스는 아직 없음)
// 3스테이지에는 다음 스테이지 = 도시 + 넘어갈 때 팁
// 메뉴: Tools > Create City Stage (Stage 4)   (여러 번 실행해도 됨)
public static class CitySetup
{
    const string Stage1Path = "Assets/Scenes/SampleScene.unity";
    const string Stage2Path = "Assets/Scenes/Stage2_Desert.unity";
    const string Stage3Path = "Assets/Scenes/Stage3_IceCave.unity";
    public const string Stage4Path = "Assets/Scenes/Stage4_City.unity";
    const string MatDir = "Assets/Stages/City/Materials";

    public const string Stage4Tip = "tip. 도시에서는 신호등과 마주 달려오는 자동차를 피하세요! 신호등 아래로 빠져나가거나 위로 넘어가세요.";

    [MenuItem("Tools/Create City Stage (Stage 4)")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }
        EditorSceneManager.SaveOpenScenes();
        Directory.CreateDirectory(MatDir);

        // ── 3스테이지: 다음 스테이지 = 도시 + 넘어갈 때 팁 ──
        EditorSceneManager.OpenScene(Stage3Path);
        BirdController bird3 = FindBird();
        Undo.RecordObject(bird3, "City Stage");
        bird3.nextStageScene = Path.GetFileNameWithoutExtension(Stage4Path);
        bird3.nextStageTip = Stage4Tip;
        EditorUtility.SetDirty(bird3);
        GameUIStyler.Apply(); // 메뉴에 STAGE 4 버튼 (씬 저장까지 함)

        // ── 4스테이지: 처음이면 사막 씬을 그대로 복사 ──
        if (!File.Exists(Stage4Path)) AssetDatabase.CopyAsset(Stage2Path, Stage4Path);
        EditorSceneManager.OpenScene(Stage4Path);

        BirdController bird = FindBird();
        Undo.RecordObject(bird, "City Stage");
        bird.stageNumber = 4;
        bird.stageName = "CITY";
        bird.nextStageScene = "";
        bird.prevStageScene = Path.GetFileNameWithoutExtension(Stage3Path);
        bird.nextStageTip = "";
        bird.storyGoalMeters = 400f; // 400m 날면 클리어
        bird.ceilingY = 200f; // 천장 없음 (대신 신호등 높이 근처에 오래 있으면 헬기 이벤트 → CeilingHazard)
        bird.bossFight = null;
        bird.eagleChase = null;
        bird.iceEscape = null;
        bird.hatchIntro = null;
        EditorUtility.SetDirty(bird);

        // 사막 전용 오브젝트 + 바위 장애물 생성기 제거
        DestroyIfExists(GameObject.Find("EagleChase"));
        DestroyIfExists(GameObject.Find("Sandstorm"));
        DestroyIfExists(GameObject.Find("HatchIntro"));
        Transform canvas = GameObject.Find("Canvas").transform;
        DestroyIfExists(Find(canvas, "BossHud"));
        DestroyIfExists(Find(canvas, "BossCountdown"));
        NewMonoBehaviourScript rocks = Object.FindAnyObjectByType<NewMonoBehaviourScript>();
        if (rocks != null) DestroyIfExists(rocks.gameObject);

        // 사막 배경(땅·메사·풀)은 끔 → 도시 빌딩숲이 대신함
        GameObject outside = GameObject.Find("boxophobic");
        if (outside != null) { Undo.RecordObject(outside, "City Stage"); outside.SetActive(false); }

        // 머티리얼
        Material road = Lit("City_Road", new Color(0.18f, 0.19f, 0.21f), 0.15f);
        Material line = Unlit("City_Line", new Color(0.92f, 0.92f, 0.88f));
        Material center = Unlit("City_CenterLine", new Color(0.98f, 0.78f, 0.2f));
        Material walk = Lit("City_Sidewalk", new Color(0.55f, 0.53f, 0.5f), 0.1f);
        Material[] walls =
        {
            Lit("City_Wall_Concrete", new Color(0.62f, 0.6f, 0.58f), 0.15f),
            Lit("City_Wall_Brick", new Color(0.62f, 0.36f, 0.28f), 0.1f),
            Lit("City_Wall_Glass", new Color(0.32f, 0.45f, 0.58f), 0.75f),
            Lit("City_Wall_Sand", new Color(0.78f, 0.7f, 0.56f), 0.15f),
        };
        Material window = Lit("City_Window", new Color(0.1f, 0.14f, 0.2f), 0.9f);
        Material litWindow = Lit("City_WindowLit", new Color(1f, 0.85f, 0.55f), 0.5f, new Color(1.4f, 1.05f, 0.55f));
        Material roof = Lit("City_Roof", new Color(0.35f, 0.36f, 0.38f), 0.3f);
        Material lamp = Lit("City_Lamp", new Color(1f, 0.95f, 0.8f), 0.5f, new Color(2f, 1.8f, 1.3f));
        Material pole = Lit("City_Pole", new Color(0.25f, 0.27f, 0.3f), 0.45f);
        Material signal = Lit("City_Signal", new Color(0.08f, 0.08f, 0.09f), 0.4f);
        Material lampOff = Lit("City_LampOff", new Color(0.18f, 0.18f, 0.18f), 0.6f);
        Material red = Lit("City_Red", new Color(1f, 0.15f, 0.1f), 0.6f, new Color(3f, 0.25f, 0.15f));
        Material yellow = Lit("City_Yellow", new Color(1f, 0.75f, 0.1f), 0.6f, new Color(3f, 2f, 0.2f));
        Material green = Lit("City_Green", new Color(0.15f, 1f, 0.45f), 0.6f, new Color(0.2f, 3f, 0.9f));
        Material carBody = Lit("City_CarBody", Color.white, 0.7f);
        Material glass = Lit("City_CarGlass", new Color(0.12f, 0.16f, 0.22f), 0.95f);
        Material tire = Lit("City_Tire", new Color(0.06f, 0.06f, 0.07f), 0.2f);
        Material headlight = Lit("City_Headlight", new Color(1f, 0.97f, 0.85f), 0.5f, new Color(3f, 2.9f, 2.4f));
        Material taillight = Lit("City_Taillight", new Color(0.9f, 0.08f, 0.06f), 0.6f, new Color(2.2f, 0.12f, 0.08f));
        Material carTrim = Lit("City_CarTrim", new Color(0.13f, 0.13f, 0.15f), 0.35f);
        Material hub = Lit("City_Hub", new Color(0.78f, 0.8f, 0.82f), 0.8f);
        Material backplate = Lit("City_Backplate", new Color(0.98f, 0.82f, 0.15f), 0.4f);
        Material streetSign = Lit("City_StreetSign", new Color(0.06f, 0.42f, 0.22f), 0.5f);
        Material signText = Unlit("City_SignText", new Color(0.95f, 0.95f, 0.95f));
        Material trimMat = Lit("City_Trim", new Color(0.8f, 0.77f, 0.72f), 0.2f);
        Material storefront = Lit("City_Storefront", new Color(1f, 0.9f, 0.7f), 0.8f, new Color(1.1f, 0.85f, 0.5f));
        Material[] awnings =
        {
            Lit("City_Awning_Red", new Color(0.78f, 0.18f, 0.16f), 0.2f),
            Lit("City_Awning_Green", new Color(0.18f, 0.52f, 0.32f), 0.2f),
            Lit("City_Awning_Blue", new Color(0.18f, 0.35f, 0.7f), 0.2f),
            Lit("City_Awning_Stripe", new Color(0.95f, 0.92f, 0.85f), 0.2f),
        };
        Material[] neon =
        {
            Lit("City_Neon_Pink", new Color(1f, 0.35f, 0.7f), 0.5f, new Color(2.6f, 0.6f, 1.6f)),
            Lit("City_Neon_Cyan", new Color(0.3f, 0.9f, 1f), 0.5f, new Color(0.5f, 2.2f, 2.6f)),
            Lit("City_Neon_Orange", new Color(1f, 0.6f, 0.2f), 0.5f, new Color(2.6f, 1.3f, 0.3f)),
        };
        Material tank = Lit("City_WaterTank", new Color(0.45f, 0.3f, 0.2f), 0.15f);

        // 도시 배경
        CityStreet street = GetOrAdd<CityStreet>("CityStreet");
        Undo.RecordObject(street, "City Stage");
        street.bird = bird.transform;
        street.roadMat = road;
        street.lineMat = line;
        street.centerLineMat = center;
        street.sidewalkMat = walk;
        street.buildingMats = walls;
        street.windowMat = window;
        street.litWindowMat = litWindow;
        street.roofMat = roof;
        street.lampMat = lamp;
        street.trimMat = trimMat;
        street.storefrontMat = storefront;
        street.awningMats = awnings;
        street.neonMats = neon;
        street.tankMat = tank;
        EditorUtility.SetDirty(street);

        // 신호등 + 자동차
        CityTraffic traffic = GetOrAdd<CityTraffic>("CityTraffic");
        Undo.RecordObject(traffic, "City Stage");
        traffic.player = bird.transform;
        traffic.rowSpacing = 24f; // 숲·사막과 같은 벽 간격
        traffic.poleMat = pole;
        traffic.signalMat = signal;
        traffic.lampOffMat = lampOff;
        traffic.redMat = red;
        traffic.yellowMat = yellow;
        traffic.greenMat = green;
        traffic.carBodyMat = carBody;
        traffic.carGlassMat = glass;
        traffic.tireMat = tire;
        traffic.headlightMat = headlight;
        traffic.taillightMat = taillight;
        traffic.carTrimMat = carTrim;
        traffic.hubMat = hub;
        traffic.backplateMat = backplate;
        traffic.streetSignMat = streetSign;
        traffic.signTextMat = signText;
        EditorUtility.SetDirty(traffic);

        // 높이 10.7 위에 2.5초 머물면: 경고 → 방송국 헬기 컷씬 → 추락
        CeilingHazard hazard = GetOrAdd<CeilingHazard>("CeilingHazard");
        Undo.RecordObject(hazard, "City Stage");
        hazard.bird = bird;
        hazard.bodyMat = Lit("City_HeliBody", new Color(0.85f, 0.12f, 0.12f), 0.7f);
        hazard.stripeMat = Lit("City_HeliStripe", new Color(0.95f, 0.95f, 0.95f), 0.5f);
        hazard.glassMat = Lit("City_HeliGlass", new Color(0.15f, 0.22f, 0.3f), 0.95f);
        hazard.darkMat = Lit("City_HeliDark", new Color(0.12f, 0.12f, 0.14f), 0.4f);
        hazard.lightMat = lamp;
        hazard.dangerHeight = 10.7f; // 신호등을 높여도 헬기 이벤트 높이는 그대로
        hazard.limitSeconds = 2.5f;
        hazard.roadY = street.roadY;
        EditorUtility.SetDirty(hazard);

        // 400m 보스: 새총 쏘는 아이 (30초 버티면 피할 수 없는 한 발 → 엔딩 컷씬)
        GameUIIconGenerator.SlingshotIcon();
        KidBoss boss = GetOrAdd<KidBoss>("KidBoss");
        Undo.RecordObject(boss, "City Stage");
        boss.bird = bird;
        boss.traffic = traffic;
        boss.ceilingHazard = hazard;
        boss.skinMat = Lit("Kid_Skin", new Color(1f, 0.8f, 0.66f), 0.3f);
        boss.hairMat = Lit("Kid_Hair", new Color(0.28f, 0.17f, 0.09f), 0.3f);
        boss.shirtMat = Lit("Kid_Shirt", new Color(0.2f, 0.48f, 0.9f), 0.2f);
        boss.stripeMat = Lit("Kid_White", new Color(0.97f, 0.97f, 0.97f), 0.2f);
        boss.shortsMat = Lit("Kid_Shorts", new Color(0.3f, 0.32f, 0.38f), 0.2f);
        boss.shoeMat = Lit("Kid_Shoes", new Color(0.92f, 0.25f, 0.2f), 0.4f);
        boss.capMat = Lit("Kid_Cap", new Color(0.95f, 0.75f, 0.15f), 0.3f);
        boss.eyeMat = Lit("Kid_Eye", new Color(0.06f, 0.05f, 0.06f), 0.6f);
        boss.woodMat = Lit("Kid_Wood", new Color(0.58f, 0.36f, 0.17f), 0.3f);
        boss.bandMat = Lit("Kid_Band", new Color(0.55f, 0.12f, 0.1f), 0.4f);
        boss.pebbleMat = Lit("Kid_Pebble", new Color(0.5f, 0.5f, 0.52f), 0.25f);
        boss.cheekMat = Lit("Kid_Cheek", new Color(1f, 0.52f, 0.55f), 0.3f);
        boss.tapeMat = Lit("Kid_Tape", new Color(0.95f, 0.78f, 0.6f), 0.2f);
        boss.telegraphMat = BossSetup.Unlit("AttackLine_Red", new Color(1f, 0.12f, 0.12f, 0.25f), true);
        boss.bossBgm = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/BossBGM.wav");
        boss.shootSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EagleWhoosh.wav");
        boss.hitSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/IceThud.wav");
        boss.surviveSeconds = 30f;
        boss.roadY = street.roadY;
        // 화면 위 가운데: 새총 아이콘 + "SLINGSHOT KID 30s" + 줄어드는 남은 시간 막대 (주황)
        GameObject hud; RectTransform fill; TMPro.TextMeshProUGUI countdown, nameText;
        GameUIStyler.BuildBossHud(canvas, "Icon_Slingshot", new Color(1f, 0.85f, 0.6f), "SLINGSHOT KID", new Color(0.95f, 0.5f, 0.2f), false,
                                  out hud, out fill, out countdown, out nameText);
        boss.bossHud = hud;
        boss.timeFill = fill;
        boss.bossName = nameText;
        EditorUtility.SetDirty(boss);
        Undo.RecordObject(bird, "City Stage");
        bird.kidBoss = boss;
        EditorUtility.SetDirty(bird);

        SetCityLighting();
        GameUIStyler.Apply(); // 메뉴 제목 "STAGE 4 - CITY", STAGE 3 버튼, 클리어 문구, BGM (씬 저장까지 함)

        // 빌드에 네 스테이지 모두 포함
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(Stage1Path, true),
            new EditorBuildSettingsScene(Stage2Path, true),
            new EditorBuildSettingsScene(Stage3Path, true),
            new EditorBuildSettingsScene(Stage4Path, true),
        };
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("4스테이지 도시 준비 완료: " + Stage4Path);
    }

    // 해 질 녘 도시: 낮은 금빛 햇살, 따뜻한 안개로 먼 길 끝을 가림
    static void SetCityLighting()
    {
        Camera cam = GameObject.Find("Cube/Main Camera").GetComponent<Camera>();
        Undo.RecordObject(cam, "City Stage");
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.farClipPlane = 1500f;

        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/BOXOPHOBIC/Skybox Cubemap Extended/Demo/Materials/Skybox Cubemap Extended Day.mat");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.86f, 0.78f, 0.7f);
        RenderSettings.fogStartDistance = 90f;
        RenderSettings.fogEndDistance = 520f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.75f, 0.72f, 0.75f);
        RenderSettings.ambientEquatorColor = new Color(0.6f, 0.52f, 0.48f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.28f);

        Light sun = GameObject.Find("Directional Light").GetComponent<Light>();
        Undo.RecordObject(sun, "City Stage");
        Undo.RecordObject(sun.transform, "City Stage");
        sun.color = new Color(1f, 0.82f, 0.62f);
        sun.intensity = 1.25f;
        sun.transform.rotation = Quaternion.Euler(28f, -35f, 0f);

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(sun.gameObject.scene);
    }

    // ───────────────────────── 공용 ─────────────────────────

    static BirdController FindBird() { return GameObject.Find("Cube").GetComponent<BirdController>(); }

    static GameObject Find(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        return t != null ? t.gameObject : null;
    }

    static void DestroyIfExists(GameObject g)
    {
        if (g != null) Undo.DestroyObjectImmediate(g);
    }

    static T GetOrAdd<T>(string name) where T : Component
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "City Stage");
        }
        T c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    static Material GetOrCreate(string name, string shader)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    static Material Lit(string name, Color color, float smoothness, Color? emission = null)
    {
        Material m = GetOrCreate(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Unlit(string name, Color color)
    {
        Material m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }
}
