using UnityEngine;
using UnityEngine.UI;

// 화살표를 누르면 펼쳐지는 목록을 스크롤되게 만듦 (BGM 선택, 카메라 선택에서 같이 씀)
// 목록 창 안에 보이는 칸(Viewport) + 줄들이 들어가는 Content + 오른쪽 스크롤바를 만들고,
// 줄이 maxRows보다 많으면 마우스 휠/드래그로 넘겨 봄
public static class DropdownList
{
    const float Pad = 6f;          // 목록 창 위아래 여백
    const float BarWidth = 8f;     // 스크롤바 굵기

    // list: 목록 창 (위쪽 가운데 기준, 펼치기 버튼 바로 아래). 반환값: 줄을 넣을 Content
    public static RectTransform MakeScrollable(RectTransform list)
    {
        Transform old = list.Find("Viewport");
        if (old != null) return (RectTransform)old.Find("Content");

        RectTransform viewport = NewRect("Viewport", list);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(0f, Pad);
        viewport.offsetMax = new Vector2(0f, -Pad);
        viewport.gameObject.AddComponent<RectMask2D>();
        // 줄 사이 빈 곳을 잡아 끌어도 스크롤되게 (투명)
        Image hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        RectTransform content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        // 스크롤바: 오른쪽 안쪽에 가는 막대 (어두운 홈 + 크림색 손잡이)
        RectTransform bar = NewRect("Scrollbar", list);
        bar.anchorMin = new Vector2(1f, 0f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(1f, 0.5f);
        bar.sizeDelta = new Vector2(BarWidth, -Pad * 2f - 4f);
        bar.anchoredPosition = new Vector2(-5f, 0f);
        Image track = bar.gameObject.AddComponent<Image>();
        track.color = new Color(0.05f, 0.07f, 0.16f, 0.8f);
        RectTransform area = NewRect("Sliding Area", bar);
        Stretch(area);
        RectTransform handle = NewRect("Handle", area);
        Stretch(handle);
        Image hi = handle.gameObject.AddComponent<Image>();
        hi.color = new Color(0.965f, 0.906f, 0.757f);
        Scrollbar sb = bar.gameObject.AddComponent<Scrollbar>();
        sb.handleRect = handle;
        sb.targetGraphic = hi;
        sb.direction = Scrollbar.Direction.BottomToTop;

        ScrollRect sr = list.gameObject.AddComponent<ScrollRect>();
        sr.content = content;
        sr.viewport = viewport;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 25f;
        sr.verticalScrollbar = sb;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }

    // 줄 수에 맞춰 Content 높이와 목록 창 높이(최대 maxRows줄)를 정하고, 줄을 위에서부터 쌓음
    public static void Layout(RectTransform list, RectTransform content, RectTransform[] rows, float rowH, int maxRows)
    {
        bool scroll = rows.Length > maxRows;
        float right = scroll ? BarWidth + 10f : 6f;
        for (int i = 0; i < rows.Length; i++)
        {
            RectTransform r = rows[i];
            float h = r.sizeDelta.y;
            r.SetParent(content, false);
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.offsetMin = new Vector2(6f, 0f);
            r.offsetMax = new Vector2(-right, 0f);
            r.sizeDelta = new Vector2(r.sizeDelta.x, h);
            r.anchoredPosition = new Vector2(r.anchoredPosition.x, -i * rowH);
        }
        content.sizeDelta = new Vector2(0f, rows.Length * rowH);
        int shown = Mathf.Clamp(rows.Length, 1, maxRows);
        list.sizeDelta = new Vector2(list.sizeDelta.x, shown * rowH + Pad * 2f - 4f);
    }

    // 고른 줄이 보이도록 스크롤 (펼칠 때)
    public static void ScrollTo(RectTransform list, int index, int count, int maxRows)
    {
        ScrollRect sr = list.GetComponent<ScrollRect>();
        if (sr == null) return;
        Canvas.ForceUpdateCanvases();
        if (count <= maxRows) { sr.verticalNormalizedPosition = 1f; return; }
        int top = Mathf.Clamp(index - maxRows / 2, 0, count - maxRows);
        sr.verticalNormalizedPosition = 1f - (float)top / (count - maxRows);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.layer = 5; // UI
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
