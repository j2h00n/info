using UnityEngine;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;

// 웹캠 모션 조작 받기 (motion_tracker.py가 UDP 5052로 보냄: "얼굴x,얼굴y,누적날갯짓수,얼굴보임" 또는 "STATUS,상태")
//  - 고개 좌우 위치 → 좌우 이동 (가운데 자리에서 얼마나 벗어났는지)
//  - 손을 아래로 내리친 횟수가 늘어나면 → 날갯짓(점프)
// 모션 조작이 켜져 있으면 왼쪽 아래에 내 카메라 화면을 보여줌 (UDP 5053으로 오는 JPEG)
// 트래커 신호 받기: 씬이 바뀌어도(재시작, 스테이지 이동) 끊기지 않도록 게임 전체에서 한 번만 열고 계속 유지
// (예전엔 씬마다 포트를 닫았다 다시 열어서, 그 사이 트래커가 에러로 죽거나 받기가 멈춰 연결이 끊겼음)
static class MotionLink
{
    public static volatile float headX = 0.5f;
    public static volatile float headY = 0.5f;
    public static volatile bool faceVisible = false;
    public static volatile int flapCount = -1;   // 파이썬이 보낸 누적 날갯짓 수
    public static volatile string status = "";   // connecting / busy / nocam
    static long lastPacketTicks = 0;             // 마지막으로 무엇이든 받은 시각
    static byte[] latestFrame;                    // 받기 스레드가 넣고 Update에서 꺼내 씀
    static readonly object frameLock = new object();
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    static bool started;

    // 마지막 신호 뒤 지난 시간(초) → 오래되면 트래커가 꺼졌거나 멈춘 것
    public static float SecondsSincePacket
    {
        get { long t = Interlocked.Read(ref lastPacketTicks); return t == 0 ? float.MaxValue : (float)(clock.ElapsedTicks - t) / System.Diagnostics.Stopwatch.Frequency; }
    }

    public static void Start()
    {
        if (started) return;
        started = true;
        StartThread(5052, OnData);
        StartThread(5053, data => { lock (frameLock) latestFrame = data; });
    }

    public static byte[] TakeFrame()
    {
        lock (frameLock) { byte[] f = latestFrame; latestFrame = null; return f; }
    }

    static void StartThread(int port, System.Action<byte[]> handle)
    {
        Thread t = new Thread(() => Receive(port, handle));
        t.IsBackground = true; // 게임을 끄면 같이 끝남
        t.Start();
    }

    static void Receive(int port, System.Action<byte[]> handle)
    {
        UdpClient client = null;
        while (true)
        {
            try
            {
                if (client == null)
                {
                    client = new UdpClient();
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    client.Client.ReceiveBufferSize = 1 << 20; // 카메라 화면이 몰려와도 안 버리게 넉넉히
                    try { client.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch { } // SIO_UDP_CONNRESET 끄기
                    client.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
                }
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref any);
                Interlocked.Exchange(ref lastPacketTicks, clock.ElapsedTicks);
                handle(data);
            }
            catch (System.Exception)
            {
                // 이상한 신호 하나, 순간적인 소켓 에러로 받기가 영영 멈추지 않게: 잠깐 쉬고 소켓을 다시 열어서 계속
                try { if (client != null) client.Close(); } catch { }
                client = null;
                Thread.Sleep(200);
            }
        }
    }

