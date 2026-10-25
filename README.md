# 갈매기 비행 (Unity)

Unity 6000.5.10f1 프로젝트. 스테이지 1~4 + 엔딩. 모션 컨트롤(웹캠)은 선택 기능.

## GitHub에 올리는 것 / 따로 공유하는 것

### GitHub에 올릴 것 (소스)

- `Assets/` : 스크립트, 씬, 프리팹, 머티리얼, 오디오, 모델 등 (`.meta` 파일 반드시 포함)
- `Packages/` : `manifest.json`, `packages-lock.json`
- `ProjectSettings/`
- `motion_tracker.py` : 모션 컨트롤 트래커
- 모델 파일 : `hand_landmarker.task`, `blaze_face_short_range.tflite`, `haarcascade_frontalface_default.xml`
- `make_*.py` : BGM/SFX 생성 스크립트 (선택)
- `.gitignore`, `README.md`

### GitHub에 올리지 않을 것

- Unity 자동 생성 : `Library/`, `Temp/`, `Logs/`, `obj/`, `UserSettings/`, `*.csproj`, `*.sln`
- 빌드 결과 : `Build/`, `Builds/`
- 파이썬 환경 : `.venv/`, `__pycache__/`, `python_runtime/`
- 게임과 무관한 파일 : `python_game/`, `main.py`, `*.mp4`
- 개인 에디터 설정 : `.vscode/`

### 따로 공유할 것 (Drive, Discord, GitHub Releases)

- 빌드한 exe 게임 폴더 (zip으로 압축)
- `python_runtime/` : 모션 컨트롤용 번들 파이썬 (OpenCV + MediaPipe). exe 옆에 두고 함께 전달.
- 100MB를 넘는 큰 에셋 : Git LFS를 쓰거나 따로 공유. GitHub는 파일 하나가 100MB를 넘으면 push를 거부함.

### 커밋 전 확인

- Unity가 자동으로 바꾸는 파일은 내용을 확인하고 커밋 여부를 결정함.
  - `ProjectSettings/*.asset`, `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`
- 빈 폴더용 `.meta` 파일은 올리지 않아도 됨.
- `Library/`가 없어도 Unity가 다시 만들기 때문에 팀원은 처음 열 때 시간이 걸릴 뿐 문제는 없음.

## 실행 방법

1. Unity Hub에서 이 폴더를 프로젝트로 추가. 에디터 버전은 `6000.5.10f1`.
2. `Assets/Scenes/`의 씬을 열고 Play.
3. 모션 컨트롤을 쓰려면 웹캠과 파이썬이 필요함. `python_runtime/python.exe`가 있으면 그걸 쓰고, 없으면 PATH의 `python`을 씀.
   ```
   pip install opencv-python mediapipe
   ```
