using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;

public class MotionController : MonoBehaviour
{
    private Thread receiveThread;
    private UdpClient client;
    private bool isRunning = false;
    
    [HideInInspector] public float headX = 0.5f;
    [HideInInspector] public float headY = 0.5f;

    private float baselineX = 0.5f; 
    private float baselineY = 0.5f; 
    
    // 🔥 반응 속도를 훨씬 빠르고 부드럽게 상향 조정!
    public float learningRate = 5.0f; 

    private float jumpThreshold = 0.02f; 
    private float lastHeadY = 0.5f;

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
            }
        }
        catch (System.Exception) { }
    }

    void Update()
    {
        // 실시간 고개 위치를 빠르게 추적하되 튀지 않게 보정
        baselineX = Mathf.Lerp(baselineX, headX, Time.deltaTime * learningRate);
        baselineY = Mathf.Lerp(baselineY, headY, Time.deltaTime * learningRate);

        float headVelocityY = headY - lastHeadY;
        lastHeadY = headY;

        if (headVelocityY > jumpThreshold)
        {
            if (birdController != null)
            {
                birdController.TriggerJump(); 
            }
            
            jumpThreshold = Mathf.Lerp(jumpThreshold, headVelocityY * 0.7f, 0.1f); 
            jumpThreshold = Mathf.Max(0.015f, jumpThreshold); 
        }
    }

    public float GetMotionMoveX()
    {
        // 🔥 좌우 감도를 시원시원하게 키워서 고개만 살짝 움직여도 확 확 휘어지도록 수정!
        float motionMoveX = (headX - baselineX) * -8.0f; 
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