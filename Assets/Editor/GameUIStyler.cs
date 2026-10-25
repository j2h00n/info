using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

// 씬의 UI(점수판, 시작 메뉴, 재시작 버튼)를 게임 분위기에 맞게 꾸밈
// 먼저 Tools > Generate Game UI Icons 로 아이콘을 만든 뒤 실행. 여러 번 실행해도 중복 생성되지 않음
// 메뉴: Tools > Apply Game UI Style
public static class GameUIStyler
{
    const string IconDir = "Assets/UI/Icons/";
    const string TextMatPath = "Assets/UI/GameText.mat";
    const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    // 노을 지는 숲 팔레트
    static readonly Color Cream = new Color(0.965f, 0.906f, 0.757f);  // 별빛
    static readonly Color Navy = new Color(0.11f, 0.13f, 0.25f);      // 밤하늘
    static readonly Color Orange = new Color(0.93f, 0.54f, 0.24f);    // 나무 울타리
    static readonly Color Gold = new Color(0.98f, 0.72f, 0.30f);      // 노을
    static readonly Color Green = new Color(0.30f, 0.68f, 0.33f);     // 풀
    static readonly Color Teal = new Color(0.20f, 0.58f, 0.55f);      // 엔드리스 버튼 (스토리 주황과 구분)
    static readonly Color BossPurple = new Color(0.52f, 0.30f, 0.72f); // 보스 바로 하기 버튼 (뱀 색)
    static readonly Color EagleBrown = new Color(0.62f, 0.38f, 0.20f);  // 사막 보스 바로 하기 버튼 (독수리 색)
    static readonly Color IceBlue = new Color(0.30f, 0.62f, 0.86f);     // 얼음 동굴 탈출 바로 하기 버튼
    static readonly Color SlingOrange = new Color(0.90f, 0.42f, 0.18f); // 도시 새총 아이 보스 바로 하기 버튼
    static readonly Color Track = new Color(0.05f, 0.07f, 0.16f, 0.9f);

    static TMP_FontAsset font;
    static Material textMat;

    [MenuItem("Tools/Apply Game UI Style")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }

        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        textMat = MakeTextMaterial();

        Transform canvas = GameObject.Find("Canvas").transform;

        // ── 게임 중 점수판: 갈매기(거리) / 바위벽(통과한 벽) ──
        Badge(canvas, "ScoreBadge", "ScoreText", "Icon_Seagull", false);
        Badge(canvas, "WallBadge", "WallText", "Icon_Wall", true);

        // ── 재시작 / 메뉴 버튼 ──
        EndButtons(canvas, GameObject.Find("Cube").GetComponent<BirdController>());

