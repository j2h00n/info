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

    void Start()
    {
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
        if (mat.HasProperty("_Color"))
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