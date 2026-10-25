using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using System.IO;

// 바탕화면에 실행용 데모 빌드 만들기
// 메뉴: Tools > Build Demo (Desktop)
// 결과: 바탕화면\SeagullFlight_Demo2\SeagullFlight.exe (+ 모션 조작용 파이썬 파일)
// 1차 데모(Demo1, 숲만)는 그대로 두고, 2차 데모(숲 + 사막)는 새 폴더에
public static class DemoBuilder
{
    const string FolderName = "SeagullFlight_Demo2";
    const string ExeName = "SeagullFlight.exe";

    [MenuItem("Tools/Build Demo (Desktop)")]
    public static void Build()
    {
        string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
        string outDir = Path.Combine(desktop, FolderName);
        Directory.CreateDirectory(outDir);

        // Build Settings에 켜져 있는 씬 전부 (첫 번째 = 게임 시작 씬)
        var scenes = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene s0 in EditorBuildSettings.scenes) if (s0.enabled) scenes.Add(s0.path);

        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = Path.Combine(outDir, ExeName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary s = report.summary;
        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError("데모 빌드 실패: " + s.result + " (에러 " + s.totalErrors + "개)");
            return;
        }

        // 모션 조작(웹캠 얼굴 인식): 게임이 exe 옆에서 이 파일들을 찾음
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        foreach (string file in new[] { "motion_tracker.py", "haarcascade_frontalface_default.xml", "hand_landmarker.task", "blaze_face_short_range.tflite" })
        {
            string src = Path.Combine(project, file);
            if (File.Exists(src)) File.Copy(src, Path.Combine(outDir, file), true);
        }

        // 파이썬 + OpenCV + MediaPipe 묶음: 받는 사람이 아무것도 설치하지 않아도 모션 조작이 되게 (이미 같은 크기로 있으면 건너뜀)
        CopyFolder(Path.Combine(project, "python_runtime"), Path.Combine(outDir, "python_runtime"));

        File.WriteAllText(Path.Combine(outDir, "README.txt"),
            "갈매기 비행 - 2차 데모 (스테이지 1 숲 + 스테이지 2 사막 + 스테이지 3 얼음 동굴 + 스테이지 4 도시)\r\n" +
            "\r\n" +
            "실행: SeagullFlight.exe 더블클릭\r\n" +
            "      (이 폴더 안의 파일들은 함께 있어야 합니다. exe만 따로 옮기면 실행되지 않아요)\r\n" +
            "\r\n" +
            "조작\r\n" +
            "  스페이스바 : 날갯짓 (위로)\r\n" +
            "  ← → 방향키 : 좌우 이동\r\n" +
            "  F4         : 전체화면 / 창 모드 전환\r\n" +
            "  메뉴 오른쪽 위 ♪ : 소리 크기 + BGM 선택 (화살표 → 스크롤 목록, BGM 선택은 엔드리스 모드에만 적용)\r\n" +
            "  Alt+F4     : 종료\r\n" +
            "\r\n" +
            "모드\r\n" +
            "  STORY        : 비행 후 보스전 (숲: 300m 뱀 보스 / 사막: 500m 독수리에게서 40초 버티기)\r\n" +
            "                 숲 스토리는 둥지의 알을 ← → 로 번갈아 눌러 깨고 나와서 출발\r\n" +
            "  ENDLESS      : 끝없이 날기 (최고 기록 저장)\r\n" +
            "  STAGE 1/2 BOSS : 바로 보스전 / STAGE 3 ESCAPE : 바로 동굴 붕괴 탈출\r\n" +
            "  STAGE 2      : 사막 스테이지로 (스테이지 1 보스를 깨면 자동으로 넘어감, 300m~350m, 400m~450m 모래 바람)\r\n" +
            "  STAGE 3      : 얼음 동굴 (사막 독수리를 따돌리면 자동으로 넘어감)\r\n" +
            "                 벽을 지날 때마다 40% 확률로 몸이 얼어붙음 → ← → 를 2번 눌러 2초 안에 깨고 나오기\r\n" +
            "                 300m부터 동굴이 무너짐 → 고드름·얼음 기둥·얼음 바위를 피하며 35초 버티면 탈출\r\n" +
            "  STAGE 4      : 도시 (얼음 동굴을 탈출하면 자동으로 넘어감)\r\n" +
            "                 빌딩 사이 큰길에서 신호등과 마주 달려오는 자동차를 피하며 400m까지\r\n" +
            "                 400m: 새총 쏘는 잼민이 보스 → 30초 동안 돌멩이를 피하면... 엔딩 (THE END)\r\n" +
            "                 (조준·지그재그·돌멩이 비·움직이지 마!·틈 찾기·마지막 연사)\r\n" +
            "  STAGE 4 BOSS : 바로 새총 아이 보스전\r\n" +
            "  메뉴 아래 STAGE 1~4 버튼 : 원하는 스테이지로 바로 이동\r\n" +
            "                 너무 높이(경고가 뜨는 높이) 2.5초 머물면 헬기에 부딪혀 추락하니 조심!\r\n" +
            "                 (빨간 경고가 뜨고 1.8초 뒤에 날아옴)\r\n" +
            "\r\n" +
            "독수리 추격전과 동굴 탈출은 새 앞에서 얼굴을 보는 시점이라, 좌우 방향키는 화면 기준으로 움직입니다.\r\n" +
            "\r\n" +
            "Motion Control (웹캠 조작)\r\n" +
            "  고개를 좌우로 움직이기 : 좌우 이동 (출발할 때 고개 위치가 가운데)\r\n" +
            "  손을 위아래로 흔들기   : 아래로 내리칠 때마다 날갯짓\r\n" +
            "  Motion Control 옆 카메라 버튼 : 화살표를 눌러 연결된 웹캠 이름 목록에서 고르기 (자동 선택 / 기기 이름)\r\n" +
            "                           카메라가 여러 개이거나 OBS 가상 카메라가 잡히면 여기서 바꾸세요\r\n" +
            "파이썬/OpenCV/MediaPipe는 python_runtime 폴더에 들어 있어서 따로 설치할 필요 없습니다.\r\n" +
            "웹캠이 없으면 꺼두고 키보드로 플레이하세요.\r\n",
            System.Text.Encoding.UTF8);

        Debug.Log("데모 빌드 완료: " + outDir + " (" + (s.totalSize / (1024 * 1024)) + "MB, " + s.totalTime.TotalSeconds.ToString("F0") + "초)");
        EditorUtility.RevealInFinder(Path.Combine(outDir, ExeName));
    }

    static void CopyFolder(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(dst, file.Substring(src.Length + 1));
            if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(file).Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }
    }
}
