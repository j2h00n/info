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
    
    public GameObject restartButton;
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
        
        AudioSource[] audioSources = GetComponents<AudioSource>();
        if (audioSources.Length > 0) jumpAudioSource = audioSources[0];
        
        float savedJumpVol = PlayerPrefs.GetFloat("JumpVolume", 1f);
        float savedMusicVol = PlayerPrefs.GetFloat("MusicVolume", 1f);

        useMotionControl = PlayerPrefs.GetInt("MotionMode", 0) == 1;

        if (jumpAudioSource != null) jumpAudioSource.volume = savedJumpVol;
        if (bgmAudioSource != null) bgmAudioSource.volume = savedMusicVol;

        if (bgmAudioSource != null && bgmClip != null)
        {
            bgmAudioSource.clip = bgmClip;
            bgmAudioSource.loop = true; 
            bgmAudioSource.Play();      
        }

        if(restartButton != null) restartButton.SetActive(false); 
        
        Time.timeScale = 0f; 
        isGameStarted = false;
        
        bestScore = PlayerPrefs.GetInt("BestScore", 0);
        bestWall = PlayerPrefs.GetInt("BestWall", 0);
        
        if(highScoreText != null) highScoreText.text = "High Score\n" + bestScore.ToString();
        if(highWallText != null) highWallText.text = "High Wall\n" + bestWall.ToString();
        
        if(jumpSoundSlider != null) jumpSoundSlider.value = savedJumpVol;
        if(musicSoundSlider != null) musicSoundSlider.value = savedMusicVol;

        if (motionToggle != null)
        {
            motionToggle.isOn = useMotionControl;
        }
    }

    void Update()
    {
        if (!isGameStarted || isGameOver) return; 

        if (Input.GetKeyDown(KeyCode.Space))
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * flapForce, ForceMode.Impulse);
            
            if (jumpAudioSource != null && jumpSound != null)
            {
                jumpAudioSource.PlayOneShot(jumpSound);
            }
        }

        int currentScore = Mathf.Max(0, (int)transform.position.z); 
        int passedWalls = Mathf.Max(0, Mathf.FloorToInt((transform.position.z - 5f) / 15f));
        
        if (scoreText != null && wallText != null)
        {
            scoreText.text = "Score : " + currentScore.ToString();
            wallText.text = "Wall : " + passedWalls.ToString();
        }

        if (currentScore > bestScore)
        {
            bestScore = currentScore;
            PlayerPrefs.SetInt("BestScore", bestScore);
        }
        if (passedWalls > bestWall)
        {
            bestWall = passedWalls;
            PlayerPrefs.SetInt("BestWall", bestWall);
        }

        float moveX = Input.GetAxis("Horizontal"); 
        if (useMotionControl)
        {
            MotionController motion = GetComponent<MotionController>();
            if (motion != null) moveX = motion.GetMotionMoveX();
        }

        Quaternion targetRotation = Quaternion.Euler(0f, 0f, -moveX * tiltAngle);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * tiltSpeed);
    }

    void FixedUpdate()
    {
        if (!isGameStarted || isGameOver) return; 
        
        float moveX = Input.GetAxis("Horizontal"); 
        
        if (useMotionControl)
        {
            MotionController motion = GetComponent<MotionController>();
            if (motion != null)
            {
                float motionMoveX = motion.GetMotionMoveX();
                moveX = Mathf.Abs(moveX) > Mathf.Abs(motionMoveX) ? moveX : motionMoveX;
            }
        }
        
        moveX = Mathf.Clamp(moveX, -1f, 1f);
        
        // 🔥 갈매기가 굳지 않고 부드럽게 움직이도록 물리 속도 적용
        rb.linearVelocity = new Vector3(moveX * sideSpeed, rb.linearVelocity.y, forwardSpeed);
    }

    public void TriggerJump()
    {
        if (!isGameStarted || isGameOver) return;
        if (!useMotionControl) return; 

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * flapForce, ForceMode.Impulse);
        if (jumpAudioSource != null && jumpSound != null)
        {
            jumpAudioSource.PlayOneShot(jumpSound);
        }
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

        if (collision.gameObject.name.Contains("Cube"))
        {
            isGameOver = true; 
            if(restartButton != null) restartButton.SetActive(true); 
        }
    }
    
    public void RestartGame()
    {
        Time.timeScale = 1f; 
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    
    public void StartGame()
    {
        isGameStarted = true;
        Time.timeScale = 1f; 
        startTime = Time.time; 
        if(startMenuPanel != null) startMenuPanel.SetActive(false); 
    }
    
    public void OnJumpVolumeChanged(float volume)
    {
        if(jumpAudioSource != null) jumpAudioSource.volume = volume;
        PlayerPrefs.SetFloat("JumpVolume", volume);
    }

    public void OnMusicVolumeChanged(float volume)
    {
        if(bgmAudioSource != null) bgmAudioSource.volume = volume;
        PlayerPrefs.SetFloat("MusicVolume", volume);
    }
}