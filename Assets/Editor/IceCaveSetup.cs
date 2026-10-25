using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using TMPro;

// 3스테이지 얼음 동굴 만들기
// 2스테이지(사막) 씬을 복사해서: 독수리·모래바람을 빼고 → 동굴 터널(IceCaveTunnel) + 얼음 이벤트(IceFreeze) + 300m 붕괴 탈출(IceCaveEscape)
// 장애물은 얼음색 + 벽 간격 30, 조명·안개는 푸른 동굴, 바깥 배경은 설산(탈출 컷씬에서만 켜짐)
// 2스테이지에는 다음 스테이지 = 얼음 동굴 + 넘어갈 때 팁
// 메뉴: Tools > Create Ice Cave Stage (Stage 3)   (여러 번 실행해도 됨)
public static class IceCaveSetup
{
    const string Stage1Path = "Assets/Scenes/SampleScene.unity";
    const string Stage2Path = "Assets/Scenes/Stage2_Desert.unity";
    const string Stage3Path = "Assets/Scenes/Stage3_IceCave.unity";
    const string MatDir = "Assets/Stages/IceCave/Materials";
    const string MeshDir = "Assets/Stages/IceCave/Meshes";
    const string PropsName = "SnowProps";

    const string Stage3Tip = "tip. 얼음 동굴에서는 벽을 지날 때마다 몸이 얼어붙을 수 있어요! ← → (모션: 고개 좌우)를 2번 흔들어 2초 안에 빠져나오세요. 300m부터는 동굴이 무너집니다.";

    [MenuItem("Tools/Create Ice Cave Stage (Stage 3)")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }
        EditorSceneManager.SaveOpenScenes();
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory(MeshDir);
        GameUIIconGenerator.SnowflakeIcon(); // 메뉴의 탈출 바로 하기 버튼 아이콘
        SetMusicImport("Assets/Audio/IceCaveBGM.wav");
        SetMusicImport("Assets/Audio/IceEscapeBGM.wav");

        // ── 2스테이지: 다음 스테이지 = 얼음 동굴 + 넘어갈 때 팁 ──
        EditorSceneManager.OpenScene(Stage2Path);
        BirdController bird2 = FindBird();
        Undo.RecordObject(bird2, "Ice Cave Stage");
        bird2.nextStageScene = Path.GetFileNameWithoutExtension(Stage3Path);
        bird2.nextStageTip = Stage3Tip;
        EditorUtility.SetDirty(bird2);
        GameUIStyler.Apply(); // 메뉴에 STAGE 3 버튼 + STAGE 1 버튼 (씬 저장까지 함)

        // ── 3스테이지: 처음이면 사막 씬을 그대로 복사 ──
        if (!File.Exists(Stage3Path)) AssetDatabase.CopyAsset(Stage2Path, Stage3Path);
        EditorSceneManager.OpenScene(Stage3Path);

        BirdController bird = FindBird();
        Undo.RecordObject(bird, "Ice Cave Stage");
        bird.stageNumber = 3;
        bird.stageName = "ICE CAVE";
        // 도시(4스테이지)가 이미 있으면 그쪽으로 이어지게 둠
        bool hasCity = File.Exists(CitySetup.Stage4Path);
        bird.nextStageScene = hasCity ? Path.GetFileNameWithoutExtension(CitySetup.Stage4Path) : "";
        bird.prevStageScene = Path.GetFileNameWithoutExtension(Stage2Path);
        bird.nextStageTip = hasCity ? CitySetup.Stage4Tip : "";
        bird.storyGoalMeters = 300f; // 300m에서 동굴 붕괴
        bird.bossFight = null;
        bird.eagleChase = null;
        EditorUtility.SetDirty(bird);

        // 사막 전용 오브젝트 제거
        DestroyIfExists(GameObject.Find("EagleChase"));
        DestroyIfExists(GameObject.Find("Sandstorm"));
        Transform canvas = GameObject.Find("Canvas").transform;
        DestroyIfExists(Find(canvas, "BossHud"));
        DestroyIfExists(Find(canvas, "BossCountdown"));

