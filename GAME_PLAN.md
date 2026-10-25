# 작업 과정 요약

Unity 3D 플래피버드류 게임(갈매기가 전진하며 기둥 사이 틈을 통과)을 Python(Panda3D)으로 포팅하고,
웹캠 모션 조작(머리 기울기 조향 + 양팔 파닥임 점프)을 추가한 프로젝트의 진행 기록.

- 저장소: https://github.com/j2h00n/info (작업 브랜치 `kang-ji-hun`)
- 실행 방법과 코드 구조는 `README.md` 참고

---

## 1. 분석 및 설계

- Unity 원본(`Assets/`) 전수 조사: 씬 파일(`SampleScene.unity`) 파싱, 커스텀 스크립트 6개 로직 확인
  (`BirdController`, `BoundaryWall`, `FollowEnvironment`, `MotionController`, `NewMonoBehaviourScript`, `PythonLauncher`)
- 물리 수치(중력, 점프력, 이동속도, 틸트각), 카메라(FOV/오프셋), UI 레이아웃(RectTransform), 오디오 파일을 원본에서 추출
- 원본의 함정 발견: 기둥 생성 로직이 `BoundaryWall.cs`가 아니라 `NewMonoBehaviourScript.cs`에 있음
- 스택 결정: Python + Panda3D(렌더링, 물리 직접 구현) + OpenCV/MediaPipe(모션 인식)
- 라이선스 경계: Unity 유료 애셋(BOXOPHOBIC, Mellow Fox, SailCharacterPack 등)은 이식하지 않고,
  배경/장식은 CC0 애셋으로 대체

## 2. 핵심 게임 로직 이식

| 파일 | 내용 |
|------|------|
| `bird.py` | 중력/점프/전진/기울기 물리, 점수·최고기록 (Panda3D 비의존 순수 로직) |
| `wall.py` | 컬럼형 기둥 스포너(랜덤 틈 위치), 줄 단위 생성/제거, 충돌 판정 |
| `boundary.py` | 좌우 이동범위 제한 + 경계 쉴드 페이드 |
| `geometry.py` | 박스 메시 즉석 생성 (기둥/쉴드/바닥) |
| `seagull_loader.py` | 원본 갈매기 FBX(ASCII 6.1 구버전)를 직접 파싱해 지오메트리 재구성, 날개/몸통 분리 |
| `prefs.py` | Unity `PlayerPrefs` 대체 (JSON 저장) |
| `main.py` | Panda3D 앱: 씬/조명/카메라/입력/게임 루프 통합 |

## 3. 비주얼 / 오디오 / UI

- 카메라 구도(FOV, 위치, 각도): 원본 값 반영 후 실제 플레이 화면에 맞게 재조정
- UI: 시작 화면(최고기록, 음량 슬라이더, 모션 토글, 시작 버튼), 인게임 HUD(점수/통과 수), 재시작 버튼.
  원본 RectTransform 좌표를 Panda 좌표계로 환산해 배치
- 오디오: 점프 효과음 + 배경음악, 볼륨 설정 저장
- 배경: CC0 3D 애셋(풀/나무/구름)을 절차적으로 배치 (`scenery.py`)
- 쉴드 이펙트: 무료 텍스처(`Hex Tile.png`)로 근접 구현
- Windows 경로 문제로 텍스처/오디오 로딩이 실패하던 것 수정 (`Filename.from_os_specific`)

## 4. 모션 인식

- 1차: 배경 제거(MOG2) 기반 움직임 추적 -> 부정확해서 폐기
- 2차: MediaPipe Pose 도입 (코/귀/손목 랜드마크)
- 조향: "머리 위치" -> "머리 기울기 각도"(귀-귀 선)로 재설계
- 점프: "머리 Y속도 스파이크" -> "양팔 파닥임"으로 재설계
  - 손목 수직속도 EMA 스무딩, 양손 동시성 검사, 히스테리시스(armed 래치)로 오탐/중복 점프 방지
- 게임 내장형(`motion_capture.py`): 별도 프로세스/UDP 없이 게임 창 안에 웹캠 미리보기 표시
- Unity 연동: 독립 송신기 `motion_sender.py` (UDP 5052, `x,y,angleDeg,jump`) + `MotionController.cs` 수정

## 5. 밸런싱 / 버그 수정 / 실험 모드

- 난이도(기둥 간격, 틈 크기, 전진속도) 튜닝, 화면 프레이밍에 맞춰 가로 기둥 수 3개로 재조정
- 저공비행으로 기둥 밑을 그냥 통과하는 버그 -> 바닥/천장 충돌 판정 추가
- 실험 모드(`experiment.py`, `easy=True`): 같은 로직에서 난이도만 낮춘 진입점
- 좌우 반전, 물리 방향 부호 등 조작 버그 수정 -> 재발 방지용 `python_game/CONVENTIONS.md` 작성
- 웹캠 이슈: `CAP_DSHOW`/`MJPG` 강제 지정 시 화면이 깨지거나 검게 나옴 -> 기본 `VideoCapture(0)`만 사용
- `mediapipe`가 전역 `numpy`/`protobuf`를 망가뜨림 -> 의존성을 `.venv`로 격리

## 6. 코드 주석 및 문서화 (최근 작업)

- `python_game/` 전 모듈과 `motion_sender.py`에 한국어 주석 추가 (로직 변경 없음, 컴파일 확인)
  - 튜닝 상수마다 "바꾸면 무엇이 달라지는지" 설명
  - Panda3D 좌표 변환(`setPos(x, z, y)`) 주의사항
  - 모션 파닥 판정 흐름(속도 -> 동시성 -> armed 래치) 단계별 설명
- 한국어 `README.md` 신규: 설치/실행/조작법/코드 구조/수정 가이드/문제 해결
- `git clone`부터 실행까지의 단계, 자주 나는 오류 대처법 추가

## 7. 커밋 이력 (`kang-ji-hun`)

| 커밋 | 내용 |
|------|------|
| `f88f5be` | Unity 게임을 Python(Panda3D)으로 포팅 |
| `4eea560` | 쉴드 박스 텍스처(UV) 지원, Hex Tile 적용 |
| `ca3ad1f` | Windows 경로 문제로 텍스처/오디오 로딩 실패 수정 |
| `b562155` | 배경 CC0 애셋, 모션 인식 재설계(각도 조향 + 파닥 점프), Unity 연동 |
| `cbf95db` | venv 격리, 좌표/부호 컨벤션 문서화 |
| `640c5e7` | 한국어 주석 대폭 추가, README 신규, 모션 로직 갱신 |
| `8766b04` | GitHub에서 `kang-ji-hun.md` 삭제 (테스트용 파일) |
| `495e0dd` | README에 실행 방법 단계별 안내 추가 |

## 8. 의도적으로 하지 않은 것

- 유료 배경 에셋 이식, 갈매기 본래 날개 애니메이션, 쉴드 커스텀 셰이더
  (라이선스/복잡도 문제로 제외, 대체 구현으로 근접)
- 테스트 코드와 린터: 없음. `py_compile`로 문법만 확인하고 직접 실행해서 검증
