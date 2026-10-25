using UnityEngine;
using System.Collections.Generic;

public class NewMonoBehaviourScript : MonoBehaviour
{
    public Transform player;
    public const float FirstChunkZ = 20f;
    public float chunkSpacing = 24f;       // 장애물 줄 간격 (숲·사막 24 = 얼음 동굴의 4/5, 얼음 동굴은 얼음 이벤트 때문에 30)
    public static float Spacing = 24f;     // 지금 씬의 벽 간격 (벽 개수 세기 / 얼음 이벤트에서 씀)
    private float nextChunkZ = FirstChunkZ;

    void Awake() { Spacing = chunkSpacing; }

    // 🔥 배경과 어울리는 로우폴리 외형 (비워두면 기존 하얀 큐브 그대로)
    public GameObject[] rockPrefabs;   // 쌓아서 기둥을 만들 바위
    public GameObject[] grassPrefabs;  // 아래 기둥 꼭대기에 얹을 풀

    private Queue<List<GameObject>> activeChunks = new Queue<List<GameObject>>();

    private bool spawning = true;

    // 보스 등장: 장애물 생성을 멈추고 남은 바위들을 양옆으로 날려버림
    public void StopAndScatter()
    {
        spawning = false;
        List<GameObject> all = new List<GameObject>();
        foreach (List<GameObject> chunk in activeChunks)
            foreach (GameObject go in chunk)
                if (go != null) all.Add(go);
        activeChunks.Clear();
        StartCoroutine(Scatter(all));
    }

    System.Collections.IEnumerator Scatter(List<GameObject> rocks)
    {
        Vector3[] velocity = new Vector3[rocks.Count];
        Vector3[] spin = new Vector3[rocks.Count];
        for (int i = 0; i < rocks.Count; i++)
        {
            // 날아가는 동안 부딪혀도 안 죽게 판정 끄기
            foreach (Collider c in rocks[i].GetComponentsInChildren<Collider>()) c.enabled = false;

            float x = rocks[i].transform.position.x;
            float side = Mathf.Abs(x) < 1f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(x); // 왼쪽 것은 왼쪽으로, 오른쪽 것은 오른쪽으로
            velocity[i] = new Vector3(side * Random.Range(30f, 50f), Random.Range(5f, 15f), Random.Range(-5f, 10f));
            spin[i] = Random.insideUnitSphere * 400f;
        }

        float t = 0f;
        while (t < 2.5f)
        {
            t += Time.deltaTime;
            for (int i = 0; i < rocks.Count; i++)
            {
                if (rocks[i] == null) continue;
                velocity[i] += Physics.gravity * Time.deltaTime;
                rocks[i].transform.position += velocity[i] * Time.deltaTime;
                rocks[i].transform.Rotate(spin[i] * Time.deltaTime, Space.World);
            }
            yield return null;
        }
        foreach (GameObject go in rocks) if (go != null) Destroy(go);
    }

    void Update()
    {
        if (player == null || !spawning) return;

        // 플레이어 앞쪽에 장애물 생성
        if (player.position.z + 60f > nextChunkZ)
        {
            SpawnPipes(nextChunkZ);
            nextChunkZ += chunkSpacing;
        }

        // 지나친 장애물 삭제 (메모리 정리)
        if (activeChunks.Count > 0)
        {
            List<GameObject> oldestChunk = activeChunks.Peek();
            if (oldestChunk.Count > 0 && oldestChunk[0].transform.position.z < player.position.z - 15f)
            {
                List<GameObject> chunkToRemove = activeChunks.Dequeue();
                foreach (GameObject pipe in chunkToRemove)
                {
                    Destroy(pipe);
                }
            }
        }
    }

    void SpawnPipes(float zPos)
    {
        List<GameObject> currentChunk = new List<GameObject>();
        
        // 1. 화면 양옆을 꽉 채우도록 기둥 개수를 9개로 넉넉하게 늘림
        int columnCount = 9; 
        
        // 2. 기둥 두께와 틈새(하얀 줄) 간격 설정
        float pillarWidth = 7f; // 기둥 하나의 넓이
        float gapThickness = 1f; // 🔥 하얗게 칠해주신 '세로 틈'의 두께 (원하시면 숫자 수정 가능!)
        
        float spacingX = pillarWidth + gapThickness; // 기둥 중심부터 다음 기둥 중심까지의 거리
        float startX = -((columnCount - 1) / 2f) * spacingX; // 정중앙을 기준으로 맨 왼쪽 기둥 위치 자동 계산

        for (int i = 0; i < columnCount; i++)
        {
            float xPos = startX + (i * spacingX);
            
            // 각 기둥마다 위아래 통과할 빈 공간의 높이를 랜덤으로 다르게 굴림
            float gapPosition = Random.Range(2f, 9f); 
            float gapSize = 3.5f;

            // 🔥 바위가 있으면 네모 기둥 대신 바위를 쌓아 만든 울퉁불퉁한 기둥
            if (rockPrefabs != null && rockPrefabs.Length > 0)
            {
                BuildRockColumn(xPos, zPos, gapPosition - gapSize, 1f, -12f, currentChunk);                 // 아래: 틈 → 땅속
                BuildRockColumn(xPos, zPos, gapPosition + gapSize, -1f, gapPosition + gapSize + 30f, currentChunk); // 위: 틈 → 하늘
                continue;
            }

            // 아래 막대기 조각 생성
            GameObject bottomPipe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bottomPipe.transform.position = new Vector3(xPos, gapPosition - gapSize - 15f, zPos);
            // 크기에 pillarWidth(7) 적용
            bottomPipe.transform.localScale = new Vector3(pillarWidth, 30, 2); 
            currentChunk.Add(bottomPipe);

            // 위 막대기 조각 생성
            GameObject topPipe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topPipe.transform.position = new Vector3(xPos, gapPosition + gapSize + 15f, zPos);
            topPipe.transform.localScale = new Vector3(pillarWidth, 30, 2);
            currentChunk.Add(topPipe);
        }

        activeChunks.Enqueue(currentChunk);
    }

