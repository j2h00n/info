# Flappy Runner 3D

갈매기가 앞으로 날아가며 기둥 사이 틈을 통과하는 3D 플래피버드류 게임.
웹캠으로 머리 기울기(조향)와 양팔 파닥임(점프)을 인식하는 모션 컨트롤을 지원함.

이 레포에는 두 가지가 들어있음.

1. **Unity 프로젝트** (`Assets/`, `Packages/`, `ProjectSettings/`) - 원본 게임
2. **Python 포팅** (`python_game/`) - Unity 원본을 Panda3D로 옮긴 버전

> 값(속도, 중력, 간격 등)이 의심스러우면 Unity C# 스크립트(`Assets/New Folder/`)와
> 씬 파일(`Assets/Scenes/SampleScene.unity`)이 기준임.

---

## 빠른 시작 (Python 버전)

### 0. 준비물

- Windows + Python 3.11 (3.11.9에서 확인함, 다른 버전은 미확인) (Git Bash 또는 PowerShell 기준)
- 웹캠 (모션 컨트롤을 쓸 때만 필요, 키보드만 쓰면 없어도 됨)

### 1. 내려받기 + 설치 (최초 1회)

```
git clone https://github.com/j2h00n/info.git
cd info
git checkout kang-ji-hun
python -m venv .venv
.venv/Scripts/python.exe -m pip install -r python_game/requirements.txt
```

PowerShell에서는 `.venv/Scripts/python.exe` 대신 `.venv\Scripts\python.exe`를 써도 됨.

`mediapipe`가 시스템 전역 `numpy`/`protobuf` 버전을 망가뜨리므로 **반드시 `.venv` 안에만 설치**할 것.

> **참고**: 유료 에셋(갈매기 모델 `Seagull.fbx`, 효과음, BGM)은 라이선스 문제로 레포에 없음.
> 없어도 게임은 실행됨 - 갈매기 대신 노란 박스가 나오고 소리만 안 남.

### 2. 실행

레포 루트에서 실행 (`python_game/` 안에서 실행하면 상대 import 때문에 오류남).

```
.venv/Scripts/python.exe -m python_game.main
```

창이 뜨면 시작 메뉴에서 **시작** 버튼을 누르거나 `Space`를 누르면 게임이 시작됨.

**실행이 안 될 때**
- `ModuleNotFoundError`: `.venv` 말고 시스템 Python으로 실행했을 가능성. 위 명령처럼 `.venv/Scripts/python.exe`로 실행.
- `ImportError: attempted relative import`: `python_game/` 폴더 안에서 실행했을 가능성. 레포 루트에서 `-m python_game.main`으로 실행.
- 웹캠 화면이 까맣거나 깨짐: 코드에서 카메라 백엔드/코덱을 건드리지 말고 `M`으로 모션 모드를 끄고 키보드로 플레이.

### 3. 조작법

| 입력 | 동작 |
|------|------|
| `Space` | 시작 / 점프 / 게임오버 후 재시작 |
| `←` `→` | 좌우 기울기(조향) |
| `M` | 웹캠 모션 컨트롤 켜기/끄기 |

**모션 컨트롤**
- 머리를 좌우로 기울이면 조향 (귀-귀 선의 각도 사용)
- 양팔을 같이 위로 휙 올리면 점프(파닥임)
- 화면 우하단에 웹캠 미리보기(스켈레톤 표시)가 뜸

### 4. 난이도

`python_game/main.py`의 `Game(easy=True)`가 기본값이라 **쉬운 모드**로 켜짐.
원본 난이도로 하려면 `main()` 안을 `Game(easy=False)`로 바꾸면 됨.
쉬운 모드 배율은 `bird.py`, `wall.py`의 `easy` 분기에서 조정.

---

## 코드 구조 (`python_game/`)

각 파일은 Unity 스크립트 하나(또는 그 일부)에 대응함. 로직 파일은 Panda3D에 의존하지 않는 순수 Python.

