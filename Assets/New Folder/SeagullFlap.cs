using UnityEngine;

// 갈매기 날개 뼈를 코드로 움직여서 파닥거리게 함
// 평소엔 천천히 날갯짓, 점프(Flap 호출)하면 잠깐 빠르고 크게 파닥파닥
public class SeagullFlap : MonoBehaviour
{
    public float idleFlapsPerSecond = 2.5f;  // 평소 날갯짓 속도
    public float burstFlapsPerSecond = 7f;   // 점프 직후 날갯짓 속도
    public float burstDuration = 0.4f;       // 빠른 날갯짓 지속 시간
    public float idleAngle = 30f;            // 평소 어깨 각도 폭
    public float burstAngle = 50f;           // 점프 직후 어깨 각도 폭
    public float wingTipFollow = 0.6f;       // 날개 끝이 어깨를 얼마나 따라 휘는지 (0~1)

    private Transform shoulderL, shoulderR, wingL, wingR;
    private Quaternion restShoulderL, restShoulderR, restWingL, restWingR;
    private float phase;
    private float burstTimer;

    void Start()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Shoulder_L") shoulderL = t;
            else if (t.name == "Shoulder_R") shoulderR = t;
            else if (t.name == "Wing_L") wingL = t;
            else if (t.name == "Wing_R") wingR = t;
        }
        if (shoulderL == null || shoulderR == null || wingL == null || wingR == null)
        {
            Debug.LogWarning("SeagullFlap: 날개 뼈(Shoulder_L/R, Wing_L/R)를 못 찾았어요.");
            enabled = false;
            return;
        }
        restShoulderL = shoulderL.localRotation;
        restShoulderR = shoulderR.localRotation;
        restWingL = wingL.localRotation;
        restWingR = wingR.localRotation;
    }

    // 점프할 때 BirdController가 호출
    public void Flap()
    {
        burstTimer = burstDuration;
    }

    void LateUpdate()
    {
        // 시작 메뉴(Time.timeScale = 0)에서도 날갯짓하도록 unscaled 시간 사용
        float dt = Time.unscaledDeltaTime;
        burstTimer = Mathf.Max(0f, burstTimer - dt);
        float burst = burstTimer / burstDuration; // 1 → 0 으로 서서히 평소 날갯짓으로 돌아옴

        float speed = Mathf.Lerp(idleFlapsPerSecond, burstFlapsPerSecond, burst);
        float amount = Mathf.Lerp(idleAngle, burstAngle, burst);
        phase += dt * speed * Mathf.PI * 2f;

        // 위로 올릴 때보다 아래로 내려칠 때 살짝 덜 내려가게 (+10도 위로 치우침)
        float shoulder = Mathf.Sin(phase) * amount + 10f;
        // 날개 끝은 어깨보다 조금 늦게 따라와서 채찍처럼 휘어짐
        float tip = Mathf.Sin(phase - 0.9f) * amount * wingTipFollow;

        // 두 날개 모두 로컬 Y축 + 방향이 위쪽
        shoulderL.localRotation = restShoulderL * Quaternion.AngleAxis(shoulder, Vector3.up);
        shoulderR.localRotation = restShoulderR * Quaternion.AngleAxis(shoulder, Vector3.up);
        wingL.localRotation = restWingL * Quaternion.AngleAxis(tip, Vector3.up);
        wingR.localRotation = restWingR * Quaternion.AngleAxis(tip, Vector3.up);
    }
}
