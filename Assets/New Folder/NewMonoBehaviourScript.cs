using UnityEngine;
using System.Collections.Generic;

public class NewMonoBehaviourScript : MonoBehaviour
{
    public Transform player;
    private float nextChunkZ = 20f;
    private float chunkSpacing = 15f; // 장애물 줄 간격
    
    private Queue<List<GameObject>> activeChunks = new Queue<List<GameObject>>();

    void Update()
    {
        if (player == null) return;

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
}