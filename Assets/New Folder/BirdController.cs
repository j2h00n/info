using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI; 
using TMPro;

public class BirdController : MonoBehaviour
{
    public float forwardSpeed = 15f;
    public float flapForce = 7f;
    public float sideSpeed = 10f;
    public float tiltAngle = 35f; 
    public float tiltSpeed = 7f;
    public float fallLimitY = -5f; // 바닥 높이: 이 아래로는 안 떨어지고 바닥을 스치듯 날아감 (보스전에선 독바다)
    public float ceilingY = 17f;   // 천장 높이: 이 위로는 못 올라감 (장애물 틈 꼭대기 12.5보다 여유 있게)

    public GameObject restartButton;
    public GameObject menuButton;   // 재시작 버튼 옆 "메뉴로 돌아가기" (재시작 버튼과 같이 켜지고 꺼짐)
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI wallText;

    public GameObject startMenuPanel;
    public TextMeshProUGUI highScoreText;
    public TextMeshProUGUI highWallText;
    public Slider jumpSoundSlider;
    public Slider musicSoundSlider; 
    
    public Toggle motionToggle; 
    private bool useMotionControl = false; 

    public AudioClip jumpSound;
    public AudioClip bgmClip;
    public AudioSource bgmAudioSource;
    // 배경음 볼륨은 믹서로 조절: 메뉴는 시간이 멈춰 있어서(timeScale 0) AudioSource.volume을 바꿔도 이미 재생 중인 곡엔 반영이 안 됨
    // 믹서는 UnscaledTime으로 돌아서 멈춘 동안에도 바로 바뀜
    public UnityEngine.Audio.AudioMixer musicMixer;

    // 🔥 배경음: 스토리 모드는 스테이지 전용 곡 고정(보스전 제외), 엔드리스는 BGM 선택 창에서 고른 곡
    public AudioClip storyBgm;
    public AudioClip[] endlessBgms;
    public string[] endlessBgmNames;     // BGM 선택 창에 보이는 이름 (endlessBgms와 같은 순서)
    const string EndlessBgmKey = "EndlessBgm";
    public int EndlessBgmIndex
    {
        get { return endlessBgms == null || endlessBgms.Length == 0 ? 0 : Mathf.Clamp(PlayerPrefs.GetInt(EndlessBgmKey, 0), 0, endlessBgms.Length - 1); }
    }
    public AudioClip StoryMusic { get { return storyBgm != null ? storyBgm : bgmClip; } }
    AudioClip EndlessMusic { get { return endlessBgms != null && endlessBgms.Length > 0 ? endlessBgms[EndlessBgmIndex] : bgmClip; } }

    // 🔥 게임 모드: 스토리(목표 거리 도달하면 클리어) / 엔드리스(기존 무한 모드)
    public enum GameMode { Story, Endless }
    public float storyGoalMeters = 500f;
    public GameObject scoreBadge;  // 왼쪽 위 점수판 (게임 시작 전엔 숨김)
    public GameObject wallBadge;   // 오른쪽 위 벽 개수 (시작 전 + 스토리 모드에선 숨김)
    public GameObject clearPanel;  // 스토리 클리어 화면
    public BossFight bossFight;    // 목표 거리에서 등장하는 뱀 보스 (없으면 바로 클리어)
    public EagleChase eagleChase;  // 사막 스테이지 보스: 독수리 추격전
    public HatchIntro hatchIntro;  // 1스테이지 스토리 시작: 둥지의 알을 깨고 나옴
    public IceCaveEscape iceEscape; // 얼음 동굴 스테이지 300m: 동굴 붕괴 탈출
    public KidBoss kidBoss;         // 도시 스테이지 400m: 새총 쏘는 아이 (30초 버티면 엔딩 컷씬)
    private GameMode mode = GameMode.Endless;

    // 재시작: 씬을 다시 불러온 뒤 메뉴 없이 방금 하던 방식으로 바로 출발 (static이라 씬이 바뀌어도 남아 있음)
    enum StartKind { None, Story, Endless, Boss }
    private static StartKind pendingRestart = StartKind.None;
    private StartKind lastStart = StartKind.None;
    private bool isCleared = false;
    private bool bossStarted = false;