    // 틈(tipY)에서 시작해 endY까지 바위를 겹겹이 쌓아 기둥 하나를 만듦
    // dir: 1이면 아래 기둥(바위 꼭대기가 위를 향함), -1이면 위 기둥(거꾸로 매달림)
    void BuildRockColumn(float x, float z, float tipY, float dir, float endY, List<GameObject> chunk)
    {
        float edgeY = tipY; // 지금까지 쌓은 바위의 틈 반대쪽 끝
        bool first = true;

        while (first || (dir > 0f ? edgeY > endY : edgeY < endY))
        {
            // 옆 기둥(간격 8)과 겹치도록 넓게 → 세로 이음새가 안 보임
            Vector3 size = new Vector3(Random.Range(8.5f, 10f), Random.Range(4f, 6f), Random.Range(3f, 4.5f));
            float jitterX = first ? 0f : Random.Range(-0.6f, 0.6f);

            GameObject rock = SpawnRock(chunk);
            float rockTipY = first ? tipY : edgeY + dir * size.y * 0.45f; // 아래 바위와 절반쯤 겹치게
            PlaceRock(rock, new Vector3(x + jitterX, rockTipY, z), size, dir);

            // 첫 바위 꼭대기에 풀
            if (first && dir > 0f) AddGrass(x, tipY, z, chunk);

            edgeY = rockTipY - dir * size.y;
            first = false;
        }
    }

    void AddGrass(float x, float tipY, float z, List<GameObject> chunk)
    {
        if (grassPrefabs == null || grassPrefabs.Length == 0) return;

        int grassCount = Random.Range(2, 4);
        for (int g = 0; g < grassCount; g++)
        {
            GameObject grass = Instantiate(grassPrefabs[Random.Range(0, grassPrefabs.Length)]);
            foreach (Collider c in grass.GetComponentsInChildren<Collider>()) Destroy(c); // 풀은 통과 가능
            grass.transform.position = new Vector3(x + Random.Range(-1.8f, 1.8f), tipY - 0.8f, z - 0.6f);
            grass.transform.rotation = Quaternion.Euler(270f, Random.Range(0f, 360f), 0f);
            grass.transform.localScale = Vector3.one * Random.Range(1.5f, 2.2f);
            chunk.Add(grass);
        }
    }

    GameObject SpawnRock(List<GameObject> chunk)
    {
        GameObject rock = Instantiate(rockPrefabs[Random.Range(0, rockPrefabs.Length)]);
        foreach (Collider c in rock.GetComponentsInChildren<Collider>()) Destroy(c);

        // 바위 모양 그대로 충돌 판정 → 보이는 곳 = 부딪히는 곳
        foreach (MeshFilter mf in rock.GetComponentsInChildren<MeshFilter>())
        {
            MeshCollider col = mf.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = mf.sharedMesh;
            col.convex = true;
            mf.gameObject.tag = "Obstacle";
        }
        foreach (MeshRenderer mr in rock.GetComponentsInChildren<MeshRenderer>()) Tint(mr);

        chunk.Add(rock);
        return rock;
    }

    // 로우폴리 바위(Z축이 위쪽인 메쉬)를 size 크기로 늘리고, 뾰족한 꼭대기가 정확히 tipY에 오게 배치
    // → 바위가 틈 안으로 절대 튀어나오지 않음
    void PlaceRock(GameObject rock, Vector3 tip, Vector3 size, float dir)
    {
        Bounds mesh = rock.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;

        // 270도: 메쉬 Z→월드 위 / 90도: 위아래 뒤집힘. 메쉬 Y→월드 앞뒤
        // 앞뒤 뒤집기 + 살짝 비틀기로 모양 다양하게 (90도 돌리면 넓적한 바위가 옆으로 얇아지므로 금지)
        float yaw = (Random.value < 0.5f ? 0f : 180f) + Random.Range(-15f, 15f);
        rock.transform.rotation = Quaternion.Euler(0f, yaw, 0f)
                                * Quaternion.Euler(dir > 0 ? 270f : 90f, 0f, 0f);
        rock.transform.localScale = new Vector3(size.x / mesh.size.x, size.z / mesh.size.y, size.y / mesh.size.z);

        rock.transform.position = tip - Vector3.up * dir * mesh.max.z * rock.transform.localScale.z;
    }

    // 바위 색 (스테이지마다 다르게: 숲 = 노을빛 아래 회색으로 보이는 차가운 톤, 사막 = 사암 주황)
    public Color rockTint = new Color(0.34f, 0.40f, 0.52f);
    void Tint(Renderer r)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor("_BaseColor", rockTint);
        r.SetPropertyBlock(block);
    }
}