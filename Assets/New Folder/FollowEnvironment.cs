using UnityEngine;

public class FollowEnvironment : MonoBehaviour
{
    public Transform player; // 따라다닐 대상 (새)
    private Vector3 offset; // 처음 시작할 때의 간격(거리)

    void Start()
    {
        // 게임이 시작될 때, 플레이어와 이 배경 사이의 초기 거리를 기억해 둡니다.
        if (player != null)
        {
            offset = transform.position - player.position;
        }
    }

    void LateUpdate()
    {
        // 매 프레임마다 플레이어의 위치에 처음 기억한 거리를 더해서 그대로 따라갑니다.
        if (player != null)
        {
            transform.position = player.position + offset;
        }
    }
}