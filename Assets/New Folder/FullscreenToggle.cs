using UnityEngine;

// F4: 진짜 전체화면(창 제목줄/테두리 없음, 작업표시줄까지 덮음) ↔ 창 모드 전환
// 게임 시작 때 저절로 생기고 씬이 바뀌어도 남아 있어서, 씬에 따로 붙일 필요 없음
public class FullscreenToggle : MonoBehaviour
{
    const int WindowWidth = 1280;
    const int WindowHeight = 720;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        GameObject go = new GameObject("FullscreenToggle");
        DontDestroyOnLoad(go);
        go.AddComponent<FullscreenToggle>();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F4)) return;
        // Alt+F4(종료)를 누를 때는 전환하지 않음
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;

        if (Screen.fullScreenMode == FullScreenMode.Windowed)
        {
            Resolution native = Screen.currentResolution;
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            Screen.SetResolution(WindowWidth, WindowHeight, FullScreenMode.Windowed);
        }
    }
}