        // ── 시작 메뉴 (볼륨 슬라이더는 오른쪽 위 소리 설정 창으로 옮김) ──
        RectTransform menu = (RectTransform)canvas.Find("StartMenu");
        Place(menu, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 330));
        Sliced(Img(menu), Sprite("UI_Panel"), Color.white, 1f);

        // 최고 기록: 트로피 / 바위벽
        Icon(menu, "BestScoreIcon", "Icon_Trophy", Gold, new Vector2(-90, 72), 56);
        Icon(menu, "BestWallIcon", "Icon_Wall", Cream, new Vector2(90, 72), 56);
        StatText(menu, "Text1", -90);
        StatText(menu, "Text2", 90);

        MotionToggle(menu);

        // ── 모드 선택: 스토리 / 엔드리스 ──
        BirdController bird = GameObject.Find("Cube").GetComponent<BirdController>();
        AssignMusic(bird);
        Transform endless = menu.Find("Start Button"); // 기존 시작 버튼을 엔드리스 버튼으로 사용
        Transform story = menu.Find("Story Button");
        if (story == null)
        {
            GameObject copy = Object.Instantiate(endless.gameObject, menu);
            copy.name = "Story Button";
            Undo.RegisterCreatedObjectUndo(copy, "Game UI");
            story = copy.transform;
        }
        Vector2 modeSize = new Vector2(172, 62);
        Place((RectTransform)story, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-92, -108), modeSize);
        Place((RectTransform)endless, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(92, -108), modeSize);
        // 두 버튼 글자 크기를 똑같이 맞추기 위해 긴 쪽(ENDLESS)에 맞춘 크기 사용
        StyleButton(story, "STORY", "Icon_Flag", modeSize, Orange, 0.34f);
        StyleButton(endless, "ENDLESS", "Icon_Infinity", modeSize, Teal, 0.34f);
        SetClick(story.GetComponent<Button>(), bird.StartStory);
        SetClick(endless.GetComponent<Button>(), bird.StartEndless);

        // ── 오른쪽 위 ♪ 버튼 → 소리 설정 창 (볼륨 + 엔드리스 BGM 선택) ──
        BuildSoundMenu(canvas, menu, story, bird);

        // ── 메뉴 패널 바깥 왼쪽 위: 보스 바로 하기 / 다른 스테이지로 ──
        Vector2 sideSize = new Vector2(200, 64);
        float panelHalfW = menu.sizeDelta.x * 0.5f, panelHalfH = menu.sizeDelta.y * 0.5f;
        float sideX = -panelHalfW - 16 - sideSize.x * 0.5f;
        float sideY = panelHalfH - sideSize.y * 0.5f;

        // 보스 바로 하기 (보스가 있는 스테이지만: 1 = 뱀, 2 = 독수리, 3 = 동굴 붕괴 탈출, 4 = 새총 아이)
        if (bird.bossFight != null || bird.eagleChase != null || bird.iceEscape != null || bird.kidBoss != null)
        {
            Transform bossBtn = CopyButton(menu, story, "Boss Button");
            Place((RectTransform)bossBtn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(sideX, sideY), sideSize);
            if (bird.bossFight != null) StyleButton(bossBtn, "STAGE " + bird.stageNumber + "\nBOSS", "Icon_Snake", sideSize, BossPurple, 0.3f);
            else if (bird.eagleChase != null) StyleButton(bossBtn, "STAGE " + bird.stageNumber + "\nBOSS", "Icon_Eagle", sideSize, EagleBrown, 0.3f);
            else if (bird.kidBoss != null) StyleButton(bossBtn, "STAGE " + bird.stageNumber + "\nBOSS", "Icon_Slingshot", sideSize, SlingOrange, 0.3f);
            else StyleButton(bossBtn, "STAGE " + bird.stageNumber + "\nESCAPE", "Icon_Snowflake", sideSize, IceBlue, 0.3f);
            SetClick(bossBtn.GetComponent<Button>(), bird.StartBossDirect);
            sideY -= sideSize.y + 12;
        }
        else RemoveChild(menu, "Boss Button");

        // 다음 / 이전 스테이지로 이동 (둘 다 있으면 다음 버튼 아래에 이전 버튼)
        bool hasNext = !string.IsNullOrEmpty(bird.nextStageScene);
        bool hasPrev = !string.IsNullOrEmpty(bird.prevStageScene);
        if (hasNext) { StageNavButton(menu, story, "Stage Button", bird, true, new Vector2(sideX, sideY), sideSize); sideY -= sideSize.y + 12; }
        if (hasPrev) StageNavButton(menu, story, hasNext ? "Prev Stage Button" : "Stage Button", bird, false, new Vector2(sideX, sideY), sideSize);
        if (!hasNext && !hasPrev) RemoveChild(menu, "Stage Button");
        if (!(hasNext && hasPrev)) RemoveChild(menu, "Prev Stage Button");

        // 메뉴 패널 맨 위 스테이지 제목 (패널 밖에 두면 작은 화면에서 위가 잘림)
        TextMeshProUGUI title = Label(menu, "StageTitle", new Vector2(0, panelHalfH - 26), new Vector2(380, 36));
        Style(title, 24, TextAlignmentOptions.Center);
        title.color = Gold;
        title.text = "STAGE " + bird.stageNumber + " - " + bird.stageName;

        // 스토리 클리어 후 다음 스테이지 버튼 (재시작 버튼 아래)
        if (hasNext)
        {
            Transform next = canvas.Find("NextStage Button");
            if (next == null)
            {
                GameObject copy = Object.Instantiate(canvas.Find("Restart").gameObject, canvas);
                copy.name = "NextStage Button";
                Undo.RegisterCreatedObjectUndo(copy, "Game UI");
                next = copy.transform;
            }
            Place((RectTransform)next, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(260, 64));
            StyleButton(next, "NEXT STAGE", "Icon_Play", new Vector2(260, 64), Teal);
            SetClick(next.GetComponent<Button>(), bird.LoadNextStage);
            Undo.RecordObject(next.gameObject, "Game UI");
            next.gameObject.SetActive(false);
            Undo.RecordObject(bird, "Game UI");
            bird.nextStageButton = next.gameObject;
        }
        else
        {
            RemoveChild(canvas, "NextStage Button");
            Undo.RecordObject(bird, "Game UI");
            bird.nextStageButton = null;
        }

        // ── 스토리 클리어 화면 ──
        string clearMessage = bird.bossFight != null ? "You beat the Snake Boss!"
                            : bird.eagleChase != null ? "You escaped the Sand Eagle!"
                            : bird.iceEscape != null ? "You escaped the Ice Cave!"
                            : "You crossed the " + bird.stageName.ToLower() + "!";
        RectTransform clear = ClearPanel(canvas, clearMessage);

        Undo.RecordObject(bird, "Game UI");
        bird.wallBadge = canvas.Find("WallBadge").gameObject;
        bird.scoreBadge = canvas.Find("ScoreBadge").gameObject;
        bird.clearPanel = clear.gameObject;
        EditorUtility.SetDirty(bird);

        // ── 갈매기 날갯짓 ──
        GameObject gull = GameObject.Find("Cube/Seagull");
        if (gull.GetComponent<SeagullFlap>() == null) Undo.AddComponent<SeagullFlap>(gull);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("게임 UI 스타일 적용 완료");
    }

    // 글자에 남색 외곽선 + 그림자 → 바위나 하늘 위에서도 잘 보임
    static Material MakeTextMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(TextMatPath);
        if (mat == null)
        {
            mat = new Material(font.material);
            AssetDatabase.CreateAsset(mat, TextMatPath);
        }
        mat.EnableKeyword("OUTLINE_ON");
        mat.SetFloat("_OutlineWidth", 0.18f);
        mat.SetColor("_OutlineColor", Navy);
        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor("_UnderlayColor", new Color(0, 0, 0, 0.5f));
        mat.SetFloat("_UnderlayOffsetY", -0.8f);
        mat.SetFloat("_UnderlaySoftness", 0.3f);
        mat.SetFloat("_FaceDilate", 0.1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void Badge(Transform canvas, string badgeName, string textName, string icon, bool right)
    {
        RectTransform badge = Child(canvas, badgeName);
        Vector2 corner = right ? new Vector2(1, 1) : new Vector2(0, 1);
        Place(badge, corner, corner, new Vector2(right ? -16 : 16, -16), new Vector2(190, 56));
        Image bg = Img(badge);
        Sliced(bg, Sprite("UI_Panel"), Color.white, 1.6f);
        bg.raycastTarget = false;

        float iconSize = icon == "Icon_Seagull" ? 52 : 40; // 갈매기는 선이 가늘어서 크게 해야 바위 아이콘과 무게감이 비슷
        Image ic = Icon(badge, badgeName + "Icon", icon, Cream, Vector2.zero, iconSize);
        Place((RectTransform)ic.transform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(34, 0), new Vector2(iconSize, iconSize));

        Transform txt = canvas.Find(textName);
        if (txt == null) txt = badge.Find(textName);
        if (txt.parent != badge) Undo.SetTransformParent(txt, badge, "Game UI");
        RectTransform trt = (RectTransform)txt;
        Undo.RecordObject(trt, "Game UI");
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.pivot = new Vector2(0.5f, 0.5f);
        trt.offsetMin = new Vector2(62, 0); trt.offsetMax = new Vector2(-14, 0);

        TextMeshProUGUI t = txt.GetComponent<TextMeshProUGUI>();
        Style(t, 30, TextAlignmentOptions.MidlineLeft);
        t.text = "0";
    }

    static void StyleButton(Transform btn, string label, string icon, Vector2 size, Color color, float fontScale = 0.45f)
    {
        RectTransform rt = (RectTransform)btn;
        Undo.RecordObject(rt, "Game UI");
        rt.sizeDelta = size;
        Sliced(Img(rt), Sprite("UI_Button"), color, 96f / size.y * 0.9f);

        Button b = btn.GetComponent<Button>();
        Undo.RecordObject(b, "Game UI");
        ColorBlock cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 0.93f, 0.85f);
        cb.pressedColor = new Color(0.8f, 0.72f, 0.65f);
        cb.selectedColor = Color.white;
        b.colors = cb;

        TextMeshProUGUI t = btn.GetComponentInChildren<TextMeshProUGUI>(true);
        Style(t, size.y * fontScale, TextAlignmentOptions.Center);
        t.text = label;
        // 좁은 버튼에서도 글자가 넘치지 않게 자동 축소
        t.enableAutoSizing = true; t.fontSizeMin = 14; t.fontSizeMax = size.y * fontScale;
        RectTransform trt = (RectTransform)t.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        float iconSize = size.y * 0.5f;
        trt.offsetMin = new Vector2(size.y * 0.62f + iconSize * 0.5f + 6f, 4); trt.offsetMax = new Vector2(-size.y * 0.25f, 0); // 아이콘 오른쪽부터 글자
        Image ic = Icon(btn, "Icon", icon, Cream, Vector2.zero, iconSize);
        Place((RectTransform)ic.transform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(size.y * 0.62f, 2), new Vector2(iconSize, iconSize));
    }

    // 게임오버 화면: [RESTART] [MENU] 나란히. 지금 열린 씬에만 이 버튼들을 추가하는 메뉴 (전체 스타일 다시 적용 없이)
    [MenuItem("Tools/Add Menu Button")]
    public static void AddMenuButton()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("플레이 모드를 끄고 실행하세요."); return; }
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        textMat = MakeTextMaterial();
        Transform canvas = GameObject.Find("Canvas").transform;
        EndButtons(canvas, GameObject.Find("Cube").GetComponent<BirdController>());
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
    }

    static void EndButtons(Transform canvas, BirdController bird)
    {
        Vector2 size = new Vector2(240, 64);
        Transform restart = canvas.Find("Restart");
        Place((RectTransform)restart, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-130, 0), size);
        StyleButton(restart, "RESTART", "Icon_Restart", size, Orange);

        Transform menu = CopyButton(canvas, restart, "Menu Button");
        menu.SetSiblingIndex(restart.GetSiblingIndex() + 1);
        Place((RectTransform)menu, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(130, 0), size);
        StyleButton(menu, "MENU", "Icon_Home", size, Teal);
        SetClick(menu.GetComponent<Button>(), bird.BackToMenu);
        Undo.RecordObject(menu.gameObject, "Game UI");
        menu.gameObject.SetActive(false);

        Undo.RecordObject(bird, "Game UI");
        bird.menuButton = menu.gameObject;
        EditorUtility.SetDirty(bird);
    }

    static void StageNavButton(Transform menu, Transform source, string name, BirdController bird, bool next, Vector2 pos, Vector2 size)
    {
        Transform nav = CopyButton(menu, source, name);
        Place((RectTransform)nav, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        int other = next ? bird.stageNumber + 1 : bird.stageNumber - 1;
        StyleButton(nav, "STAGE " + other + "\n" + StageName(other), "Icon_Play", size, next ? Teal : Orange, 0.3f);
        SetClick(nav.GetComponent<Button>(), next ? (UnityEngine.Events.UnityAction)bird.LoadNextStage : bird.LoadPrevStage);
        // 이전 스테이지로 갈 땐 화살표를 왼쪽으로
        Transform navIcon = nav.Find("Icon");
        Undo.RecordObject(navIcon, "Game UI");
        navIcon.localScale = new Vector3(next ? 1 : -1, 1, 1);
    }

    static string StageName(int stage)
    {
        switch (stage)
        {
            case 1: return "FOREST";
            case 2: return "DESERT";
            case 3: return "ICE CAVE";
            case 4: return "CITY";
            default: return "";
        }
    }

    static Transform CopyButton(Transform parent, Transform source, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return t;
        GameObject copy = Object.Instantiate(source.gameObject, parent);
        copy.name = name;
        Undo.RegisterCreatedObjectUndo(copy, "Game UI");
        return copy.transform;
    }

    static void RemoveChild(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
    }

    static void SetClick(Button b, UnityEngine.Events.UnityAction action)
    {
        Undo.RecordObject(b, "Game UI");
        while (b.onClick.GetPersistentEventCount() > 0)
            UnityEditor.Events.UnityEventTools.RemovePersistentListener(b.onClick, 0);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(b.onClick, action);
    }

    // "STAGE CLEAR!" 배너 (트로피 + 제목 + 거리). 처음엔 꺼져 있다가 클리어하면 켜짐
    static RectTransform ClearPanel(Transform canvas, string message)
    {
        RectTransform panel = Child(canvas, "ClearPanel");
        Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 112), new Vector2(460, 130));
        Image bg = Img(panel);
        Sliced(bg, Sprite("UI_Panel"), Color.white, 1f);
        bg.raycastTarget = false;

        Icon(panel, "TrophyLeft", "Icon_Trophy", Gold, new Vector2(-190, 6), 58);
        Icon(panel, "TrophyRight", "Icon_Trophy", Gold, new Vector2(190, 6), 58);

        TextMeshProUGUI title = Label(panel, "ClearTitle", new Vector2(0, 18), new Vector2(310, 50));
        Style(title, 36, TextAlignmentOptions.Center);
        title.color = Gold;
        title.text = "STAGE CLEAR!";

        TextMeshProUGUI sub = Label(panel, "ClearDistance", new Vector2(0, -28), new Vector2(300, 34));
        Style(sub, 22, TextAlignmentOptions.Center);
        sub.text = message;

        Undo.RecordObject(panel.gameObject, "Game UI");
        panel.gameObject.SetActive(false);
        return panel;
    }

    // 뱀 보스 체력바(화면 위 가운데) + 카운트다운 숫자. BossSetup에서 호출
    public static void BuildBossHud(Transform canvas, out GameObject hud, out RectTransform hpFill, out TextMeshProUGUI countdown)
    {
        TextMeshProUGUI nameText;
        BuildBossHud(canvas, "Icon_Snake", new Color(0.72f, 0.92f, 0.35f), "SNAKE BOSS", new Color(0.86f, 0.22f, 0.32f), true,
                     out hud, out hpFill, out countdown, out nameText);
    }

    // 보스 막대 UI. halfNotch: 가운데(50%) 눈금 표시 (뱀 풍선 2개). 독수리는 남은 시간 막대로 씀
    public static void BuildBossHud(Transform canvas, string iconName, Color iconColor, string bossName, Color fillColor, bool halfNotch,
                                    out GameObject hud, out RectTransform hpFill, out TextMeshProUGUI countdown, out TextMeshProUGUI nameText)
    {
        if (font == null) font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (textMat == null) textMat = MakeTextMaterial();

        RectTransform panel = Child(canvas, "BossHud");
        Place(panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -14), new Vector2(460, 76));
        Image bg = Img(panel);
        Sliced(bg, Sprite("UI_Panel"), Color.white, 1.4f);
        bg.raycastTarget = false;

        Image icon = Icon(panel, "BossIcon", iconName, iconColor, Vector2.zero, 56);
        Place((RectTransform)icon.transform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(42, 0), new Vector2(56, 56));

        TextMeshProUGUI name = Label(panel, "BossName", new Vector2(30, 15), new Vector2(350, 26));
        Style(name, 20, TextAlignmentOptions.MidlineLeft);
        name.text = bossName;
        nameText = name;

        RectTransform track = Child(panel, "HpTrack");
        Place(track, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, -13), new Vector2(350, 20));
        Image trackImg = Img(track);
        Sliced(trackImg, Sprite("UI_Track"), Track, 1.6f);
        trackImg.raycastTarget = false;

        hpFill = Child(track, "HpFill");
        Undo.RecordObject(hpFill, "Game UI");
        hpFill.anchorMin = Vector2.zero; hpFill.anchorMax = Vector2.one; hpFill.pivot = new Vector2(0, 0.5f);
        hpFill.offsetMin = new Vector2(2, 2); hpFill.offsetMax = new Vector2(-2, -2);
        hpFill.localScale = Vector3.one;
        Image fillImg = Img(hpFill);
        Sliced(fillImg, Sprite("UI_Track"), fillColor, 2f);
        fillImg.raycastTarget = false;

        // 50%마다 눈금 (풍선 하나 = 한 칸, 풍선 2개)
        for (int i = 2; i <= 3; i++)
        {
            Transform old = track.Find("Notch" + i); // 풍선 4개 시절 눈금 정리
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        if (halfNotch)
        {
            RectTransform notch = Child(track, "Notch1");
            Undo.RecordObject(notch, "Game UI");
            notch.anchorMin = new Vector2(0.5f, 0); notch.anchorMax = new Vector2(0.5f, 1);
            notch.pivot = new Vector2(0.5f, 0.5f); notch.sizeDelta = new Vector2(3, 0); notch.anchoredPosition = Vector2.zero;
            notch.localScale = Vector3.one;
            Image n = Img(notch);
            n.sprite = null; n.color = Navy; n.raycastTarget = false;
            notch.SetAsLastSibling();
        }
        else RemoveChild(track, "Notch1");

        Undo.RecordObject(panel.gameObject, "Game UI");
        panel.gameObject.SetActive(false);
        hud = panel.gameObject;

        countdown = Label(canvas, "BossCountdown", new Vector2(0, 40), new Vector2(300, 220));
        Style(countdown, 160, TextAlignmentOptions.Center);
        countdown.color = Gold;
        countdown.text = "3";
        Undo.RecordObject(countdown.gameObject, "Game UI");
        countdown.gameObject.SetActive(false);
    }

    static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        RectTransform rt = Child(parent, name);
        Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        TextMeshProUGUI t = rt.GetComponent<TextMeshProUGUI>();
        if (t == null) t = Undo.AddComponent<TextMeshProUGUI>(rt.gameObject);
        t.raycastTarget = false;
        return t;
    }

    // 볼륨 슬라이더 한 줄: 시작 메뉴에 있으면 소리 설정 창(panel)으로 옮김
    static void SliderRow(RectTransform menu, RectTransform panel, string sliderName, string icon, float y)
    {
        RectTransform sl = (RectTransform)menu.Find(sliderName);
        if (sl == null) sl = (RectTransform)panel.Find(sliderName);
        if (sl.parent != panel) Undo.SetTransformParent(sl, panel, "Game UI");
        Place(sl, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(24, y), new Vector2(230, 26));
        RemoveChild(menu, sliderName + "Icon");
        Icon(panel, sliderName + "Icon", icon, Cream, new Vector2(-128, y), 40);

        RectTransform bg = (RectTransform)sl.Find("Background");
        Sliced(Img(bg), Sprite("UI_Track"), Track, 2f);
        bg.anchorMin = new Vector2(0, 0.2f); bg.anchorMax = new Vector2(1, 0.8f);

        RectTransform fillArea = (RectTransform)sl.Find("Fill Area");
        Undo.RecordObject(fillArea, "Game UI");
        fillArea.anchorMin = new Vector2(0, 0.2f); fillArea.anchorMax = new Vector2(1, 0.8f);
        Sliced(Img((RectTransform)fillArea.Find("Fill")), Sprite("UI_Track"), Gold, 2f);

        RectTransform handle = (RectTransform)sl.Find("Handle Slide Area/Handle");
        Image hi = Img(handle);
        hi.sprite = Sprite("UI_Knob"); hi.type = Image.Type.Simple; hi.color = Cream; hi.preserveAspect = true;
        Undo.RecordObject(handle, "Game UI");
        handle.sizeDelta = new Vector2(30, 6);
    }

    // 스토리 모드 BGM(스테이지별 고정) + 엔드리스에서 고를 수 있는 BGM 목록
    static void AssignMusic(BirdController bird)
    {
        AudioClip afternoon = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/RedsenGameMusic_Afternoon/RedsenGameMusic_Afternoon_Cute_Casual_Loopable.wav");
        AudioClip forest = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/ForestBGM.wav");
        AudioClip desert = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/DesertBGM.wav");
        AudioClip boss = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/BossBGM.wav");
        AudioClip ice = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/IceCaveBGM.wav");
        AudioClip eagle = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EagleBGM.wav");
        AudioClip escape = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/IceEscapeBGM.wav");
        Undo.RecordObject(bird, "Game UI");
        bird.storyBgm = bird.stageNumber == 4 ? afternoon : bird.stageNumber == 3 ? ice : bird.stageNumber == 2 ? desert : forest; // 도시는 경쾌한 오후 곡
        bird.endlessBgms = new[] { afternoon, forest, desert, boss, ice, eagle, escape }; // 새 곡은 맨 뒤에 (저장된 선택 번호가 안 밀리게)
        bird.endlessBgmNames = new[] { "AFTERNOON", "FOREST", "DESERT", "SNAKE BOSS", "ICE CAVE", "EAGLE CHASE", "CAVE ESCAPE" };
        // 배경음은 음악 믹서로 (메뉴에서 시간이 멈춰 있어도 볼륨 슬라이더가 바로 반영되게)
        bird.musicMixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Audio/MusicMixer.mixer");
        if (bird.musicMixer != null && bird.bgmAudioSource != null)
        {
            Undo.RecordObject(bird.bgmAudioSource, "Game UI");
            bird.bgmAudioSource.outputAudioMixerGroup = bird.musicMixer.FindMatchingGroups("Master")[0];
            EditorUtility.SetDirty(bird.bgmAudioSource);
        }
        EditorUtility.SetDirty(bird);
    }

    // 오른쪽 위 ♪ 동그라미 버튼 + 소리 설정 창
    // 창: 효과음/배경음 볼륨 → 안내 문구 → BGM 펼치기 버튼 (누르면 아래로 곡 목록)
    static void BuildSoundMenu(Transform canvas, RectTransform menu, Transform buttonSource, BirdController bird)
    {
        RectTransform root = Child(canvas, "SoundMenu");
        Undo.RecordObject(root, "Game UI");
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero; root.localScale = Vector3.one;
        root.SetSiblingIndex(menu.GetSiblingIndex() + 1); // 시작 메뉴 위에 그려짐
        SoundMenu sm = root.GetComponent<SoundMenu>();
        if (sm == null) sm = Undo.AddComponent<SoundMenu>(root.gameObject);

        // ♪ 동그라미 버튼
        RectTransform btn = Child(root, "SoundButton");
        Place(btn, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16), new Vector2(68, 68));
        Image bi = Img(btn);
        bi.sprite = Sprite("UI_Circle"); bi.type = Image.Type.Simple; bi.color = Color.white; bi.preserveAspect = true;
        Button bb = btn.GetComponent<Button>();
        if (bb == null) bb = Undo.AddComponent<Button>(btn.gameObject);
        SetClick(bb, sm.TogglePanel);
        Icon(btn, "Icon", "Icon_Music", Cream, Vector2.zero, 38);

        // 설정 창 (버튼 바로 아래)
        RectTransform panel = Child(root, "SoundPanel");
        Place(panel, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -94), new Vector2(340, 236));
        Sliced(Img(panel), Sprite("UI_Panel"), Color.white, 1f);

        // 볼륨: 효과음(스피커) / 배경음(음표)
        SliderRow(menu, panel, "Slider1", "Icon_Sound", 74);
        SliderRow(menu, panel, "Slider2", "Icon_Music", 20);

        // 안내 문구 (한글은 TMP 글꼴에 없어서 일반 Text + 실행 때 윈도우 한글 글꼴)
        RectTransform nrt = Child(panel, "BgmNotice");
        Place(nrt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -26), new Vector2(320, 26));
        Text nt = nrt.GetComponent<Text>();
        if (nt == null) nt = Undo.AddComponent<Text>(nrt.gameObject);
        Undo.RecordObject(nt, "Game UI");
        nt.text = "BGM 변경은 엔드리스 모드에만 적용됩니다";
        nt.fontSize = 16; nt.fontStyle = FontStyle.Bold; nt.color = Gold; nt.alignment = TextAnchor.MiddleCenter;
        nt.horizontalOverflow = HorizontalWrapMode.Overflow; nt.raycastTarget = false;
        Shadow ns = nrt.GetComponent<Shadow>();
        if (ns == null) ns = Undo.AddComponent<Shadow>(nrt.gameObject);
        ns.effectColor = new Color(0, 0, 0, 0.5f); ns.effectDistance = new Vector2(1, -1);

        // BGM 펼치기 버튼: 음표 + 곡 이름 + 오른쪽 화살표
        Vector2 ddSize = new Vector2(300, 48);
        Transform dd = CopyButton(panel, buttonSource, "BgmDropdown");
        Place((RectTransform)dd, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -74), ddSize);
        StyleButton(dd, bird.endlessBgmNames[0], "Icon_Music", ddSize, Teal, 0.4f);
        RectTransform ddText = (RectTransform)dd.GetComponentInChildren<TextMeshProUGUI>(true).transform;
        ddText.offsetMax = new Vector2(-48, 0); // 화살표 자리 비움
        Image arrow = Icon(dd, "Arrow", "Icon_Arrow", Cream, Vector2.zero, 26);
        Place((RectTransform)arrow.transform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-28, 0), new Vector2(26, 26));
        SetClick(dd.GetComponent<Button>(), sm.ToggleList);

        // 펼쳐지는 곡 목록 (펼치기 버튼 바로 아래, 창 밖으로 내려와도 됨). 줄은 실행 때 곡 수만큼 채움
        RectTransform list = Child(panel, "BgmList");
        list.SetAsLastSibling(); // 다른 것들 위에 그려짐
        Place(list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -74 - ddSize.y * 0.5f - 4), new Vector2(ddSize.x, 100));
        Sliced(Img(list), Sprite("UI_Panel"), Color.white, 1.6f);

        Vector2 optSize = new Vector2(ddSize.x - 12, 40);
        RectTransform opt = Child(list, "OptionTemplate");
        Place(opt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -6), optSize);
        Sliced(Img(opt), Sprite("UI_Button"), Color.white, 96f / optSize.y * 0.9f);
        Button ob = opt.GetComponent<Button>();
        if (ob == null) ob = Undo.AddComponent<Button>(opt.gameObject);
        TextMeshProUGUI ot = Label(opt, "Text", Vector2.zero, Vector2.zero);
        Undo.RecordObject(ot.rectTransform, "Game UI");
        ot.rectTransform.anchorMin = Vector2.zero; ot.rectTransform.anchorMax = Vector2.one;
        ot.rectTransform.offsetMin = new Vector2(18, 2); ot.rectTransform.offsetMax = new Vector2(-12, 0);
        Style(ot, 18, TextAlignmentOptions.MidlineLeft);
        ot.text = "BGM";
        Undo.RecordObject(opt.gameObject, "Game UI");
        opt.gameObject.SetActive(false);

        Undo.RecordObject(sm, "Game UI");
        sm.bird = bird;
        sm.soundButton = btn.gameObject;
        sm.panel = panel.gameObject;
        sm.currentName = dd.GetComponentInChildren<TextMeshProUGUI>(true);
        sm.arrow = (RectTransform)arrow.transform;
        sm.list = list;
        sm.optionTemplate = ob;
        sm.notice = nt;
        EditorUtility.SetDirty(sm);

        Undo.RecordObject(panel.gameObject, "Game UI");
        panel.gameObject.SetActive(false);
    }

    static void StatText(RectTransform menu, string name, float x)
    {
        RectTransform rt = (RectTransform)menu.Find(name);
        Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 18), new Vector2(160, 50));
        Style(rt.GetComponent<TextMeshProUGUI>(), 18, TextAlignmentOptions.Center);
    }

    // 체크박스 + 얼굴인식 아이콘 + 글자
    static void MotionToggle(RectTransform menu)
    {
        RectTransform tg = (RectTransform)menu.Find("Motion Toggle");
        Place(tg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(240, 34));

        RectTransform box = (RectTransform)tg.Find("Background");
        Place(box, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(30, 30));
        Sliced(Img(box), Sprite("UI_CheckBox"), Cream, 1.5f);

        RectTransform chk = (RectTransform)box.Find("Checkmark");
        Image ci = Img(chk);
        ci.sprite = Sprite("Icon_Check"); ci.color = Green; ci.preserveAspect = true;
        Undo.RecordObject(chk, "Game UI");
        chk.sizeDelta = new Vector2(30, 30);

        Image mi = Icon(tg, "MotionIcon", "Icon_Motion", Cream, Vector2.zero, 30);
        Place((RectTransform)mi.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(30, 30));

        RectTransform lbl = (RectTransform)tg.Find("Label");
        Undo.RecordObject(lbl, "Game UI");
        lbl.anchorMin = Vector2.zero; lbl.anchorMax = Vector2.one;
        lbl.offsetMin = new Vector2(82, 0); lbl.offsetMax = Vector2.zero;

        Text lt = lbl.GetComponent<Text>();
        Undo.RecordObject(lt, "Game UI");
        lt.text = "Motion Control";
        lt.fontSize = 18; lt.fontStyle = FontStyle.Bold; lt.color = Cream; lt.alignment = TextAnchor.MiddleLeft;

        Shadow sh = lbl.GetComponent<Shadow>();
        if (sh == null) sh = Undo.AddComponent<Shadow>(lbl.gameObject);
        sh.effectColor = new Color(0, 0, 0, 0.5f);
        sh.effectDistance = new Vector2(1, -1);
    }

    // ───────── 공용 ─────────

    static Sprite Sprite(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + name + ".png"); }

    static RectTransform Child(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return (RectTransform)t;
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5; // UI
        Undo.RegisterCreatedObjectUndo(go, "Game UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        Undo.RecordObject(rt, "Game UI");
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        rt.localScale = Vector3.one; // 기존 UI 일부가 2배로 키워져 있어서 크기 기준을 통일
    }

    static Image Img(RectTransform rt)
    {
        Image i = rt.GetComponent<Image>();
        if (i == null) i = Undo.AddComponent<Image>(rt.gameObject);
        Undo.RecordObject(i, "Game UI");
        return i;
    }

    static Image Icon(Transform parent, string name, string sprite, Color color, Vector2 pos, float size)
    {
        RectTransform rt = Child(parent, name);
        Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
        Image i = Img(rt);
        i.sprite = Sprite(sprite); i.color = color; i.preserveAspect = true; i.raycastTarget = false;
        return i;
    }

    static void Sliced(Image i, Sprite sprite, Color color, float ppu)
    {
        i.sprite = sprite; i.type = Image.Type.Sliced; i.color = color; i.pixelsPerUnitMultiplier = ppu;
    }

    static void Style(TextMeshProUGUI t, float size, TextAlignmentOptions align)
    {
        Undo.RecordObject(t, "Game UI");
        t.font = font; t.fontSharedMaterial = textMat;
        t.fontSize = size; t.fontStyle = FontStyles.Bold; t.color = Cream;
        t.alignment = align; t.textWrappingMode = TextWrappingModes.NoWrap;
    }
}
