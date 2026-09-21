using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;

public class MotionController : MonoBehaviour
{
    // 프로토콜: "x,y,angleDeg,jump" (파이썬 motion_sender.py 쪽 참고)
    // - x,y: 코 위치 정규화 좌표(0~1, 좌우이동엔 안 씀 - 참고/디버그용으로만 유지)
    // - angleDeg: 귀-귀 선 기울기 각도. 조향은 이제 "머리 위치"가 아니라 "머리 각도" 기반
    // - jump: 0/1. 파이썬 쪽에서 머리 옆 손 파닥임을 감지해 직접 보내주는 신호.
    //   (기존의 헤드 Y속도 스파이크로 점프 추정하던 방식은 정확도가 낮아서 폐기)
    private Thread receiveThread;
    private UdpClient client;
    private bool isRunning = false;

    [HideInInspector] public float headX = 0.5f;
    [HideInInspector] public float headY = 0.5f;
    [HideInInspector] public float headAngleDeg = 0f;

    public float maxTiltDeg = 20f; // 파이썬 motion_sender.py의 MAX_TILT_DEG와 맞출 것
    private volatile bool jumpPending = false;

    private BirdController birdController;

    void Start()
    {
        birdController = GetComponent<BirdController>();
        InitUDP();
    }

    private void InitUDP()
    {
        isRunning = true;
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    private void ReceiveData()
    {
        try
        {
            client = new UdpClient(5052);
            while (isRunning)
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);
                string[] splitData = text.Split(',');

                headX = float.Parse(splitData[0], CultureInfo.InvariantCulture);
                headY = float.Parse(splitData[1], CultureInfo.InvariantCulture);
                if (splitData.Length >= 3)
                {
                    headAngleDeg = float.Parse(splitData[2], CultureInfo.InvariantCulture);
                }
                if (splitData.Length >= 4 && splitData[3].Trim() == "1")
                {
                    jumpPending = true;
                }
            }
        }
        catch (System.Exception) { }
    }

    void Update()
    {
        if (jumpPending)
        {
            jumpPending = false;
            if (birdController != null)
            {
                birdController.TriggerJump();
            }
        }
    }

    public float GetMotionMoveX()
    {
        float motionMoveX = headAngleDeg / maxTiltDeg;
        return Mathf.Clamp(motionMoveX, -1f, 1f);
    }

    void StopUDP()
    {
        isRunning = false;
        try
        {
            if (client != null) { client.Close(); client = null; }
            if (receiveThread != null && receiveThread.IsAlive) { receiveThread.Abort(); }
        }
        catch { }
    }

    void OnDisable() { StopUDP(); }
    void OnDestroy() { StopUDP(); }
    void OnApplicationQuit() { StopUDP(); }
}