    public bool IsGameOver { get { return isGameOver; } }
    public bool BossStarted { get { return bossStarted; } }
    public bool UsesMotionControl { get { return useMotionControl; } }
    public bool IsPlaying { get { return isGameStarted && !isGameOver && !cutscene; } } // 조작 중 (얼음 이벤트가 걸릴 수 있는 상태)
    public bool IsStory { get { return mode == GameMode.Story; } }
    public float MoveInput { get { return GetMoveX(); } } // 좌우 입력 (방향키 + 모션)

    [HideInInspector] public float windX; // 모래 바람이 옆으로 미는 속도 (Sandstorm이 정함)
    [HideInInspector] public bool cutscene;       // 보스 등장 컷씬 중: 조작을 안 받고 EagleChase가 직접 움직임
    [HideInInspector] public bool mirrorControls; // 새 앞에서 얼굴을 보는 시점: 화면 기준으로 좌우가 맞게 반대로

    // 🔥 스테이지: 씬마다 하나 (1 = 숲, 2 = 사막 ...)
    public int stageNumber = 1;
    public string stageName = "FOREST";  // 메뉴 위에 "STAGE 1 - FOREST"처럼 표시
    public string nextStageScene = "";   // 클리어 후 NEXT STAGE / 메뉴의 다음 스테이지 버튼으로 갈 씬
    public string prevStageScene = "";   // 메뉴의 이전 스테이지 버튼으로 갈 씬
    public GameObject nextStageButton;   // 스토리 클리어 화면의 NEXT STAGE 버튼
    [TextArea] public string nextStageTip = "tip. 다음 스테이지 부터는 300m~350m, 400m~450m 구간에 모래 바람이 붑니다."; // 넘어갈 때 검은 화면에 표시

    // 1스테이지는 예전 기록 그대로, 2스테이지부터는 스테이지별로 따로 저장
    string BestScoreKey { get { return stageNumber <= 1 ? "BestScore" : "BestScore_S" + stageNumber; } }
    string BestWallKey { get { return stageNumber <= 1 ? "BestWall" : "BestWall_S" + stageNumber; } }

    // 다음 스테이지는 페이드 + 팁 화면을 거쳐서 (메뉴 버튼으로 갈 땐 도착해서 메뉴부터)
    public void LoadNextStage() { StageTransition.Go(nextStageScene, nextStageTip, false); }
    public void LoadPrevStage() { LoadStage(prevStageScene); }

