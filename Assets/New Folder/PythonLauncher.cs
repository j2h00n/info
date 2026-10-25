using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;
using System.IO;
using TMPro;
using System.Collections.Generic;

public class PythonLauncher : MonoBehaviour
{
    private static Process pythonProcess; // static으로 유지하여 중복 실행 방지
    public Toggle motionToggle; 
    private bool lastToggleState = false;

    // 카메라 선택: "" = 자동(0~2번 중 화면이 들어오는 것), 그 밖엔 고른 카메라 이름 (트래커가 이름으로 찾음)
    // Motion Control 옆 펼치기 버튼 → 컴퓨터에 연결된 카메라 이름 목록 (많으면 스크롤)
    private static string CameraName
    {
        get { return PlayerPrefs.GetString("CameraName", ""); }
        set { PlayerPrefs.SetString("CameraName", value); PlayerPrefs.Save(); }
    }
    const string AutoLabel = "자동 선택";
    const int CameraMaxRows = 4;
    static readonly Color OptionColor = new Color(0.20f, 0.24f, 0.42f);
    static readonly Color SelectedColor = new Color(0.20f, 0.58f, 0.55f);
    private Text cameraLabel;
    private RectTransform cameraArrow, cameraList, cameraContent;
    private Font koreanFont;
    private Image rowSprite;   // 목록 줄 모양 (소리 설정 창의 BGM 목록 줄을 따라 함)
    private readonly List<GameObject> cameraRows = new List<GameObject>();

    void Start()
    {
        int savedMode = PlayerPrefs.GetInt("MotionMode", 0);
        bool initialOn = (savedMode == 1);

        if (motionToggle != null)
        {
            motionToggle.SetIsOnWithoutNotify(initialOn);
        }

        lastToggleState = initialOn;
        BuildCameraButton();
        
        if (initialOn)
        {
            StartPython();
        }
    }

    void Update()
    {
        if (motionToggle != null)
        {
            if (motionToggle.isOn != lastToggleState)
            {
                lastToggleState = motionToggle.isOn;

                PlayerPrefs.SetInt("MotionMode", lastToggleState ? 1 : 0);
                PlayerPrefs.Save();

                if (lastToggleState)
                {
                    StartPython();
                }
                else
                {
                    KillPython();
                }
            }
        }

        // 모션 조작이 켜져 있는데 트래커가 어떤 이유로든 꺼져 있으면 3초마다 다시 켬 (게임 도중 끊겨도 저절로 복구)
        if (lastToggleState && Time.unscaledTime - lastWatchdog > 3f)
        {
            lastWatchdog = Time.unscaledTime;
            if (pythonProcess == null || pythonProcess.HasExited)
            {
                UnityEngine.Debug.Log("모션 트래커가 꺼져 있어서 다시 켭니다");
                pythonProcess = null;
                StartPython();
            }
        }
    }

    private float lastWatchdog = 0f;

