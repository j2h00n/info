using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

// 뱀 보스 준비: 머티리얼 만들기 + 씬에 BossFight 배치 + 체력바 UI + 연결
// 먼저 Tools > Generate Game UI Icons, Tools > Apply Game UI Style 을 실행한 뒤 사용
// 메뉴: Tools > Setup Snake Boss (여러 번 실행해도 중복 생성 안 됨)
public static class BossSetup
{
    const string MatDir = "Assets/Boss/Materials";

    [MenuItem("Tools/Setup Snake Boss")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }
        System.IO.Directory.CreateDirectory(MatDir);

        BirdController bird = GameObject.Find("Cube").GetComponent<BirdController>();
        NewMonoBehaviourScript spawner = Object.FindAnyObjectByType<NewMonoBehaviourScript>();

        GameObject go = GameObject.Find("BossFight");
        if (go == null)
        {
            go = new GameObject("BossFight");
            Undo.RegisterCreatedObjectUndo(go, "Snake Boss");
        }
        BossFight boss = go.GetComponent<BossFight>();
        if (boss == null) boss = Undo.AddComponent<BossFight>(go);
        Undo.RecordObject(boss, "Snake Boss");

        boss.bird = bird;
        boss.obstacleSpawner = spawner;

        // 보라색 독뱀 + 라임색 줄무늬, 노란 눈
        Color purple = new Color(0.45f, 0.24f, 0.62f), lime = new Color(0.70f, 0.92f, 0.30f);
        boss.snakeMat = Lit("Snake_Body", purple, 0.45f);
        // 매끈한 몸통용: 보라 바탕에 라임 줄무늬가 감긴 텍스처
        boss.skinMat = Lit("Snake_Skin", Color.white, 0.45f);
        boss.skinMat.SetTexture("_BaseMap", StripeTexture(purple, lime));
        boss.eyeMat = Lit("Snake_Eye", new Color(1f, 0.85f, 0.15f), 0.8f, new Color(0.5f, 0.4f, 0f));
        boss.pupilMat = Lit("Snake_Pupil", new Color(0.05f, 0.03f, 0.05f), 0.9f);
        boss.tongueMat = Lit("Snake_Tongue", new Color(0.9f, 0.15f, 0.25f), 0.6f);
        boss.balloonMats = new Material[]
        {
            Lit("Balloon_Red", new Color(0.95f, 0.25f, 0.25f), 0.85f),
            Lit("Balloon_Yellow", new Color(1f, 0.82f, 0.25f), 0.85f),
            Lit("Balloon_Blue", new Color(0.30f, 0.65f, 0.95f), 0.85f),
            Lit("Balloon_Pink", new Color(0.98f, 0.50f, 0.75f), 0.85f),
        };
        // 독 오물: 형광 초록으로 빛나서 어두운 노을 배경에서 잘 보임
        boss.sludgeMat = Lit("Sludge", new Color(0.45f, 0.85f, 0.15f), 0.9f, new Color(0.25f, 0.55f, 0.05f));
        boss.pinMat = Lit("Pin_Gold", new Color(1f, 0.80f, 0.30f), 0.9f, new Color(0.7f, 0.5f, 0.1f));
        boss.stringMat = Unlit("Balloon_String", new Color(0.95f, 0.95f, 0.9f), false);
        boss.telegraphMat = Unlit("AttackLine_Red", new Color(1f, 0.12f, 0.12f, 0.25f), true);
        boss.trailMat = Unlit("Dash_Trail", new Color(0.78f, 0.45f, 1f, 0.5f), true); // 보라빛 잔상
        // 독바다: 진한 형광 독초록 (매끈하게 하면 하늘이 비쳐 허옇게 보여서 반사는 약하게, 대신 스스로 빛나게)
        // 뒤쪽 풀밭(진초록)과 구분되도록 형광 라임으로 빛나게
        boss.poisonMat = Lit("Poison_Sea", new Color(0.42f, 0.88f, 0.08f), 0.35f, new Color(0.26f, 0.60f, 0.03f));
        boss.poisonMat.SetFloat("_EnvironmentReflections", 0f);
        boss.poisonMat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

        // 보스전 BGM (make_boss_bgm.py로 만든 8비트 루프)
        const string bgmPath = "Assets/Audio/BossBGM.wav";
        AudioImporter audio = AssetImporter.GetAtPath(bgmPath) as AudioImporter;
        if (audio != null)
        {
            AudioImporterSampleSettings s = audio.defaultSampleSettings;
            s.loadType = AudioClipLoadType.CompressedInMemory;
            audio.defaultSampleSettings = s;
            audio.SaveAndReimport();
        }
        boss.bossBgm = AssetDatabase.LoadAssetAtPath<AudioClip>(bgmPath);

        // 체력바 + 카운트다운 UI
        Transform canvas = GameObject.Find("Canvas").transform;
        GameObject hud; RectTransform fill; TextMeshProUGUI countdown;
        GameUIStyler.BuildBossHud(canvas, out hud, out fill, out countdown);
        boss.bossHud = hud;
        boss.hpFill = fill;
        boss.countdownText = countdown;
        EditorUtility.SetDirty(boss);

        Undo.RecordObject(bird, "Snake Boss");
        bird.bossFight = boss;
        EditorUtility.SetDirty(bird);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
        Debug.Log("뱀 보스 준비 완료");
    }

    internal static Material Lit(string name, Color color, float smoothness, Color? emission = null)
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

    // 경고선: 반투명 + 양면 (새가 빨간 기둥 안에 있어도 보이게)
    internal static Material Unlit(string name, Color color, bool transparent)
    {
        Material m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
        if (transparent)
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
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // 세로 한 줄짜리 텍스처: 아래 70% 보라, 위 30% 라임 (경계는 부드럽게)
    static Texture2D StripeTexture(Color body, Color stripe)
    {
        string path = MatDir + "/Snake_Stripes.png";
        const int h = 64;
        var tex = new Texture2D(4, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.66f, 0.72f, v)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.94f, 1f, v)));
            Color c = Color.Lerp(body, stripe, s);
            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.sRGBTexture = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
        else if (m.shader.name != shader)
        {
            m.shader = Shader.Find(shader);
        }
        return m;
    }
}