    void LoadStage(string scene)
    {
        if (string.IsNullOrEmpty(scene)) return;
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene);
    }

    private SeagullFlap flap;
    private Rigidbody rb;
    private AudioSource jumpAudioSource; 
    private bool isGameOver = false;
    private bool isGameStarted = false; 
    private float startTime; 
    
    private int bestScore = 0;
    private int bestWall = 0;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        flap = GetComponentInChildren<SeagullFlap>();
        if (clearPanel != null) clearPanel.SetActive(false);
        // 시작 메뉴에서는 점수판 숨김 → StartGame에서 보여줌
        if (scoreBadge != null) scoreBadge.SetActive(false);
        if (wallBadge != null) wallBadge.SetActive(false);

        AudioSource[] audioSources = GetComponents<AudioSource>();
        if (audioSources.Length > 0) jumpAudioSource = audioSources[0];
        
        float savedJumpVol = PlayerPrefs.GetFloat("JumpVolume", 1f);
        float savedMusicVol = PlayerPrefs.GetFloat("MusicVolume", 1f);

        useMotionControl = PlayerPrefs.GetInt("MotionMode", 0) == 1;

        if (jumpAudioSource != null) jumpAudioSource.volume = Loudness(savedJumpVol);
        ApplyMusicVolume(savedMusicVol);

        PlayMusic(EndlessMusic); // 메뉴에선 엔드리스용으로 고른 곡을 미리 들려줌

        SetEndButtons(false);
        StageSelect.Build(startMenuPanel, stageNumber); // 메뉴 아래 모든 스테이지 바로가기

        Time.timeScale = 0f;
        isGameStarted = false;
        
        bestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
        bestWall = PlayerPrefs.GetInt(BestWallKey, 0);
        if (nextStageButton != null) nextStageButton.SetActive(false);
        // 스테이지 전환 화면을 거쳐 들어온 경우: 페이드인 동안 메뉴를 숨김 (끝나면 전환 쪽에서 스토리 시작 또는 메뉴 표시)
        if (StageTransition.IsRunning && startMenuPanel != null) startMenuPanel.SetActive(false);
        
        if(highScoreText != null) highScoreText.text = "High Score\n" + bestScore.ToString();
        if(highWallText != null) highWallText.text = "High Wall\n" + bestWall.ToString();
        
        // 저장된 위치로 손잡이만 옮김 (값 변경 알림을 보내면 시작하자마자 미리듣기 소리가 남)
        if(jumpSoundSlider != null) jumpSoundSlider.SetValueWithoutNotify(savedJumpVol);
        if(musicSoundSlider != null) musicSoundSlider.SetValueWithoutNotify(savedMusicVol);

        if (motionToggle != null)
        {
            motionToggle.isOn = useMotionControl;
            // 토글을 켜고 끄는 즉시 반영 (예전엔 씬을 다시 불러와야 적용돼서, 켜도 모션이 안 먹었음)
            motionToggle.onValueChanged.AddListener(OnMotionToggleChanged);
        }

        if (pendingRestart != StartKind.None)
        {
            if (startMenuPanel != null) startMenuPanel.SetActive(false);
            StartCoroutine(RestartNextFrame(pendingRestart));
            pendingRestart = StartKind.None;
        }
    }

    // 보스/독수리 등 다른 오브젝트의 Start가 끝난 뒤에 출발하도록 한 프레임 기다림
    System.Collections.IEnumerator RestartNextFrame(StartKind kind)
    {
        yield return null;
        switch (kind)
        {
            case StartKind.Story: BeginStory(false); break; // 재시작 땐 알 깨기 건너뜀
            case StartKind.Endless: StartEndless(); break;
            case StartKind.Boss: StartBossDirect(); break;
        }
    }

    // 모션 조작: 모드를 고르는 순간의 고개 위치를 가운데 칸으로 (알 깨기 뒤 출발할 땐 다시 잡지 않음 → 알 깨며 기운 자세가 기준이 되지 않게)
    void RecenterMotion()
    {
        MotionController motion = GetComponent<MotionController>();
        if (motion != null) motion.Recenter();
    }

    void SetEndButtons(bool on)
    {
        if (restartButton != null) restartButton.SetActive(on);
        if (menuButton != null) menuButton.SetActive(on);
    }

    void Update()
    {
        if (!isGameStarted || isGameOver || cutscene) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            Jump();
        }

        int currentScore = Mathf.Max(0, (int)transform.position.z);
        // 첫 벽은 z=20, 그 뒤로 벽 간격마다 하나씩
        int passedWalls = Mathf.Max(0, Mathf.FloorToInt((transform.position.z - NewMonoBehaviourScript.FirstChunkZ) / NewMonoBehaviourScript.Spacing) + 1);

        if (mode == GameMode.Story)
        {
            // 스토리: 거리(m)만 표시, 목표 도달하면 보스전 (보스전 중엔 거리 고정)
            if (!bossStarted)
            {
                if (scoreText != null) scoreText.text = currentScore + "m";
                if (currentScore >= storyGoalMeters)
                {
                    bossStarted = true;
                    if (scoreText != null) scoreText.text = storyGoalMeters.ToString("0") + "m";
                    if (bossFight != null) bossFight.Begin();
                    else if (eagleChase != null) eagleChase.Begin();
                    else if (iceEscape != null) iceEscape.Begin();
                    else if (kidBoss != null) kidBoss.Begin();
                    else { StageClear(); return; }
                }
            }
        }
        else
        {
            if (scoreText != null && wallText != null)
            {
                // 갈매기/바위벽 아이콘이 옆에 있으므로 숫자만 표시
                scoreText.text = currentScore.ToString();
                wallText.text = passedWalls.ToString();
            }

            // 최고 기록은 엔드리스 모드에서만 갱신
            if (currentScore > bestScore)
            {
                bestScore = currentScore;
                PlayerPrefs.SetInt(BestScoreKey, bestScore);
            }
            if (passedWalls > bestWall)
            {
                bestWall = passedWalls;
                PlayerPrefs.SetInt(BestWallKey, bestWall);
            }
        }

        // 좌우로 움직이는 만큼 몸을 기울임 (이동과 같은 입력 사용)
        float moveX = GetMoveX();
        Quaternion targetRotation = Quaternion.Euler(0f, 0f, -moveX * tiltAngle);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * tiltSpeed);
    }

    void FixedUpdate()
    {
        // 바닥 아래로는 못 내려감 (떨어져도 죽지 않음). 게임오버 뒤에도 적용 → 죽은 새가 독바다 밑으로 빠지지 않게
        if (!rb.isKinematic && rb.position.y <= fallLimitY && rb.linearVelocity.y < 0f)
        {
            rb.position = new Vector3(rb.position.x, fallLimitY, rb.position.z);
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }
        // 천장에 닿으면 더 못 올라가고 그 자리에서 다시 떨어지기 시작
        if (!rb.isKinematic && rb.position.y >= ceilingY && rb.linearVelocity.y > 0f)
        {
            rb.position = new Vector3(rb.position.x, ceilingY, rb.position.z);
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }

        if (!isGameStarted || isGameOver || cutscene) return;

        float moveX = GetMoveX();

        // 🔥 갈매기가 굳지 않고 부드럽게 움직이도록 물리 속도 적용
        rb.linearVelocity = new Vector3(moveX * sideSpeed + windX, rb.linearVelocity.y, forwardSpeed);
    }

    // 좌우 입력: 방향키와 고개(모션) 중 더 크게 움직인 쪽을 사용
    // (예전엔 기울기만 모션 값만 써서, 모션 모드를 켜면 방향키로 움직여도 몸이 안 기울었음)
    float GetMoveX()
    {
        float moveX = Input.GetAxis("Horizontal");
        if (useMotionControl)
        {
            MotionController motion = GetComponent<MotionController>();
            if (motion != null)
            {
                float motionMoveX = motion.GetMotionMoveX();
                if (Mathf.Abs(motionMoveX) > Mathf.Abs(moveX)) moveX = motionMoveX;
            }
        }
        if (mirrorControls) moveX = -moveX;
        return Mathf.Clamp(moveX, -1f, 1f);
    }

    public void TriggerJump()
    {
        if (!isGameStarted || isGameOver || cutscene) return;
        if (!useMotionControl) return;

        Jump();
    }

    void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * flapForce, ForceMode.Impulse);
        if (jumpAudioSource != null && jumpSound != null)
        {
            jumpAudioSource.PlayOneShot(jumpSound);
        }
        if (flap != null) flap.Flap(); // 날개 파닥파닥
    }

    public void OnMotionToggleChanged(bool isOn)
    {
        useMotionControl = isOn;
        PlayerPrefs.SetInt("MotionMode", isOn ? 1 : 0);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isGameStarted) return; 
        if (Time.time - startTime < 1.0f) return;
        if (collision.transform.IsChildOf(transform)) return;

        if (isCleared) return;

        if (collision.gameObject.CompareTag("Obstacle") || collision.gameObject.name.Contains("Cube"))
        {
            GameOver();
        }
    }

    public void GameOver()
    {
        if (isGameOver) return;
        isGameOver = true;
        SetEndButtons(true);
    }

    // 스토리 모드 목표 거리 도달: 새를 공중에 멈추고 클리어 화면 표시
    public void StageClear()
    {
        isCleared = true;
        isGameOver = true;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true; // 제자리에서 날갯짓하며 떠 있게
        if (clearPanel != null) clearPanel.SetActive(true);

        // 다음 스테이지가 있으면 버튼 없이 바로 넘어감 (클리어 화면이 보이는 채로 페이드아웃 시작) → 도착하면 스토리 바로 시작
        if (!string.IsNullOrEmpty(nextStageScene))
        {
            StageTransition.Go(nextStageScene, nextStageTip, true);
            return;
        }
        SetEndButtons(true);
    }

    // RESTART: 메뉴를 거치지 않고 방금 하던 모드로 바로 다시 출발
    public void RestartGame()
    {
        pendingRestart = lastStart;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // MENU: 처음 시작 메뉴로
    public void BackToMenu()
    {
        pendingRestart = StartKind.None;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void StartStory() { BeginStory(true); }

    void BeginStory(bool withHatch)
    {
        lastStart = StartKind.Story;
        RecenterMotion();
        mode = GameMode.Story;
        PlayMusic(StoryMusic);
        if (wallBadge != null) wallBadge.SetActive(false);
        if (scoreText != null) scoreText.text = "0m";
        // 1스테이지: 둥지의 알을 깨고 나와야 출발 (다 깨면 HatchIntro가 StartGame 호출)
        if (withHatch && hatchIntro != null)
        {
            if (startMenuPanel != null) startMenuPanel.SetActive(false);
            hatchIntro.Begin();
            return;
        }
        StartGame();
    }

    // 시작 메뉴의 "STAGE 1 BOSS" 버튼: 500m 비행 없이 바로 보스전
    public void StartBossDirect()
    {
        lastStart = StartKind.Boss;
        RecenterMotion();
        mode = GameMode.Story;
        if (wallBadge != null) wallBadge.SetActive(false);
        if (scoreText != null) scoreText.text = "BOSS";
        bossStarted = true;
        StartGame();
        if (bossFight != null) bossFight.Begin();
        else if (eagleChase != null) eagleChase.Begin();
        else if (iceEscape != null) iceEscape.Begin();
        else if (kidBoss != null) kidBoss.Begin();
    }

    public void StartEndless()
    {
        lastStart = StartKind.Endless;
        RecenterMotion();
        mode = GameMode.Endless;
        PlayMusic(EndlessMusic);
        if (wallBadge != null) wallBadge.SetActive(true);
        StartGame();
    }

    public void StartGame()
    {
        if (scoreBadge != null) scoreBadge.SetActive(true);
        if (wallBadge != null) wallBadge.SetActive(mode == GameMode.Endless); // 스토리/보스전에선 벽 개수 안 보임
        isGameStarted = true;
        Time.timeScale = 1f; 
        startTime = Time.time; 
        if(startMenuPanel != null) startMenuPanel.SetActive(false);
    }
    
    // 슬라이더 위치(0~1) → 실제 볼륨. 귀는 소리 크기를 로그로 느껴서 그대로 쓰면 중간쯤에선 거의 안 바뀌는 것처럼 들림
    static float Loudness(float slider) { return slider * slider; }

    private float lastJumpPreview = -1f;

    public void OnJumpVolumeChanged(float volume)
    {
        if(jumpAudioSource != null) jumpAudioSource.volume = Loudness(volume);
        PlayerPrefs.SetFloat("JumpVolume", volume);

        // 메뉴에선 효과음이 안 나서 바뀐 게 안 들리므로, 움직이는 동안 날갯짓 소리로 미리 들려줌 (메뉴는 시간이 멈춰 있어 unscaled)
        if (jumpAudioSource != null && jumpSound != null && Time.unscaledTime - lastJumpPreview > 0.18f)
        {
            lastJumpPreview = Time.unscaledTime;
            jumpAudioSource.PlayOneShot(jumpSound);
        }
    }

    // BGM 선택 창에서 고름: 저장하고 바로 들려줌 (실제 적용은 엔드리스 모드에서만)
    public void SelectEndlessBgm(int index)
    {
        PlayerPrefs.SetInt(EndlessBgmKey, index);
        PlayMusic(EndlessMusic);
    }

    void PlayMusic(AudioClip clip)
    {
        if (bgmAudioSource == null || clip == null) return;
        if (bgmAudioSource.clip == clip && bgmAudioSource.isPlaying) return;
        bgmAudioSource.clip = clip;
        bgmAudioSource.loop = true;
        bgmAudioSource.Play();
    }

    public void OnMusicVolumeChanged(float volume)
    {
        ApplyMusicVolume(volume);
        PlayerPrefs.SetFloat("MusicVolume", volume);
    }

    void ApplyMusicVolume(float slider)
    {
        if (bgmAudioSource == null) return;
        if (musicMixer != null && bgmAudioSource.outputAudioMixerGroup != null)
        {
            bgmAudioSource.volume = 1f; // 보스전 등 곡 바꿀 때 페이드는 그대로 AudioSource.volume으로
            float v = Loudness(slider);
            musicMixer.SetFloat("MusicVolume", v > 0.0001f ? 20f * Mathf.Log10(v) : -80f);
        }
        else bgmAudioSource.volume = Loudness(slider);
    }
}