    void StartPython()
    {
        if (pythonProcess != null && !pythonProcess.HasExited) return;

        try
        {
            // 에디터: 프로젝트 폴더 / 빌드한 게임: exe가 있는 폴더 (둘 다 _Data 폴더의 한 칸 위)
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string path = Path.Combine(folder, "motion_tracker.py");
            // 같이 들어 있는 파이썬(OpenCV + MediaPipe 설치됨)이 있으면 그걸 쓰고, 없으면 컴퓨터에 깔린 파이썬
            string bundled = Path.Combine(folder, "python_runtime", "python.exe");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = File.Exists(bundled) ? bundled : "python",
                Arguments = $"\"{path}\" {Process.GetCurrentProcess().Id} \"{CameraArg()}\"", // 게임이 꺼지면 트래커도 스스로 꺼지도록 게임 번호 전달 + 고른 카메라 이름
                WorkingDirectory = folder,    // 얼굴 인식 모델(xml)을 같은 폴더에서 찾도록 (바로가기로 실행해도 안전)
                UseShellExecute = false,      // 🔥 까만 창이 뜨지 않도록 해제
                CreateNoWindow = true         // 🔥 창 숨김 처리 (백그라운드 실행)
            };
            pythonProcess = Process.Start(startInfo);
            UnityEngine.Debug.Log("파이썬 모션 트래커 백그라운드 켜짐!");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("파이썬 실행 실패: " + e.Message);
        }
    }

    void KillPython()
    {
        try
        {
            if (pythonProcess != null && !pythonProcess.HasExited)
            {
                pythonProcess.Kill();
                pythonProcess.WaitForExit(1000); // 카메라를 놓아준 뒤에 새 트래커가 열 수 있게
                pythonProcess = null;
                UnityEngine.Debug.Log("파이썬 모션 트래커 꺼짐!");
            }
        }
        catch { }
    }

    // ───────── 카메라 선택 (펼치는 목록) ─────────

    // 고른 카메라가 지금 연결돼 있으면 그 이름, 아니면 AUTO
    static string CameraArg()
    {
        string name = CameraName;
        if (name == "" || !CameraConnected(name)) return "AUTO";
        return name.Replace("\"", "");
    }

    static bool CameraConnected(string name)
    {
        foreach (WebCamDevice d in WebCamTexture.devices) if (d.name == name) return true;
        return false;
    }

    // 시작 메뉴의 Motion Control 오른쪽에 펼치기 버튼 (모드 버튼을 복제해서 같은 모양으로) + 아래로 펼쳐지는 목록
    void BuildCameraButton()
    {
        if (motionToggle == null) return;
        RectTransform toggle = (RectTransform)motionToggle.transform;
        Transform menu = toggle.parent;
        Transform source = menu.Find("Story Button");
        if (source == null) source = menu.Find("Start Button");
        if (source == null) return;
        // 기기 이름이 한글일 수도 있어서 윈도우 한글 글꼴 사용 (TMP 글꼴엔 한글이 없음)
        koreanFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 16);
        SoundMenu sound = FindAnyObjectByType<SoundMenu>(FindObjectsInactive.Include);

        toggle.anchoredPosition = new Vector2(-72f, toggle.anchoredPosition.y); // 오른쪽에 버튼 자리 비움

        // 펼치기 버튼: 고른 카메라 이름 + 오른쪽 화살표
        Vector2 size = new Vector2(172f, 36f);
        float y = toggle.anchoredPosition.y;
        GameObject go = Instantiate(source.gameObject, menu);
        go.name = "Camera Dropdown";
        RectTransform rt = (RectTransform)go.transform;
        rt.anchoredPosition = new Vector2(108f, y);
        rt.sizeDelta = size;
        Image bg = go.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = SelectedColor; // 엔드리스 버튼과 같은 청록
            bg.pixelsPerUnitMultiplier = 96f / size.y * 0.9f;
        }
        Button b = go.GetComponent<Button>();
        b.onClick = new Button.ButtonClickedEvent(); // 복제한 버튼의 원래 동작(스토리 시작) 지움
        b.onClick.AddListener(ToggleCameraList);
        foreach (TextMeshProUGUI t in go.GetComponentsInChildren<TextMeshProUGUI>(true)) t.gameObject.SetActive(false);

        // 아이콘 자리를 화살표로 (오른쪽 끝)
        Transform icon = go.transform.Find("Icon");
        if (icon != null)
        {
            cameraArrow = (RectTransform)icon;
            cameraArrow.anchorMin = cameraArrow.anchorMax = new Vector2(1f, 0.5f);
            cameraArrow.pivot = new Vector2(0.5f, 0.5f);
            cameraArrow.anchoredPosition = new Vector2(-18f, 0f);
            cameraArrow.sizeDelta = new Vector2(20f, 20f);
            Image ai = icon.GetComponent<Image>();
            if (ai != null && sound != null && sound.arrow != null) ai.sprite = sound.arrow.GetComponent<Image>().sprite;
        }

        cameraLabel = NewText("CameraName", go.transform, 15);
        RectTransform lrt = cameraLabel.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(12f, 0f); lrt.offsetMax = new Vector2(-34f, 0f);

        // 펼쳐지는 목록: 버튼 바로 아래, 다른 메뉴 버튼들 위에 그려짐
        cameraList = new GameObject("Camera List", typeof(RectTransform)).GetComponent<RectTransform>();
        cameraList.gameObject.layer = 5;
        cameraList.SetParent(menu, false);
        cameraList.SetAsLastSibling();
        cameraList.anchorMin = cameraList.anchorMax = new Vector2(0.5f, 0.5f);
        cameraList.pivot = new Vector2(0.5f, 1f);
        cameraList.anchoredPosition = new Vector2(rt.anchoredPosition.x - 20f, y - size.y * 0.5f - 4f);
        cameraList.sizeDelta = new Vector2(size.x + 40f, 100f);
        Image li = cameraList.gameObject.AddComponent<Image>();
        Image soundList = sound != null && sound.list != null ? sound.list.GetComponent<Image>() : null;
        if (soundList != null)
        {
            li.sprite = soundList.sprite; li.type = soundList.type;
            li.pixelsPerUnitMultiplier = soundList.pixelsPerUnitMultiplier; li.color = soundList.color;
        }
        else li.color = new Color(0.11f, 0.13f, 0.25f, 0.95f);
        if (sound != null && sound.optionTemplate != null) rowSprite = sound.optionTemplate.GetComponent<Image>();
        cameraContent = DropdownList.MakeScrollable(cameraList);
        cameraList.gameObject.SetActive(false);

        RefreshCameraLabel();
    }

    void ToggleCameraList()
    {
        bool open = !cameraList.gameObject.activeSelf;
        if (open) FillCameraList(); // 펼칠 때마다 다시 읽음 (게임 도중 카메라를 꽂아도 보이게)
        cameraList.gameObject.SetActive(open);
        if (cameraArrow != null) cameraArrow.localScale = new Vector3(1f, open ? -1f : 1f, 1f);
        if (open)
        {
            string current = CameraArg() == "AUTO" ? "" : CameraName;
            int sel = 0;
            for (int i = 0; i < cameraRows.Count; i++) if (cameraRows[i].name == "Option " + current) sel = i;
            DropdownList.ScrollTo(cameraList, sel, cameraRows.Count, CameraMaxRows);
        }
    }

    // 목록: 맨 위 "자동 선택" + 컴퓨터에 연결된 카메라 이름들
    void FillCameraList()
    {
        foreach (GameObject r in cameraRows) Destroy(r);
        cameraRows.Clear();
        var names = new List<string> { "" };
        foreach (WebCamDevice d in WebCamTexture.devices) if (!names.Contains(d.name)) names.Add(d.name);

        string current = CameraArg() == "AUTO" ? "" : CameraName;
        const float rowH = 40f;
        var rects = new RectTransform[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            GameObject row = new GameObject("Option " + name, typeof(RectTransform));
            row.layer = 5;
            RectTransform r = (RectTransform)row.transform;
            r.sizeDelta = new Vector2(0f, rowH - 4f);
            Image img = row.AddComponent<Image>();
            if (rowSprite != null)
            {
                img.sprite = rowSprite.sprite; img.type = rowSprite.type;
                img.pixelsPerUnitMultiplier = rowSprite.pixelsPerUnitMultiplier;
            }
            img.color = name == current ? SelectedColor : OptionColor;
            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => ChooseCamera(name));
            Text t = NewText("Text", row.transform, 15);
            t.text = name == "" ? AutoLabel : name;
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(14f, 0f); t.rectTransform.offsetMax = new Vector2(-8f, 0f);
            rects[i] = r;
            cameraRows.Add(row);
        }
        DropdownList.Layout(cameraList, cameraContent, rects, rowH, CameraMaxRows);
    }

    void ChooseCamera(string name)
    {
        bool changed = name != (CameraArg() == "AUTO" ? "" : CameraName);
        CameraName = name;
        cameraList.gameObject.SetActive(false);
        if (cameraArrow != null) cameraArrow.localScale = Vector3.one;
        RefreshCameraLabel();

        // 모션 조작이 켜져 있으면 고른 카메라로 트래커를 다시 켬
        if (changed && lastToggleState)
        {
            KillPython();
            StartPython();
        }
    }

    void RefreshCameraLabel()
    {
        if (cameraLabel == null) return;
        string arg = CameraArg();
        cameraLabel.text = arg == "AUTO" ? AutoLabel : arg;
    }

    // 크림색 굵은 글자 + 그림자 (긴 기기 이름은 칸에 맞게 작아짐)
    Text NewText(string name, Transform parent, int size)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.layer = 5;
        g.transform.SetParent(parent, false);
        Text t = g.AddComponent<Text>();
        t.font = koreanFont;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.color = new Color(0.965f, 0.906f, 0.757f);
        t.alignment = TextAnchor.MiddleLeft;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 10;
        t.resizeTextMaxSize = size;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        Shadow sh = g.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.5f);
        sh.effectDistance = new Vector2(1f, -1f);
        return t;
    }

    // 재시작/스테이지 이동으로 씬이 바뀔 땐 트래커를 계속 켜 둠 (카메라를 다시 여는 데 몇 초 걸려서)
    // → 게임을 끌 때와 모션 조작을 끌 때만 종료
    void OnApplicationQuit()
    {
        KillPython();
    }
}