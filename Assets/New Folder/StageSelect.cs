using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// 시작 메뉴 아래에 모든 스테이지로 바로 가는 버튼 한 줄 (STAGE 1 FOREST ~ STAGE 4 CITY)
// 지금 스테이지 버튼은 흐리게(누를 수 없음). 누르면 그 스테이지의 시작 메뉴로 바로 이동
// 모드 버튼(STORY)을 복제해서 같은 모양으로, 실행 때 만듦 → 씬마다 따로 꾸밀 필요 없음 (BirdController.Start에서 호출)
public static class StageSelect
{
    static readonly string[] Scenes = { "SampleScene", "Stage2_Desert", "Stage3_IceCave", "Stage4_City" };
    static readonly string[] Names = { "FOREST", "DESERT", "ICE CAVE", "CITY" };
    static readonly Color[] Colors =
    {
        new Color(0.30f, 0.62f, 0.33f), // 숲 초록
        new Color(0.85f, 0.55f, 0.25f), // 사막 주황
        new Color(0.30f, 0.62f, 0.86f), // 얼음 파랑
        new Color(0.55f, 0.45f, 0.70f), // 도시 보라
    };

    public static void Build(GameObject menu, int currentStage)
    {
        if (menu == null || menu.transform.Find("Stage Select") != null) return;
        Transform source = menu.transform.Find("Story Button");
        if (source == null) source = menu.transform.Find("Start Button");
        if (source == null) return;

        RectTransform panel = (RectTransform)menu.transform;
        RectTransform row = new GameObject("Stage Select", typeof(RectTransform)).GetComponent<RectTransform>();
        row.gameObject.layer = 5;
        row.SetParent(panel, false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0.5f, 1f);
        // 메뉴 패널 바로 아래
        row.anchoredPosition = new Vector2(0f, -panel.sizeDelta.y * 0.5f - 10f);
        Vector2 size = new Vector2(98f, 52f);
        const float gap = 8f;
        row.sizeDelta = new Vector2(size.x * Scenes.Length + gap * (Scenes.Length - 1), size.y);

        for (int i = 0; i < Scenes.Length; i++)
        {
            GameObject b = Object.Instantiate(source.gameObject, row);
            b.name = "Go Stage " + (i + 1);
            RectTransform rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(i * (size.x + gap), 0f);
            rt.sizeDelta = size;

            Transform icon = b.transform.Find("Icon");
            if (icon != null) Object.Destroy(icon.gameObject);
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = "STAGE " + (i + 1) + "\n" + Names[i];
                RectTransform lrt = label.rectTransform;
                lrt.offsetMin = new Vector2(4f, 2f);
                lrt.offsetMax = new Vector2(-4f, -2f);
                label.enableAutoSizing = true;
                label.fontSizeMin = 10f;
                label.fontSizeMax = 17f;
                label.alignment = TextAlignmentOptions.Center;
            }

            bool here = i + 1 == currentStage;
            Image img = b.GetComponent<Image>();
            if (img != null)
            {
                img.color = here ? new Color(0.35f, 0.37f, 0.45f) : Colors[i];
                img.pixelsPerUnitMultiplier = 96f / size.y * 0.9f;
            }
            Button btn = b.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent(); // 복제한 버튼의 원래 동작(스토리 시작) 지움
            btn.interactable = !here;
            if (label != null && here) label.alpha = 0.55f;
            string scene = Scenes[i];
            btn.onClick.AddListener(() => Go(scene));
        }
    }

    static void Go(string scene)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene);
    }
}