        // 머티리얼
        Material rock = Lit("IceCave_Rock", new Color(0.16f, 0.27f, 0.42f), 0.35f);
        Material ice = Lit("IceCave_Ice", new Color(0.70f, 0.90f, 1f), 0.92f, new Color(0.05f, 0.12f, 0.18f));
        Material crystal = Lit("IceCave_Crystal", new Color(0.45f, 0.85f, 1f), 0.9f, new Color(0.25f, 0.75f, 1.1f));
        Material block = LitTransparent("IceCave_IceBlock", new Color(0.72f, 0.92f, 1f, 0.5f), 0.95f);
        Material shard = Lit("IceCave_Shard", new Color(0.82f, 0.95f, 1f), 0.9f, new Color(0.1f, 0.2f, 0.28f));
        Material boulder = Lit("IceCave_Boulder", new Color(0.5f, 0.72f, 0.9f), 0.6f);
        Material crack = Unlit("IceCave_Crack", Color.white, false);
        Material dust = Unlit("IceCave_Dust", new Color(0.9f, 0.95f, 1f, 0.5f), true);
        Material exitLight = Unlit("IceCave_ExitLight", new Color(1f, 1f, 1f, 0.9f), true);
        Material telegraph = BossSetup.Unlit("AttackLine_Red", new Color(1f, 0.12f, 0.12f, 0.25f), true);
        Material cliff = Lit("IceCave_Cliff", new Color(0.55f, 0.68f, 0.82f), 0.3f);
        Material snow = Lit("IceCave_Snow", new Color(0.95f, 0.97f, 1f), 0.2f);

        // 장애물: 얼음빛 바위, 벽 간격 넓게 (벽마다 얼음 이벤트가 나올 수 있어서)
        NewMonoBehaviourScript spawner = Object.FindAnyObjectByType<NewMonoBehaviourScript>();
        Undo.RecordObject(spawner, "Ice Cave Stage");
        spawner.rockTint = new Color(0.62f, 0.86f, 1f);
        spawner.grassPrefabs = new GameObject[0];
        spawner.chunkSpacing = 30f;
        EditorUtility.SetDirty(spawner);

        // 바깥 배경 → 설산 (동굴 안에선 꺼 두고 탈출 컷씬에서 켬)
        GameObject outside = GameObject.Find("boxophobic");
        if (outside == null) outside = FindInactiveRoot("boxophobic");
        ConvertToSnow(outside.transform, snow, cliff);
        Undo.RecordObject(outside, "Ice Cave Stage");
        outside.SetActive(false);

        // 동굴 터널
        IceCaveTunnel tunnel = GetOrAdd<IceCaveTunnel>("IceCaveTunnel");
        Undo.RecordObject(tunnel, "Ice Cave Stage");
        tunnel.bird = bird.transform;
        tunnel.rockMat = rock;
        tunnel.iceMat = ice;
        tunnel.crystalMat = crystal;
        EditorUtility.SetDirty(tunnel);

        // 얼음 이벤트
        IceFreeze freeze = GetOrAdd<IceFreeze>("IceFreeze");
        Undo.RecordObject(freeze, "Ice Cave Stage");
        freeze.bird = bird;
        freeze.iceMat = block;
        freeze.shardMat = shard;
        freeze.crackMat = crack;
        freeze.freezeSound = Clip("IceFreeze");
        freeze.crackSound = Clip("IceCrack");
        freeze.shatterSound = Clip("IceShatter");
        freeze.thudSound = Clip("IceThud");
        freeze.chance = 0.4f;
        freeze.pressesToBreak = 2;
        freeze.breakTime = 2f;
        EditorUtility.SetDirty(freeze);

        // 동굴 붕괴 탈출
        IceCaveEscape escape = GetOrAdd<IceCaveEscape>("IceCaveEscape");
        Undo.RecordObject(escape, "Ice Cave Stage");
        escape.bird = bird;
        escape.obstacleSpawner = spawner;
        escape.tunnel = tunnel;
        escape.outsideWorld = outside;
        escape.outsideSkybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/BOXOPHOBIC/Skybox Cubemap Extended/Demo/Materials/Skybox Cubemap Extended Day.mat");
        escape.icicleMat = ice;
        escape.beamMat = ice;
        escape.boulderMat = boulder;
        escape.debrisMat = rock;
        escape.shardMat = shard;
        escape.dustMat = dust;
        escape.telegraphMat = telegraph;
        escape.lightMat = exitLight;
        escape.cliffMat = cliff;
        escape.snowMat = snow;
        escape.escapeBgm = Clip("IceEscapeBGM");
        escape.rumbleSound = Clip("CaveRumble");
        escape.icicleSound = Clip("IcicleFall");
        escape.shatterSound = Clip("IceShatter");
        escape.thudSound = Clip("IceThud");
        escape.surviveSeconds = 35f;
        escape.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        escape.titleMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/UI/GameText.mat");
        // 화면 위 가운데: 눈송이 아이콘 + "ESCAPE THE CAVE 35s" + 줄어드는 남은 시간 막대 (얼음색)
        GameObject hud; RectTransform fill; TextMeshProUGUI countdown, nameText;
        GameUIStyler.BuildBossHud(canvas, "Icon_Snowflake", new Color(0.85f, 0.95f, 1f), "ESCAPE THE CAVE", new Color(0.45f, 0.8f, 1f), false,
                                  out hud, out fill, out countdown, out nameText);
        escape.bossHud = hud;
        escape.timeFill = fill;
        escape.bossName = nameText;
        EditorUtility.SetDirty(escape);