| 파일 | 역할 | Unity 원본 |
|------|------|-----------|
| `main.py` | 실행 진입점. 창/카메라/UI/오디오/매 프레임 루프 | 여러 스크립트 통합 |
| `bird.py` | 중력, 점프, 기울기, 점수 (순수 로직) | `BirdController.cs` |
| `wall.py` | 기둥 줄 생성/삭제, 충돌 판정 | `NewMonoBehaviourScript.cs` |
| `boundary.py` | 좌우 이동 한계, 쉴드 알파 계산 | `BoundaryWall.cs` |
| `motion_capture.py` | 웹캠 + MediaPipe 모션 인식 (게임 프로세스 안에서 실행) | `MotionController.cs` (변경됨) |
| `scenery.py` | 풀/나무/구름 배경 배치 (CC0 모델) | - |
| `seagull_loader.py` | `Seagull.fbx`(ASCII FBX 6.1) 직접 파서 | - |
| `geometry.py` | 박스 메시 생성 (기둥, 바닥, 쉴드용) | `CreatePrimitive(Cube)` |
| `prefs.py` | JSON 저장소 (`save.json`) | `PlayerPrefs` |
| `environment.py` | 대상을 일정 오프셋으로 따라가는 유틸 | `FollowEnvironment.cs` |
| `experiment.py` | 쉬운 모드 진입점 | - |

루트의 `motion_sender.py`는 Unity용 독립 모션 송신기 (UDP 5052로 전송).

### 한 프레임의 흐름 (`Game.update`)

```
입력(키보드/모션) -> bird.update(물리) -> boundary(좌우 한계)
 -> 노드 위치 반영 -> 카메라 -> 기둥/배경 생성·삭제 -> 충돌 판정 -> HUD
```

---

## 직접 수정할 때 알아둘 것

모든 코드에 한국어 주석이 달려있음. 자주 만지는 값은 아래 위치에 있음.

| 바꾸고 싶은 것 | 위치 |
|---------------|------|
| 전진 속도, 점프 힘, 중력, 기울기 | `bird.py` 의 `Bird` 상수 |
| 기둥 개수/폭, 틈 크기, 줄 간격 | `wall.py` 의 `WallSpawner` 상수 |
| 좌우 이동 한계 | `boundary.py` 의 `LIMIT_X` |
| 바닥/천장 높이 | `main.py` 의 `FLOOR_Y`, `CEILING_Y` |
| 카메라 위치/시야각 | `main.py` (`camLens.setFov`, `camera.setPos`) |
| 파닥 감도 | `motion_capture.py` 의 `FLAP_VELOCITY` 등 |
| 조향 감도 | `motion_capture.py` 의 `MAX_TILT_DEG` |
| 배경 밀도 | `scenery.py` 의 `*_PER_CHUNK` |

### 주의사항

- **좌표계**: 게임 로직은 `(x, y=높이, z=전진)`, Panda3D는 `(x, 전진, 높이)`.
  노드에 넣을 때 `setPos(x, z, y)` 순서로 바꿔 넣음. 자세한 건 `python_game/CONVENTIONS.md`.
- **조향 부호**: 머리 각도 -> `move_x` -> roll -> 점프 방향이 한 체인으로 이어짐.
  하나 바꾸면 전부 다시 확인할 것 (과거에 세 번 반대로 만든 적 있음).
- **연동 값**: 기둥 구성(`wall.py`)을 바꾸면 `boundary.py`의 `LIMIT_X`와 `bird.py`의 벽 통과 계산(`15.0`, `5.0`)도 같이 맞춰야 함.
- **Windows 경로**: Panda3D 로더(`loadTexture`, `loadSfx`, `loadFont`)에 경로를 넘길 땐 `Filename.from_os_specific()`으로 변환 필수.
- **웹캠**: `cv2.CAP_DSHOW` 강제 지정이나 `MJPG` 코덱 강제 지정 금지. 이 PC에선 화면이 깨지거나 검게 나옴.
- **검증**: 테스트/린터 없음. 수정 후 `.venv/Scripts/python.exe -m py_compile python_game/*.py`로 문법 확인하고 직접 실행해서 확인.

---

## 의도적으로 포팅하지 않은 것

- Asset Store 유료 배경 에셋 (BOXOPHOBIC 스카이박스, Mellow Fox, Enxemac) - 라이선스 문제
- 갈매기 날개 애니메이션 (원본 FBX 스켈레톤) - 유료 에셋. 대신 날개 메시를 임시로 쪼개 파닥 효과만 줌
- 쉴드 커스텀 셰이더 - 무료 `Hex Tile.png` 텍스처로 대체

이 항목들은 임의로 "고치지" 말고 먼저 확인할 것.

---

## 관련 문서

- `CLAUDE.md` - 개발 가이드/이력 (AI 어시스턴트용)
- `python_game/CONVENTIONS.md` - 좌표/부호 규칙
- `GAME_PLAN.md` - 게임 기획
