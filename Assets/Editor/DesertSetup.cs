using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;

// 2스테이지 사막맵 만들기
// 1스테이지(숲) 씬을 복사해서 배경·하늘·빛·장애물 색을 사막으로 바꿈 (보스는 아직 없음 → 500m 도착하면 클리어)
// 메뉴: Tools > Create Desert Stage (Stage 2)   (여러 번 실행해도 됨: 소품은 지우고 다시 만듦)
public static class DesertSetup
{
    const string Stage1Path = "Assets/Scenes/SampleScene.unity";
    const string Stage2Path = "Assets/Scenes/Stage2_Desert.unity";
    const string MatDir = "Assets/Stages/Desert/Materials";
    const string PropsName = "DesertProps";

    // 사막 팔레트
    static readonly Color Sand = new Color(0.88f, 0.71f, 0.46f);
    static readonly Color DryGrass = new Color(0.80f, 0.69f, 0.38f);
    static readonly Color Sandstone = new Color(0.80f, 0.43f, 0.25f);
    static readonly Color SandstoneBand = new Color(0.62f, 0.30f, 0.18f);
    static readonly Color Cactus = new Color(0.30f, 0.55f, 0.28f);

    [MenuItem("Tools/Create Desert Stage (Stage 2)")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }
        EditorSceneManager.SaveOpenScenes();

        // ── 1스테이지: 다음 스테이지 = 사막 ──
        EditorSceneManager.OpenScene(Stage1Path);
        BirdController bird1 = FindBird();
        Undo.RecordObject(bird1, "Desert Stage");
        bird1.stageNumber = 1;
        bird1.stageName = "FOREST";
        bird1.nextStageScene = Path.GetFileNameWithoutExtension(Stage2Path);
        bird1.prevStageScene = "";
        EditorUtility.SetDirty(bird1);
        GameUIStyler.Apply(); // 메뉴에 STAGE 2 버튼, 클리어 화면에 NEXT STAGE 버튼 (씬 저장까지 함)

        // ── 2스테이지: 처음이면 1스테이지 씬을 그대로 복사 ──
        if (!File.Exists(Stage2Path)) AssetDatabase.CopyAsset(Stage1Path, Stage2Path);
        EditorSceneManager.OpenScene(Stage2Path);

        BirdController bird = FindBird();
        Undo.RecordObject(bird, "Desert Stage");
        bird.stageNumber = 2;
        bird.stageName = "DESERT";
        bird.storyGoalMeters = 500f; // 숲(300m)을 복사해 오므로 사막 거리는 따로 지정
        bird.nextStageScene = "";
        bird.prevStageScene = Path.GetFileNameWithoutExtension(Stage1Path);
        bird.bossFight = null; // 사막 보스는 뱀이 아니라 독수리 (아래 EagleSetup.Build)
        EditorUtility.SetDirty(bird);

        // 숲 보스와 보스 전용 UI는 이 스테이지에서 제거
        DestroyIfExists(GameObject.Find("BossFight"));
        Transform canvas = GameObject.Find("Canvas").transform;
        DestroyIfExists(Find(canvas, "BossHud"));
        DestroyIfExists(Find(canvas, "BossCountdown"));

        ConvertEnvironment();
        SetDesertLighting();

        // 장애물 바위: 사암색, 꼭대기 풀은 없음
        NewMonoBehaviourScript spawner = Object.FindAnyObjectByType<NewMonoBehaviourScript>();
        Undo.RecordObject(spawner, "Desert Stage");
        spawner.rockTint = new Color(1.0f, 0.66f, 0.42f); // 원래 바위색(연한 하늘색)에 곱해져 주황빛 사암이 됨
        spawner.grassPrefabs = new GameObject[0];
        EditorUtility.SetDirty(spawner);

        // 모래 바람 (300m ~ 350m, 400m ~ 450m)
        GameObject stormGo = GameObject.Find("Sandstorm");
        if (stormGo == null)
        {
            stormGo = new GameObject("Sandstorm");
            Undo.RegisterCreatedObjectUndo(stormGo, "Desert Stage");
        }
        Sandstorm storm = stormGo.GetComponent<Sandstorm>();
        if (storm == null) storm = Undo.AddComponent<Sandstorm>(stormGo);
        Undo.RecordObject(storm, "Desert Stage");
        storm.bird = bird;
        storm.zones = new[] { new Vector2(300f, 350f), new Vector2(400f, 450f) };
        EditorUtility.SetDirty(storm);

        EagleSetup.Build(); // 독수리 보스 + 남은 시간 막대 UI
        GameUIStyler.Apply(); // 메뉴 제목 "STAGE 2 - DESERT", STAGE 1 버튼, 보스 버튼, 클리어 문구 (씬 저장까지 함)