        Undo.RecordObject(bird, "Ice Cave Stage");
        bird.iceEscape = escape;
        EditorUtility.SetDirty(bird);

        SetCaveLighting();
        GameUIStyler.Apply(); // 메뉴 제목 "STAGE 3 - ICE CAVE", STAGE 2 버튼, 탈출 바로 하기 버튼, 클리어 문구, 얼음 동굴 BGM (씬 저장까지 함)

        // 빌드에 세 스테이지 모두 포함 (도시가 있으면 4스테이지까지)
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(Stage1Path, true),
            new EditorBuildSettingsScene(Stage2Path, true),
            new EditorBuildSettingsScene(Stage3Path, true),
        };
        if (hasCity) scenes.Add(new EditorBuildSettingsScene(CitySetup.Stage4Path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("3스테이지 얼음 동굴 준비 완료: " + Stage3Path);
    }

    // ───────────────────────── 바깥 설산 ─────────────────────────

    static void ConvertToSnow(Transform root, Material snow, Material cliff)
    {
        DestroyIfExists(Find(root, "DesertProps"));
        DestroyIfExists(Find(root, PropsName));
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            string mats = "";
            foreach (Material m in r.sharedMaterials) mats += m != null ? m.name : "";
            if (r.name.Contains("Terrain")) Swap(r, snow);                         // 땅 → 눈밭
            else if (mats.Contains("Grass") || mats.Contains("FirTree")) SetActive(r.gameObject, false); // 마른 풀 → 없앰 (눈에 덮임)
            else if (mats.Contains("Sandstone") || mats.Contains("Rock")) Swap(r, cliff); // 바위 → 푸른 얼음 바위
        }

        // 지평선의 눈 덮인 뾰족 산 (사막 메사 자리와 비슷하게, 더 높게)
        Transform props = new GameObject(PropsName).transform;
        Undo.RegisterCreatedObjectUndo(props.gameObject, "Ice Cave Stage");
        props.SetParent(root, false);
        Mesh mountain = MountainMesh(false), cap = MountainMesh(true);
        Vector3 start = FindBird().transform.position;
        var peaks = new[]
        {
            new Vector4(-420, 900, 260, 520), new Vector4(-150, 1250, 330, 700), new Vector4(120, 1400, 300, 640),
            new Vector4(380, 1000, 280, 560), new Vector4(650, 1300, 360, 720), new Vector4(-700, 1200, 320, 640),
            new Vector4(-620, 600, 200, 380), new Vector4(600, 650, 220, 420), new Vector4(0, 1700, 420, 820),
        };
        for (int i = 0; i < peaks.Length; i++)
        {
            Vector4 p = peaks[i]; // x, z, 반지름, 높이
            Transform m = new GameObject("SnowPeak").transform;
            m.SetParent(props, true);
            m.position = start + new Vector3(p.x, -80f, p.y);
            m.rotation = Quaternion.Euler(0f, i * 47f, 0f);
            m.localScale = new Vector3(p.z, p.w, p.z * Random.Range(0.8f, 1.1f));
            Part(m, mountain, cliff);
            Part(m, cap, snow);
        }
    }

    // 울퉁불퉁한 뾰족 산 (반지름 1, 높이 1). snowCap이면 위쪽 35%만 살짝 크게 (눈 모자)
    static Mesh MountainMesh(bool snowCap)
    {
        string path = MeshDir + (snowCap ? "/SnowCap.asset" : "/Mountain.asset");
        var kit = new MeshKit();
        const int sides = 11;
        float grow = snowCap ? 1.03f : 1f;
        var ring = new Vector3[sides];
        var mid = new Vector3[sides];
        Random.InitState(55); // 산과 눈 모자가 같은 모양을 따르도록 같은 씨앗
        for (int k = 0; k < sides; k++)
        {
            float a = Mathf.PI * 2f * k / sides;
            float jr = Random.Range(0.8f, 1.15f);
            float jm = Random.Range(0.85f, 1.1f);
            ring[k] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * jr;
            mid[k] = new Vector3(Mathf.Cos(a + 0.2f) * 0.55f * jm, 0.45f + Random.Range(-0.06f, 0.06f), Mathf.Sin(a + 0.2f) * 0.55f * jm);
        }
        Vector3 peak = new Vector3(Random.Range(-0.05f, 0.05f), 1f, Random.Range(-0.05f, 0.05f));
        for (int k = 0; k < sides; k++)
        {
            int k2 = (k + 1) % sides;
            if (!snowCap)
            {
                kit.Tri(ring[k], ring[k2], mid[k2], ring[k] + ring[k2]);
                kit.Tri(ring[k], mid[k2], mid[k], ring[k] + ring[k2]);
                kit.Tri(mid[k], mid[k2], peak, mid[k] + mid[k2]);
            }
            else
            {
                // 산 윗부분(mid ~ peak)을 따라 조금 바깥으로 띄운 눈, 아래 가장자리는 들쭉날쭉
                Vector3 a = Vector3.Lerp(mid[k], peak, 0.35f + 0.15f * (k % 2)) * grow;
                Vector3 b = Vector3.Lerp(mid[k2], peak, 0.35f + 0.15f * (k2 % 2)) * grow;
                kit.Tri(a, b, peak * grow, a + b);
            }
        }
        Mesh mesh = kit.ToMesh(snowCap ? "SnowCap" : "Mountain");
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); return existing; }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static void Part(Transform parent, Mesh mesh, Material mat)
    {
        GameObject g = new GameObject(mesh.name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ───────────────────────── 동굴 빛 ─────────────────────────

    static void SetCaveLighting()
    {
        // 동굴 안: 하늘 대신 짙은 남색 배경, 푸른 안개, 위에서 희미하게 새어 드는 차가운 빛
        Camera cam = GameObject.Find("Cube/Main Camera").GetComponent<Camera>();
        Undo.RecordObject(cam, "Ice Cave Stage");
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.08f, 0.14f);
        cam.farClipPlane = 2500f; // 탈출 후 바깥 설산까지

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.06f, 0.12f, 0.2f);
        RenderSettings.fogStartDistance = 25f;
        RenderSettings.fogEndDistance = 260f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.45f, 0.62f, 0.85f);
        RenderSettings.ambientEquatorColor = new Color(0.25f, 0.35f, 0.5f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.16f, 0.25f);

        Light sun = GameObject.Find("Directional Light").GetComponent<Light>();
        Undo.RecordObject(sun, "Ice Cave Stage");
        Undo.RecordObject(sun.transform, "Ice Cave Stage");
        sun.color = new Color(0.7f, 0.85f, 1f);
        sun.intensity = 0.9f;
        sun.transform.rotation = Quaternion.Euler(70f, -20f, 0f);

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(sun.gameObject.scene);
    }

    // ───────────────────────── 공용 ─────────────────────────

    static void SetMusicImport(string path)
    {
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;
        AudioImporterSampleSettings s = importer.defaultSampleSettings;
        s.loadType = AudioClipLoadType.CompressedInMemory;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.7f;
        importer.defaultSampleSettings = s;
        importer.SaveAndReimport();
    }

    static AudioClip Clip(string name) { return AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/" + name + ".wav"); }

    static T GetOrAdd<T>(string name) where T : Component
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Ice Cave Stage");
        }
        T c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    static GameObject FindInactiveRoot(string name)
    {
        foreach (GameObject g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (g.name == name) return g;
        return null;
    }

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

    static void SetActive(GameObject g, bool on)
    {
        Undo.RecordObject(g, "Ice Cave Stage");
        g.SetActive(on);
    }

    static void Swap(Renderer r, Material m)
    {
        Undo.RecordObject(r, "Ice Cave Stage");
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) mats[i] = m;
        r.sharedMaterials = mats;
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

    static Material Unlit(string name, Color color, bool transparent)
    {
        Material m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
        if (transparent) MakeTransparent(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    // 반투명 얼음 (안에 갇힌 새가 비쳐 보임). 양면으로 그려서 안쪽 면도 보임
    static Material LitTransparent(string name, Color color, float smoothness)
    {
        Material m = Lit(name, color, smoothness);
        MakeTransparent(m);
        return m;
    }

    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_Cull", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(m);
    }
}
