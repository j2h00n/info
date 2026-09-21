using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;
using System.IO;

public class PythonLauncher : MonoBehaviour
{
    private static Process pythonProcess; // static으로 유지하여 중복 실행 방지
    public Toggle motionToggle; 
    private bool lastToggleState = false;

    void Start()
    {
        int savedMode = PlayerPrefs.GetInt("MotionMode", 0);
        bool initialOn = (savedMode == 1);

        if (motionToggle != null)
        {
            motionToggle.SetIsOnWithoutNotify(initialOn);
        }

        lastToggleState = initialOn;
        
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
    }

    void StartPython()
    {
        if (pythonProcess != null && !pythonProcess.HasExited) return;

        try
        {
            string path = Path.Combine(Application.dataPath, "../motion_tracker.py");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{path}\"",
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
                pythonProcess = null;
                UnityEngine.Debug.Log("파이썬 모션 트래커 꺼짐!");
            }
        }
        catch { }
    }

    void OnApplicationQuit()
    {
        KillPython();
    }

    void OnDestroy()
    {
        KillPython();
    }
}