using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 갈매기 모델엔 부리·눈이 없어서 정면(독수리 추격전 시점)에서 보면 밋밋함 → 노란 부리 + 까만 점눈을 붙임
// 몸통 뼈(Root)에 붙여서 몸과 같이 움직임. 빌드에 들어간 모든 스테이지 씬에 적용 (여러 번 실행해도 중복 안 됨)
// 메뉴: Tools > Add Seagull Face
public static class SeagullFaceSetup
{
    const string BeakMeshPath = "Assets/Boss/Materials/Seagull_Beak.asset";

    [MenuItem("Tools/Add Seagull Face")]
    public static void AddToAllStages()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }
        EditorSceneManager.SaveOpenScenes();
        string current = EditorSceneManager.GetActiveScene().path;
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled) continue;
            EditorSceneManager.OpenScene(s.path);
            Add();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }
        if (!string.IsNullOrEmpty(current)) EditorSceneManager.OpenScene(current);
        Debug.Log("갈매기 부리·눈 추가 완료");
    }

    static void Add()
    {
        Transform gull = GameObject.Find("Cube").transform.Find("Seagull");
        Transform root = null;
        foreach (Transform t in gull.GetComponentsInChildren<Transform>(true)) if (t.name == "Root") root = t;
        if (root == null) { Debug.LogWarning("갈매기 Root 뼈를 못 찾았어요."); return; }

        Transform old = root.Find("Face");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // 얼굴 기준점: 둥근 몸 앞쪽 끝(코) 바로 안쪽. 축은 갈매기 기준 (+z 앞, +y 위)
        GameObject face = new GameObject("Face");
        face.transform.SetParent(root, false);
        face.transform.position = gull.TransformPoint(new Vector3(0f, 0.37f, 0.33f));
        face.transform.rotation = gull.rotation;

        Material beakMat = BossSetup.Lit("Seagull_Beak", new Color(1f, 0.76f, 0.2f), 0.5f);
        Material eyeMat = BossSetup.Lit("Seagull_Eye", new Color(0.03f, 0.03f, 0.04f), 0.9f);

        // 부리: 몸에 묻힌 뿌리에서 앞으로 뻗어 끝이 살짝 아래로
        GameObject beak = new GameObject("Beak");
        beak.transform.SetParent(face.transform, false);
        beak.AddComponent<MeshFilter>().sharedMesh = BeakMesh();
        MeshRenderer br = beak.AddComponent<MeshRenderer>();
        br.sharedMaterial = beakMat;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 점눈: 부리 위 양옆으로 동그란 까만 점
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(eye.GetComponent<Collider>()); // 이름이 "Cube"류 판정에 안 걸리게, 충돌도 없음
            eye.name = side < 0 ? "Eye_L" : "Eye_R";
            eye.transform.SetParent(face.transform, false);
            eye.transform.localPosition = new Vector3(side * 0.13f, 0.13f, -0.05f);
            eye.transform.localScale = new Vector3(0.08f, 0.09f, 0.06f);
            Renderer er = eye.GetComponent<Renderer>();
            er.sharedMaterial = eyeMat;
            er.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        EditorSceneManager.MarkSceneDirty(face.scene);
    }

    static Mesh BeakMesh()
    {
        var b = new MeshKit();
        b.Loft(new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, -0.01f, 0.12f), new Vector3(0f, -0.04f, 0.22f), new Vector3(0f, -0.07f, 0.27f) },
               new[] { 0.085f, 0.06f, 0.035f, 0.005f },
               new[] { 0.065f, 0.05f, 0.03f, 0.005f }, 8);
        Mesh mesh = b.ToMesh("Seagull_Beak");
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(BeakMeshPath);
        if (existing != null)
        {
            existing.Clear();
            existing.SetVertices(mesh.vertices);
            existing.SetTriangles(mesh.triangles, 0);
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, BeakMeshPath);
        return mesh;
    }
}
