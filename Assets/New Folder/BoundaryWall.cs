using UnityEngine;

public class BoundaryWall : MonoBehaviour
{
    public float limitX = 34.5f; // 화면 끝 가장자리 위치
    public float warningDistance = 15f; // 이 거리 안으로 들어오면 쉴드가 서서히 보이기 시작함
    
    // 🔥 아래 프로젝트 창에 있는 쉴드 매테리얼을 끌어다 넣을 빈칸!
    public Material shieldMaterial; 

    private GameObject leftWall;
    private GameObject rightWall;
    private MeshRenderer leftRenderer;
    private MeshRenderer rightRenderer;
    
    private Material leftMat;
    private Material rightMat;

    // 쉴드 밝기 (1 = 에셋 원래 밝기, 원래는 너무 진해서 낮춤)
    [Range(0f, 1f)] public float shieldBrightness = 0.12f;

    // 천장 쉴드: 높이는 BirdController.ceilingY를 따름, 가까워지면 서서히 보임
    public float ceilingWarningDistance = 6f;
    public float ceilingAreaScale = 10f; // 천장 쉴드 면적 배율 (가로·세로 각각 √배)
    private GameObject ceiling;
    private MeshRenderer ceilingRenderer;
    private Material ceilingMat;
    private BirdController bird;

    void Start()
    {
        bird = GetComponent<BirdController>();
        ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.name = "CeilingShield";
        float side = Mathf.Sqrt(ceilingAreaScale); // 면적 10배 = 가로·세로 각각 약 3.16배
        ceiling.transform.localScale = new Vector3((limitX * 2f + 2f) * side, 1, 100 * side);
        Destroy(ceiling.GetComponent<BoxCollider>());
        ceilingRenderer = ceiling.GetComponent<MeshRenderer>();
        if (shieldMaterial != null)
        {
            ceilingRenderer.material = shieldMaterial;
            ceilingMat = ceilingRenderer.material;
            // 커진 만큼 무늬도 촘촘하게 반복 → 육각 무늬가 크게 늘어나 보이지 않게
            if (ceilingMat.HasProperty("_Tiling"))
                ceilingMat.SetVector("_Tiling", ceilingMat.GetVector("_Tiling") * side);
        }

        // 1. 왼쪽 쉴드 생성
        leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftWall.transform.localScale = new Vector3(1, 100, 100);
        Destroy(leftWall.GetComponent<BoxCollider>());
        leftRenderer = leftWall.GetComponent<MeshRenderer>();
        
        // 2. 오른쪽 쉴드 생성
        rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightWall.transform.localScale = new Vector3(1, 100, 100);
        Destroy(rightWall.GetComponent<BoxCollider>());
        rightRenderer = rightWall.GetComponent<MeshRenderer>();

        // 3. 우리가 고른 멋진 쉴드 매테리얼 입혀주기
        if (shieldMaterial != null)
        {
            leftRenderer.material = shieldMaterial;
            rightRenderer.material = shieldMaterial;
            
            // 코드로 투명도를 조절하기 위해 복사본 저장
            leftMat = leftRenderer.material;
            rightMat = rightRenderer.material;
        }
    }

    // 컷씬 카메라에 육각 무늬 쉴드가 크게 비치지 않게 숨김 (도시 헬기 컷씬)
    public void HideShields()
    {
        if (leftWall != null) leftWall.SetActive(false);
        if (rightWall != null) rightWall.SetActive(false);
        if (ceiling != null) ceiling.SetActive(false);
        enabled = false;
    }

    void LateUpdate()
    {
        // 쉴드가 항상 플레이어 양옆을 따라다니게 고정
        leftWall.transform.position = new Vector3(-limitX, transform.position.y, transform.position.z);
        rightWall.transform.position = new Vector3(limitX, transform.position.y, transform.position.z);

        // 🔥 거리에 따라 투명도(Alpha) 서서히 조절하기
        if (leftMat != null && rightMat != null)
        {
            // 왼쪽 쉴드와 플레이어 사이의 거리 계산
            float distToLeft = Mathf.Abs(-limitX - transform.position.x);
            // 거리가 가까워질수록 0에서 1로 찌-잉 하고 진해지는 수학 공식
            float alphaLeft = Mathf.Clamp01(1f - (distToLeft / warningDistance));
            SetMaterialAlpha(leftMat, alphaLeft);

            // 오른쪽 쉴드와 플레이어 사이의 거리 계산
            float distToRight = Mathf.Abs(limitX - transform.position.x);
            float alphaRight = Mathf.Clamp01(1f - (distToRight / warningDistance));
            SetMaterialAlpha(rightMat, alphaRight);
            
            // 쉴드가 완전히 투명할 땐(알파 0) 렌더러를 꺼서 컴퓨터 메모리 아끼기
            leftRenderer.enabled = (alphaLeft > 0);
            rightRenderer.enabled = (alphaRight > 0);
        }

        // 천장 쉴드: 새 머리 위를 따라다니고, 천장에 가까워질수록 진해짐
        if (bird != null)
        {
            // 카메라가 새보다 2 위에 있어서, 쉴드는 그보다 더 위에 둬야 아래에서 올려다보는 지붕으로 보임
            ceiling.transform.position = new Vector3(0f, bird.ceilingY + 3f, transform.position.z);
            if (ceilingMat != null)
            {
                float alphaTop = Mathf.Clamp01(1f - (bird.ceilingY - transform.position.y) / ceilingWarningDistance);
                SetMaterialAlpha(ceilingMat, alphaTop);
                ceilingRenderer.enabled = alphaTop > 0;
            }
        }

        // 벽 밖으로 못 나가게 고정 (원래 있던 기능)
        if (transform.position.x < -limitX)
        {
            transform.position = new Vector3(-limitX, transform.position.y, transform.position.z);
        }
        else if (transform.position.x > limitX)
        {
            transform.position = new Vector3(limitX, transform.position.y, transform.position.z);
        }
    }

    // 쉴드 에셋의 종류(이름)에 상관없이 투명도를 강제로 조절해 주는 만능 함수
    void SetMaterialAlpha(Material mat, float alpha)
    {
        // 지금 쓰는 홀로그램 쉴드는 색 자체가 빛(HDR)이라, 알파 대신 색 세기를 줄여야 흐려짐
        // 원래 색은 에셋(shieldMaterial)에서 읽어서 매번 같은 기준으로 계산
        if (mat.HasProperty("_Color_A"))
        {
            float k = alpha * shieldBrightness;
            mat.SetColor("_Color_A", shieldMaterial.GetColor("_Color_A") * k);
            if (mat.HasProperty("_Texture_1_Color")) mat.SetColor("_Texture_1_Color", shieldMaterial.GetColor("_Texture_1_Color") * k);
        }
        else if (mat.HasProperty("_Color"))
        {
            Color c = mat.color; c.a = alpha; mat.color = c;
        }
        else if (mat.HasProperty("_TintColor"))
        {
            Color c = mat.GetColor("_TintColor"); c.a = alpha; mat.SetColor("_TintColor", c);
        }
        else if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor"); c.a = alpha; mat.SetColor("_BaseColor", c);
        }
    }
}