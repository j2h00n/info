using UnityEngine;

// 보스 패턴의 오물/돌진/아이템 판정용. 플레이어(새)가 닿으면 onPlayerEnter 호출
public class BossTrigger : MonoBehaviour
{
    public System.Action onPlayerEnter;

    void OnTriggerEnter(Collider other)
    {
        if (onPlayerEnter != null && other.GetComponentInParent<BirdController>() != null)
        {
            onPlayerEnter();
        }
    }
}
