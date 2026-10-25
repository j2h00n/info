using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// 시작 메뉴 오른쪽 위 ♪ 동그라미 버튼 → 소리 설정 창 (효과음/배경음 볼륨 + 엔드리스용 BGM 선택)
// BGM 선택은 화살표를 누르면 아래로 목록이 펼쳐짐 (4줄보다 많으면 스크롤). 고른 곡은 메뉴에서 바로 들려주고 엔드리스 모드에만 적용
// UI는 Tools > Apply Game UI Style 에서 만들어지고, 여기선 열고 닫기/목록 채우기만 함
public class SoundMenu : MonoBehaviour
{
    public BirdController bird;
    public GameObject soundButton;       // 오른쪽 위 ♪ 동그라미
    public GameObject panel;             // 소리 설정 창
    public TextMeshProUGUI currentName;  // 펼치기 버튼에 보이는 지금 고른 곡 이름
    public RectTransform arrow;          // 펼치기 화살표 (펼치면 위로 뒤집힘)
    public RectTransform list;           // 펼쳐지는 곡 목록
    public Button optionTemplate;        // 목록 한 줄 (곡 수만큼 복제)
    public Text notice;                  // "BGM 변경은 엔드리스 모드에만 적용됩니다" (한글이라 윈도우 글꼴 사용)

    public Color optionColor = new Color(0.20f, 0.24f, 0.42f);
    public Color selectedColor = new Color(0.20f, 0.58f, 0.55f);

    public int maxRows = 4;              // 목록에 한 번에 보이는 곡 수 (더 많으면 스크롤)

    private readonly List<Image> optionImages = new List<Image>();

    void Start()
    {
        if (bird == null) bird = FindAnyObjectByType<BirdController>();
        if (notice != null)
            notice.font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, notice.fontSize);

        // 곡 목록 채우기 (템플릿 높이 + 간격 4로 아래로 쌓고, 많으면 스크롤)
        string[] names = bird.endlessBgmNames ?? new string[0];
        float rowH = ((RectTransform)optionTemplate.transform).sizeDelta.y + 4f;
        RectTransform content = DropdownList.MakeScrollable(list);
        var rows = new RectTransform[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            Button b = Instantiate(optionTemplate, content);
            b.gameObject.SetActive(true);
            b.name = "Option " + names[i];
            rows[i] = (RectTransform)b.transform;
            b.GetComponentInChildren<TextMeshProUGUI>(true).text = names[i];
            int index = i;
            b.onClick.AddListener(() => Choose(index));
            optionImages.Add(b.GetComponent<Image>());
        }
        DropdownList.Layout(list, content, rows, rowH, maxRows);

        panel.SetActive(false);
        list.gameObject.SetActive(false);
        Refresh();
    }

    void Update()
    {
        // 시작 메뉴가 떠 있을 때만 버튼이 보이고, 게임이 시작되면 창도 닫힘
        bool menuShown = bird != null && bird.startMenuPanel != null && bird.startMenuPanel.activeInHierarchy;
        if (soundButton.activeSelf != menuShown) soundButton.SetActive(menuShown);
        if (!menuShown && panel.activeSelf) panel.SetActive(false);
    }

    public void TogglePanel()
    {
        panel.SetActive(!panel.activeSelf);
        list.gameObject.SetActive(false);
        Refresh();
    }

    public void ToggleList()
    {
        list.gameObject.SetActive(!list.gameObject.activeSelf);
        Refresh();
        if (list.gameObject.activeSelf) DropdownList.ScrollTo(list, bird.EndlessBgmIndex, optionImages.Count, maxRows);
    }

    void Choose(int index)
    {
        bird.SelectEndlessBgm(index);
        list.gameObject.SetActive(false);
        Refresh();
    }

    void Refresh()
    {
        int sel = bird.EndlessBgmIndex;
        string[] names = bird.endlessBgmNames;
        if (currentName != null && names != null && sel < names.Length) currentName.text = names[sel];
        if (arrow != null) arrow.localScale = new Vector3(1f, list.gameObject.activeSelf ? -1f : 1f, 1f);
        for (int i = 0; i < optionImages.Count; i++) optionImages[i].color = i == sel ? selectedColor : optionColor;
    }
}
