using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

// 사막 보스 독수리 준비: 머티리얼 + 씬에 EagleChase 배치 + 남은 시간 막대 UI + 연결
// 사막 씬을 연 채로 실행. 여러 번 실행해도 중복 생성 안 됨
// 메뉴: Tools > Setup Eagle Boss (Stage 2)
public static class EagleSetup
{
    [MenuItem("Tools/Setup Eagle Boss (Stage 2)")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }
        Build();
        GameUIStyler.Apply(); // 메뉴의 STAGE 2 BOSS 버튼 + 클리어 문구 (씬 저장까지 함)
        Debug.Log("독수리 보스 준비 완료");
    }

    // 씬 저장 없이 배치·연결만 (DesertSetup에서도 호출)
    public static void Build()
    {
        System.IO.Directory.CreateDirectory("Assets/Boss/Materials");
        BirdController bird = GameObject.Find("Cube").GetComponent<BirdController>();
        NewMonoBehaviourScript spawner = Object.FindAnyObjectByType<NewMonoBehaviourScript>();

        GameObject go = GameObject.Find("EagleChase");
        if (go == null)
        {
            go = new GameObject("EagleChase");
            Undo.RegisterCreatedObjectUndo(go, "Eagle Boss");
        }
        EagleChase eagle = go.GetComponent<EagleChase>();
        if (eagle == null) eagle = Undo.AddComponent<EagleChase>(go);
        Undo.RecordObject(eagle, "Eagle Boss");
        eagle.bird = bird;
        eagle.obstacleSpawner = spawner;

        // 흰머리독수리: 진갈색 몸·날개, 밝은 갈색 날개 띠, 흰 머리·꼬리, 노란 부리·발
        eagle.bodyMat = BossSetup.Lit("Eagle_Body", new Color(0.33f, 0.22f, 0.14f), 0.25f);
        eagle.wingMat = BossSetup.Lit("Eagle_Wing", new Color(0.27f, 0.18f, 0.115f), 0.25f);
        eagle.covertMat = BossSetup.Lit("Eagle_Covert", new Color(0.47f, 0.33f, 0.2f), 0.25f);
        eagle.primaryMat = BossSetup.Lit("Eagle_Primary", new Color(0.14f, 0.095f, 0.065f), 0.3f);
        eagle.whiteMat = BossSetup.Lit("Eagle_White", new Color(0.96f, 0.95f, 0.91f), 0.3f);
        eagle.beakMat = BossSetup.Lit("Eagle_Beak", new Color(0.98f, 0.74f, 0.16f), 0.55f);
        eagle.eyeMat = BossSetup.Lit("Eagle_Eye", new Color(1f, 0.8f, 0.1f), 0.85f, new Color(0.45f, 0.3f, 0f));
        eagle.pupilMat = BossSetup.Lit("Eagle_Pupil", new Color(0.03f, 0.02f, 0.02f), 0.9f);
        eagle.clawMat = BossSetup.Lit("Eagle_Claw", new Color(0.08f, 0.07f, 0.07f), 0.6f);
        eagle.telegraphMat = BossSetup.Unlit("AttackLine_Red", new Color(1f, 0.12f, 0.12f, 0.25f), true);
        eagle.trailMat = BossSetup.Unlit("Eagle_Trail", new Color(1f, 0.97f, 0.9f, 0.55f), true);

        eagle.bossBgm = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EagleBGM.wav"); // 전용 추격전 BGM (make_eagle_bgm.py)
        eagle.surviveSeconds = 40f;
        eagle.eagleScale = 1.8f;
        eagle.attackRange = 1.3f;
        eagle.telegraphTime = 1f;
        // 추격전 바닥: 사막 스테이지와 같은 모래·선인장·사암 색
        eagle.sandMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Stages/Desert/Materials/Desert_Sand.mat");
        eagle.cactusMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Stages/Desert/Materials/Desert_Cactus.mat");
        eagle.rockMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Stages/Desert/Materials/Desert_Sandstone.mat");
        eagle.screechSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EagleScreech.wav");
        eagle.whooshSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EagleWhoosh.wav");
        eagle.titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        eagle.titleMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/UI/GameText.mat");

        // 화면 위 가운데: 독수리 아이콘 + "SAND EAGLE 35s" + 줄어드는 남은 시간 막대 (금색)
        Transform canvas = GameObject.Find("Canvas").transform;
        GameObject hud; RectTransform fill; TextMeshProUGUI countdown, nameText;
        GameUIStyler.BuildBossHud(canvas, "Icon_Eagle", new Color(0.98f, 0.95f, 0.88f), "SAND EAGLE", new Color(0.98f, 0.72f, 0.30f), false,
                                  out hud, out fill, out countdown, out nameText);
        eagle.bossHud = hud;
        eagle.timeFill = fill;
        eagle.bossName = nameText;
        EditorUtility.SetDirty(eagle);

        Undo.RecordObject(bird, "Eagle Boss");
        bird.eagleChase = eagle;
        bird.bossFight = null;
        EditorUtility.SetDirty(bird);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(go.scene);
    }
}