        // 빌드에 두 스테이지 모두 포함
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(Stage1Path, true),
            new EditorBuildSettingsScene(Stage2Path, true),
        };
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("2스테이지 사막맵 준비 완료: " + Stage2Path);
    }

    // ───────────────────────── 배경 ─────────────────────────

    static void ConvertEnvironment()
    {
        Directory.CreateDirectory(MatDir);
        Material sand = Lit("Desert_Sand", Sand, 0.1f);
        Material grass = Lit("Desert_DryGrass", DryGrass, 0.1f);
        Material stone = Lit("Desert_Sandstone", Sandstone, 0.15f);
        Material band = Lit("Desert_SandstoneBand", SandstoneBand, 0.15f);
        Material cactus = Lit("Desert_Cactus", Cactus, 0.25f);

        Transform root = GameObject.Find("boxophobic").transform; // 배경 전체 (새를 따라다님)
        DestroyIfExists(Find(root, PropsName));
        Transform props = new GameObject(PropsName).transform;
        Undo.RegisterCreatedObjectUndo(props.gameObject, "Desert Stage");
        props.SetParent(root, false);

        var trees = new List<Bounds>();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            string mats = "";
            foreach (Material m in r.sharedMaterials) mats += m != null ? m.name : "";

            if (r is SpriteRenderer) SetActive(r.gameObject, false);                        // 덤불 그림 → 없앰
            else if (r.name.Contains("Terrain")) Swap(r, sand);                               // 땅 → 모래 언덕
            else if (mats.Contains("TreeTrunk"))                                               // 전나무 → 선인장으로 교체
            {
                trees.Add(MeshBounds(r)); // 자식으로 붙은 나무도 각자 다른 자리에 선 한 그루 → 각각 선인장 하나
                SetActive(r.gameObject, false);
            }
            else if (mats.Contains("FirTree")) Swap(r, grass);                                // 풀 → 마른 풀
            else if (mats.Contains("Rock")) Swap(r, stone);                                   // 바위 → 붉은 사암
            // 나무 울타리는 그대로 (사막 목장 느낌)
        }

        // 전나무가 있던 자리에 같은 높이의 선인장
        int seed = 0;
        foreach (Bounds b in trees)
            MakeCactus(props, new Vector3(b.center.x, b.min.y, b.center.z), b.size.y * 0.8f, cactus, seed++);

        // 지평선의 메사 (꼭대기가 평평한 붉은 바위산). 배경과 함께 새를 따라다녀서 늘 멀리 보임
        Vector3 start = FindBird().transform.position;
        // 키가 크고 옆면이 가파른 바위산이 멀리 지평선에 (가까우면 납작한 케이크처럼 보임)
        var mesas = new[]
        {
            new Vector4(-420, 900, 110, 330), new Vector4(-150, 1250, 170, 420), new Vector4(120, 1400, 130, 360),
            new Vector4(380, 1000, 140, 380), new Vector4(650, 1300, 190, 450), new Vector4(-700, 1200, 160, 400),
            new Vector4(-620, 600, 90, 260),  new Vector4(600, 650, 100, 280),
        };
        for (int i = 0; i < mesas.Length; i++)
        {
            Vector4 m = mesas[i]; // x, z, 반지름, 높이
            MakeMesa(props, start + new Vector3(m.x, -80f, m.y), m.z, m.w, stone, band, i);
        }
    }

    // 몸통 + 양쪽 팔 (팔은 옆으로 나갔다가 위로 꺾임)
    static void MakeCactus(Transform parent, Vector3 basePos, float height, Material mat, int seed)
    {
        Random.InitState(1000 + seed);
        Transform c = new GameObject("Cactus").transform;
        c.SetParent(parent, true);
        c.position = basePos;
        c.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        float w = height * 0.16f;
        Part(PrimitiveType.Capsule, c, mat, new Vector3(0, height * 0.5f, 0), new Vector3(w, height * 0.5f, w));
        for (int side = -1; side <= 1; side += 2)
        {
            float armY = height * Random.Range(0.35f, 0.6f);
            float armLen = height * Random.Range(0.18f, 0.26f);
            float armUp = height * Random.Range(0.18f, 0.3f);
            float aw = w * 0.75f;
            Part(PrimitiveType.Capsule, c, mat, new Vector3(side * armLen * 0.5f, armY, 0), new Vector3(aw, armLen * 0.5f, aw), new Vector3(0, 0, 90));
            Part(PrimitiveType.Capsule, c, mat, new Vector3(side * armLen, armY + armUp * 0.5f, 0), new Vector3(aw, armUp * 0.5f + aw * 0.5f, aw));
        }
    }

    // 층층이 쌓인 원통: 아래는 넓고 위로 갈수록 살짝 좁아짐, 띠 색을 번갈아
    static void MakeMesa(Transform parent, Vector3 basePos, float radius, float height, Material stone, Material band, int seed)
    {
        Random.InitState(2000 + seed);
        Transform m = new GameObject("Mesa").transform;
        m.SetParent(parent, true);
        m.position = basePos;
        m.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        // 바닥: 무너져 내린 돌 더미처럼 넓고 낮은 받침 두 겹
        Part(PrimitiveType.Cylinder, m, band, new Vector3(0, height * 0.04f, 0), new Vector3(radius * 3.2f, height * 0.04f, radius * 2.9f));
        Part(PrimitiveType.Cylinder, m, stone, new Vector3(0, height * 0.11f, 0), new Vector3(radius * 2.7f, height * 0.04f, radius * 2.5f));

        // 몸통: 층마다 높이·폭·위치가 조금씩 달라 통처럼 보이지 않게, 위로 갈수록 좁아짐
        const int layers = 5;
        float y = height * 0.14f;
        float squash = Random.Range(0.7f, 0.95f); // 완전 원형이 아니게
        for (int i = 0; i < layers; i++)
        {
            float h = (height * 0.86f) / layers * Random.Range(0.6f, 1.4f);
            float r = radius * Mathf.Lerp(1.15f, 0.7f, i / (float)(layers - 1)) * Random.Range(0.92f, 1.08f);
            Vector3 jitter = new Vector3(Random.Range(-0.06f, 0.06f), 0, Random.Range(-0.06f, 0.06f)) * radius;
            Part(PrimitiveType.Cylinder, m, i % 2 == 0 ? stone : band, jitter + new Vector3(0, y + h * 0.5f, 0), new Vector3(r * 2f, h * 0.5f, r * 2f * squash));
            y += h;
        }
    }

    static void Part(PrimitiveType type, Transform parent, Material mat, Vector3 localPos, Vector3 localScale, Vector3? euler = null)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(g.GetComponent<Collider>()); // 배경 소품: 충돌 없음 ("Cube" 이름 판정에도 안 걸리게)
        g.name = type.ToString();
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = localScale;
        if (euler.HasValue) g.transform.localEulerAngles = euler.Value;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ───────────────────────── 하늘 · 빛 ─────────────────────────

    static void SetDesertLighting()
    {
        // 낮 하늘
        Material day = AssetDatabase.LoadAssetAtPath<Material>("Assets/BOXOPHOBIC/Skybox Cubemap Extended/Demo/Materials/Skybox Cubemap Extended Day.mat");
        if (day != null) RenderSettings.skybox = day;

        // 머리 위에서 내리쬐는 뜨거운 햇빛 (숲은 노랗고 수평으로 비치는 노을빛)
        Light sun = GameObject.Find("Directional Light").GetComponent<Light>();
        Undo.RecordObject(sun, "Desert Stage");
        Undo.RecordObject(sun.transform, "Desert Stage");
        sun.color = new Color(1f, 0.95f, 0.84f);
        sun.intensity = 1.5f;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 멀리 갈수록 모래빛 아지랑이
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.93f, 0.82f, 0.63f);
        RenderSettings.fogStartDistance = 150f;
        RenderSettings.fogEndDistance = 2200f; // 먼 바위산도 아지랑이 속에 은은하게 보이게

        // 지평선의 바위산(최대 약 1500 거리)까지 그리도록 카메라 시야 거리 늘림
        Camera cam = GameObject.Find("Cube/Main Camera").GetComponent<Camera>();
        Undo.RecordObject(cam, "Desert Stage");
        cam.farClipPlane = 2500f;

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(sun.gameObject.scene);
    }

    // ───────────────────────── 공용 ─────────────────────────

    // 꺼져 있는 오브젝트는 Renderer.bounds가 비어 있어서, 메쉬 크기를 직접 월드로 바꿔 계산 (다시 실행해도 같은 결과)
    static Bounds MeshBounds(Renderer r)
    {
        MeshFilter mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return r.bounds;
        Bounds local = mf.sharedMesh.bounds;
        Bounds world = new Bounds(r.transform.TransformPoint(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            world.Encapsulate(r.transform.TransformPoint(corner));
        }
        return world;
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
        Undo.RecordObject(g, "Desert Stage");
        g.SetActive(on);
    }

    static void Swap(Renderer r, Material m)
    {
        Undo.RecordObject(r, "Desert Stage");
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) mats[i] = m;
        r.sharedMaterials = mats;
    }

    static Material Lit(string name, Color color, float smoothness)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }
}
