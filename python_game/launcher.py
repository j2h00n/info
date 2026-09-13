"""PythonLauncher.cs 대체: motion_tracker.py 웹캠 프로세스를 백그라운드로 켜고 끔."""
import os
import subprocess
import sys

_process = None


def start_tracker():
    global _process
    if _process is not None and _process.poll() is None:
        return
    path = os.path.join(os.path.dirname(os.path.dirname(__file__)), "motion_tracker.py")
    try:
        _process = subprocess.Popen(
            [sys.executable, path],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        print("모션 트래커 백그라운드 켜짐!")
    except Exception as e:
        print("모션 트래커 실행 실패:", e)


def kill_tracker():
    global _process
    if _process is not None and _process.poll() is None:
        _process.terminate()
        _process = None
        print("모션 트래커 꺼짐!")