    static void OnData(byte[] data)
    {
        string[] split = Encoding.UTF8.GetString(data).Split(',');
        if (split.Length < 2) return;
        if (split[0] == "STATUS") { status = split[1].Trim(); return; }
        float x, y; int flaps;
        if (!float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return;
        if (!float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return;
        headX = x;
        headY = y;
        if (split.Length >= 3 && int.TryParse(split[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out flaps)) flapCount = flaps;
        faceVisible = split.Length < 4 || split[3].Trim() == "1";
        status = "";
    }
}

public class MotionController : MonoBehaviour
{
    [HideInInspector] public float headX = 0.5f;
    [HideInInspector] public float headY = 0.5f;
    private bool faceVisible = false;
    private int handledFlaps = -1;         // 누적 날갯짓 수 중 이미 점프로 처리한 수

    // 고개 좌우: 출발할 때 고개 위치가 가운데 칸. 칸을 벗어나 있는 동안은 계속 그쪽으로 감 (칸으로 돌아오면 멈춤)
    // (카메라 정가운데를 기준으로 하면, 카메라 한쪽에 치우쳐 앉았을 때 반대쪽으로는 거의 못 감)
    // 씬에 저장된 값에 덮이지 않도록 코드 상수로 둠 (감도 조절은 여기서)
    public const float DeadZone = 0.03f;   // 가운데 칸 반폭: 이 안에서는 안 움직임 (화면 너비 비율)
    const float FullTurn = 0.06f;          // 칸 가운데에서 이만큼 벗어나면 최대 속도

    private static float centerX = 0.5f;   // 가운데 칸 위치 (재시작/스테이지 이동해도 유지)
    private float smoothX = 0.5f;
    private bool hasSmooth = false;

    private BirdController birdController;

    // 왼쪽 아래 카메라 창
    private GameObject previewRoot;
    private RawImage previewImage;
    private Text previewStatus;
    private Texture2D previewTexture;
    private float lastFrameTime = -100f;
    private RectTransform bandRect;
    private Text arrowText;
    const float PreviewW = 256f, PreviewH = 192f;

    void Start()
    {
        birdController = GetComponent<BirdController>();
        MotionLink.Start();
        handledFlaps = MotionLink.flapCount; // 씬이 바뀌기 전에 한 날갯짓은 무시
        BuildPreview();
    }

    void Update()
    {
        // 신호가 2초 넘게 안 오면(트래커 꺼짐/멈춤) 예전 고개 위치로 계속 날아가지 않게 얼굴 없음으로 처리
        bool connected = MotionLink.SecondsSincePacket < 2f;
        headX = MotionLink.headX;
        headY = MotionLink.headY;
        faceVisible = connected && MotionLink.faceVisible;

        // 손 날갯짓: 처음 받은 값은 기준으로만 쓰고, 늘어난 만큼 점프
        int flaps = MotionLink.flapCount;
        if (flaps >= 0)
        {
            if (handledFlaps < 0 || flaps < handledFlaps) handledFlaps = flaps; // 처음이거나 파이썬을 다시 켬
            if (flaps > handledFlaps)
            {
                handledFlaps = flaps;
                if (birdController != null) birdController.TriggerJump();
            }
        }

        UpdatePreview();

        // 고개: 떨림만 살짝 거름 (얼굴을 놓치면 멈추고, 다시 찾으면 그 위치부터)
        if (!faceVisible) { hasSmooth = false; return; }
        if (!hasSmooth) { smoothX = headX; hasSmooth = true; }
        smoothX = Mathf.Lerp(smoothX, headX, 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
    }

    // 지금 고개 위치를 가운데 칸으로 (출발할 때 BirdController가 부름). 얼굴이 안 보이면 이전 칸 유지
    public void Recenter()
    {
        if (!faceVisible || !hasSmooth) return;
        centerX = Mathf.Clamp(smoothX, 0.2f, 0.8f);
    }

    public float GetMotionMoveX()
    {
        if (!faceVisible || !hasSmooth) return 0f;
        // 화면이 거울처럼 뒤집혀 있어서 내가 왼쪽으로 가면 x가 작아짐 → 왼쪽(-)으로
        float offset = smoothX - centerX;
        float amount = Mathf.Clamp01((Mathf.Abs(offset) - DeadZone) / (FullTurn - DeadZone));
        return Mathf.Sign(offset) * amount;
    }

    // ───────── 왼쪽 아래 카메라 창 ─────────

    void UpdatePreview()
    {
        bool show = birdController != null && birdController.UsesMotionControl;
        if (previewRoot.activeSelf != show) previewRoot.SetActive(show);

        byte[] frame = MotionLink.TakeFrame();
        if (frame != null && previewTexture.LoadImage(frame)) lastFrameTime = Time.unscaledTime;
        if (!show) return;

        bool live = Time.unscaledTime - lastFrameTime < 1.5f;
        previewImage.color = live ? Color.white : new Color(1f, 1f, 1f, 0.25f);

        // 가운데 칸 표시 + 지금 가는 방향 화살표
        bandRect.gameObject.SetActive(live);
        bandRect.anchoredPosition = new Vector2((centerX - 0.5f) * PreviewW, 0f);
        float move = GetMotionMoveX();
        arrowText.text = !live ? "" : move > 0.01f ? "▶" : move < -0.01f ? "◀" : "";
        arrowText.rectTransform.anchoredPosition = new Vector2(Mathf.Sign(move) * PreviewW * 0.32f, 18f);
        string status = MotionLink.status;
        if (live) previewStatus.text = "";
        else if (MotionLink.SecondsSincePacket > 3f) previewStatus.text = lastFrameTime > 0f ? "연결이 끊겼어요\n다시 연결하는 중..." : "모션 트래커 켜는 중...";
        else if (status == "busy") previewStatus.text = "다른 프로그램이 카메라를 쓰고 있어요\n(디스코드·줌 등의 카메라를 꺼 주세요)";
        else if (status == "nocam") previewStatus.text = "웹캠을 찾을 수 없어요";
        else previewStatus.text = "카메라 연결 중...";
    }

    void BuildPreview()
    {
        previewRoot = new GameObject("MotionCameraPreview");
        Canvas canvas = previewRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // 게임 UI 위, 스테이지 전환 화면(1000) 아래
        CanvasScaler scaler = previewRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 16);

        // 남색 판 + 크림색 테두리 (게임 UI 팔레트)
        Image frame = NewUI<Image>("Frame", previewRoot.transform);
        frame.color = new Color(0.11f, 0.13f, 0.25f, 0.85f);
        frame.raycastTarget = false;
        Outline outline = frame.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.965f, 0.906f, 0.757f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        RectTransform frt = frame.rectTransform;
        frt.anchorMin = frt.anchorMax = frt.pivot = Vector2.zero;
        frt.anchoredPosition = new Vector2(16f, 16f);
        frt.sizeDelta = new Vector2(256f + 12f, 192f + 40f);

        previewTexture = new Texture2D(320, 240, TextureFormat.RGB24, false);
        previewImage = NewUI<RawImage>("Camera", frt);
        previewImage.texture = previewTexture;
        previewImage.color = new Color(1f, 1f, 1f, 0.25f);
        previewImage.raycastTarget = false;
        RectTransform irt = previewImage.rectTransform;
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f);
        irt.anchoredPosition = new Vector2(0f, -6f);
        irt.sizeDelta = new Vector2(PreviewW, PreviewH);

        // 가운데 칸: 반투명 띠 + 양쪽 흰 선
        Image band = NewUI<Image>("CenterBand", irt);
        band.color = new Color(1f, 1f, 1f, 0.12f);
        band.raycastTarget = false;
        bandRect = band.rectTransform;
        bandRect.anchorMin = new Vector2(0.5f, 0f);
        bandRect.anchorMax = new Vector2(0.5f, 1f);
        bandRect.sizeDelta = new Vector2(DeadZone * 2f * PreviewW, 0f);
        for (int side = 0; side < 2; side++)
        {
            Image line = NewUI<Image>("Edge", bandRect);
            line.color = new Color(1f, 1f, 1f, 0.85f);
            line.raycastTarget = false;
            RectTransform lrt = line.rectTransform;
            lrt.anchorMin = new Vector2(side, 0f);
            lrt.anchorMax = new Vector2(side, 1f);
            lrt.sizeDelta = new Vector2(2f, 0f);
        }

        arrowText = NewUI<Text>("Arrow", irt);
        arrowText.font = font;
        arrowText.fontSize = 34;
        arrowText.alignment = TextAnchor.MiddleCenter;
        arrowText.color = new Color(0.31f, 0.86f, 0.31f);
        arrowText.raycastTarget = false;
        arrowText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        RectTransform art = arrowText.rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(0.5f, 0f);
        art.sizeDelta = new Vector2(60f, 44f);

        previewStatus = NewUI<Text>("Status", irt);
        previewStatus.font = font;
        previewStatus.fontSize = 15;
        previewStatus.alignment = TextAnchor.MiddleCenter;
        previewStatus.color = new Color(0.965f, 0.906f, 0.757f);
        previewStatus.raycastTarget = false;
        previewStatus.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        Stretch(previewStatus.rectTransform, 8f);

        Text hint = NewUI<Text>("Hint", frt);
        hint.font = font;
        hint.fontSize = 14;
        hint.alignment = TextAnchor.MiddleCenter;
        hint.color = new Color(0.98f, 0.72f, 0.30f);
        hint.raycastTarget = false;
        hint.text = "고개 ← → 이동   ·   손 ↓ 내리치기 날갯짓";
        RectTransform hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0f, 0f);
        hrt.anchorMax = new Vector2(1f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 4f);
        hrt.sizeDelta = new Vector2(0f, 28f);

        previewRoot.SetActive(false);
    }

    static T NewUI<T>(string name, Transform parent) where T : Graphic
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return g.AddComponent<T>();
    }

    static void Stretch(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    // 받기(MotionLink)는 씬이 바뀌어도 계속 유지하고, 이 씬의 카메라 창만 정리
    void OnDestroy()
    {
        if (previewRoot != null) Destroy(previewRoot);
        if (previewTexture != null) Destroy(previewTexture);
    }
}
