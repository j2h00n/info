using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 1스테이지 스토리 시작 연출(알 깨고 나오기) 준비: 씬에 HatchIntro 배치 + 머티리얼·소리 연결
// 1스테이지(숲) 씬을 연 채로 실행. 여러 번 실행해도 중복 생성 안 됨
// 메뉴: Tools > Setup Hatch Intro (Stage 1)
public static class HatchSetup
{
    [MenuItem("Tools/Setup Hatch Intro (Stage 1)")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }
        System.IO.Directory.CreateDirectory("Assets/Boss/Materials");
        BirdController bird = GameObject.Find("Cube").GetComponent<BirdController>();

        GameObject go = GameObject.Find("HatchIntro");
        if (go == null)
        {
            go = new GameObject("HatchIntro");
            Undo.RegisterCreatedObjectUndo(go, "Hatch Intro");
        }
        HatchIntro hatch = go.GetComponent<HatchIntro>();
        if (hatch == null) hatch = Undo.AddComponent<HatchIntro>(go);
        Undo.RecordObject(hatch, "Hatch Intro");
        hatch.bird = bird;
        hatch.obstacleSpawner = Object.FindAnyObjectByType<NewMonoBehaviourScript>();

        // 크림색 알 + 짙은 갈색 금, 둥지는 밝고 어두운 잔가지
        hatch.eggMat = BossSetup.Lit("Egg_Shell", new Color(0.98f, 0.94f, 0.84f), 0.35f);
        hatch.crackMat = BossSetup.Unlit("Egg_Crack", new Color(0.25f, 0.16f, 0.1f), false);
        hatch.nestMat = BossSetup.Lit("Nest_Twig", new Color(0.55f, 0.38f, 0.22f), 0.1f);
        hatch.nestDarkMat = BossSetup.Lit("Nest_TwigDark", new Color(0.36f, 0.24f, 0.14f), 0.1f);

        hatch.tapSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EggTap.wav");
        hatch.crackSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EggCrack.wav");
        hatch.chirpSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game Sound Solutions - 8 bits Elements/OTHER SOUND PACK DEMO/Fantasy Creatures Demo/bird_2.wav");
        EditorUtility.SetDirty(hatch);

        Undo.RecordObject(bird, "Hatch Intro");
        bird.hatchIntro = hatch;
        EditorUtility.SetDirty(bird);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
        Debug.Log("알 깨기 시작 연출 준비 완료");
    }
}
